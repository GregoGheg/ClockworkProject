using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class WorldNavigator : MonoBehaviour
{
    [Header("Configurazione")]
    public WorldLevelConfig config;

    [Header("Prefab")]
    public GameObject levelPrefab;
    public GameManager gameManagerPrefab;

    [Header("Navigazione")]
    public float transitionDuration = 0.5f;
    public AnimationCurve transitionCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("UI opzionale")]
    public Text labelLevelName;
    public Image labelSolvedIcon;
    public Color colorSolved = new Color(0.2f, 0.9f, 0.3f);
    public Color colorUnsolved = new Color(0.9f, 0.3f, 0.2f);

    // ── Stato ─────────────────────────────────────────────────────────────
    int currentIndex = 0;
    // Albero di navigazione: per ogni livello visitato, da quale livello ci si è arrivati
    // Il livello iniziale ha parent -1 (radice)
    Dictionary<int, int> navigationParent = new(); // index → parent index
    bool isTransitioning = false;
    RectTransform worldRect;
    List<LevelViewController> levelViews = new();
    Dictionary<int, LevelSaveData> saveData = new();
    HashSet<int> visited = new();

    // Pool globale: PieceData → quantità disponibile
    public Dictionary<PieceData, int> GlobalInventory { get; private set; } = new();

    void Awake() => worldRect = GetComponent<RectTransform>();

    void Start()
    {
        if (config == null) { Debug.LogError("[WorldNavigator] Config non assegnato!"); return; }
        if (gameManagerPrefab == null) { Debug.LogError("[WorldNavigator] GameManager prefab non assegnato!"); return; }

        InitGlobalInventory();
        BuildWorld();
        currentIndex = Mathf.Clamp(config.startLevelIndex, 0, config.levels.Length - 1);
        navigationParent[currentIndex] = -1; // radice dell'albero
        SnapToLevel(currentIndex);
        LoadLevel(currentIndex);
    }

    void Update()
    {
        if (isTransitioning) return;
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.leftArrowKey.wasPressedThisFrame) TryNavigate(Vector2Int.left);
        if (kb.rightArrowKey.wasPressedThisFrame) TryNavigate(Vector2Int.right);
        if (kb.upArrowKey.wasPressedThisFrame) TryNavigate(Vector2Int.up);
        if (kb.downArrowKey.wasPressedThisFrame) TryNavigate(Vector2Int.down);
    }

    // ── Inventario globale ────────────────────────────────────────────────
    void InitGlobalInventory()
    {
        if (GlobalInventory.Count > 0) return; // già inizializzato, non toccare
        foreach (var entry in config.globalPieces)
            if (entry.data != null)
                GlobalInventory[entry.data] = entry.quantity;
    }

    /// <summary>
    /// Ricalcola quanti pezzi di ogni tipo sono disponibili nel pool globale,
    /// sottraendo quelli già piazzati in tutti i livelli.
    /// </summary>
    public int GetAvailable(PieceData data)
    {
        if (!GlobalInventory.ContainsKey(data)) return 0;
        int total = GlobalInventory[data];
        // Sottrai quelli piazzati in tutti i livelli
        foreach (var view in levelViews)
            total -= view.CountPlaced(data);
        return Mathf.Max(0, total);
    }

    /// <summary>Aggiorna i tray di tutti i livelli.</summary>
    public void NotifyInventoryChanged()
    {
        foreach (var view in levelViews) view.RefreshTray();
    }

    // ── Costruzione mondo ─────────────────────────────────────────────────
    void BuildWorld()
    {
        for (int i = 0; i < config.levels.Length; i++)
        {
            var entry = config.levels[i];
            var go = new GameObject($"Level_{i}_{entry.displayName}");
            go.transform.SetParent(transform, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(
                entry.mapPosition.x * config.nodeSpacing,
                entry.mapPosition.y * config.nodeSpacing);

            var view = go.AddComponent<LevelViewController>();
            view.Init(entry, i, this, levelPrefab, gameManagerPrefab);
            levelViews.Add(view);
            saveData[i] = new LevelSaveData();
            go.SetActive(false);
        }

        // Pre-inizializza tutti i livelli subito — così sono già pronti quando ci arrivi
        for (int i = 0; i < levelViews.Count; i++)
            levelViews[i].EnsureInitialized(saveData[i]);
    }

    // ── Navigazione ───────────────────────────────────────────────────────
    public void TryNavigate(Vector2Int direction)
    {
        if (isTransitioning) return;
        var curPos = config.levels[currentIndex].mapPosition;
        var target = curPos + direction;
        for (int i = 0; i < config.levels.Length; i++)
            if (config.levels[i].mapPosition == target) { NavigateTo(i); return; }
    }

    public void NavigateTo(int index)
    {
        if (index < 0 || index >= config.levels.Length) return;
        if (index == currentIndex || isTransitioning) return;

        // "Indietro" = tornare al parent nel tree di navigazione
        bool isGoingBack = navigationParent.ContainsKey(currentIndex)
                        && navigationParent[currentIndex] == index;

        // Indietro: sempre libero. Avanti: solo se il circuito è attualmente attivo
        if (!isGoingBack)
        {
            if (!levelViews[currentIndex].IsCurrentlySolved()) return;
        }

        // Registra nel tree: se il livello target non ha ancora un parent, impostalo
        // (se torna indietro e poi prende un altro ramo, il parent rimane quello originale)
        if (!navigationParent.ContainsKey(index))
            navigationParent[index] = currentIndex;

        SaveCurrentLevel();
        StartCoroutine(TransitionTo(index));
    }

    IEnumerator TransitionTo(int targetIndex)
    {
        isTransitioning = true;
        levelViews[currentIndex].SetInputEnabled(false);

        // Pre-spawna il livello target se non ancora inizializzato
        levelViews[targetIndex].EnsureInitialized(saveData[targetIndex]);

        var startPos = worldRect.anchoredPosition;
        var endPos = GetWorldPositionFor(targetIndex);
        float elapsed = 0f;

        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            float t = transitionCurve.Evaluate(Mathf.Clamp01(elapsed / transitionDuration));
            worldRect.anchoredPosition = Vector2.Lerp(startPos, endPos, t);
            yield return null;
        }

        worldRect.anchoredPosition = endPos;
        levelViews[currentIndex].SetInputEnabled(true);
        currentIndex = targetIndex;
        isTransitioning = false;
        LoadLevel(currentIndex);
        UpdateUI();
    }

    void SnapToLevel(int index)
    {
        worldRect.anchoredPosition = GetWorldPositionFor(index);
        UpdateUI();
    }

    Vector2 GetWorldPositionFor(int index)
    {
        var entry = config.levels[index];
        return new Vector2(
            -entry.mapPosition.x * config.nodeSpacing,
            -entry.mapPosition.y * config.nodeSpacing);
    }

    // ── Stato livelli ─────────────────────────────────────────────────────
    void LoadLevel(int index)
    {
        visited.Add(index);
        levelViews[index].ActivateLevel(saveData[index]);
    }

    void SaveCurrentLevel() => saveData[currentIndex] = levelViews[currentIndex].SaveLevel();

    public void OnLevelSolved(int index)
    {
        saveData[index].solved = true;
        UpdateUI();
    }

    void UpdateUI()
    {
        if (labelLevelName != null) labelLevelName.text = config.levels[currentIndex].displayName;
        bool solved = saveData[currentIndex].solved;
        if (labelSolvedIcon != null) labelSolvedIcon.color = solved ? colorSolved : colorUnsolved;
    }

    public int CurrentIndex => currentIndex;
    public LevelSaveData GetSaveData(int i) => saveData.ContainsKey(i) ? saveData[i] : null;
    public bool IsSolved(int i) => saveData.ContainsKey(i) && saveData[i].solved;
    public bool HasBeenVisited(int i) => visited.Contains(i);

    /// <summary>Ritorna true se il circuito del livello corrente è attivo in questo momento.</summary>
    public bool IsCurrentLevelSolved() => levelViews[currentIndex].IsCurrentlySolved();

    /// <summary>Ritorna true se candidateParent è il parent di child nel tree di navigazione.</summary>
    public bool IsParent(int candidateParent, int child)
        => navigationParent.ContainsKey(child) && navigationParent[child] == candidateParent;
}