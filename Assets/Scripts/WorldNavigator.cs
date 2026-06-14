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
                entry.mapPosition.x * entry.levelData.nodeSpacing,
                entry.mapPosition.y * entry.levelData.nodeSpacing);

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

        // 1) Livello esattamente adiacente in quella direzione (comportamento classico)
        var target = curPos + direction;
        for (int i = 0; i < config.levels.Length; i++)
            if (config.levels[i].mapPosition == target) { NavigateTo(i); return; }

        // 2) Uscita verso una mapPosition NON adiacente: cerca una destinazione
        //    soddisfatta la cui zona-target sta in questa direzione.
        int idx = GetUnlockedTargetInDirection(direction);
        if (idx >= 0) NavigateTo(idx);
    }

    /// <summary>
    /// Cerca tra le destinazioni del livello corrente quella soddisfatta la cui
    /// mapPosition di sblocco si trova nella direzione data (anche non adiacente).
    /// Restituisce l'indice del livello target, o -1.
    /// </summary>
    public int GetUnlockedTargetInDirection(Vector2Int direction)
    {
        var grid = CurrentGrid();
        if (grid == null || grid.level == null) return -1;
        var curPos = config.levels[currentIndex].mapPosition;

        foreach (var d in grid.level.GetDestinations())
        {
            if (!d.HasUnlock) continue;
            var targetPos = d.unlocksLevelAtMapPosition;
            var delta = targetPos - curPos;
            // La direzione deve combaciare (stesso segno sull'asse dominante)
            if (!SameDirection(delta, direction)) continue;
            if (!CircuitSolver.IsDestinationSatisfied(grid, d)) continue;
            // Trova il livello con quella mapPosition
            for (int i = 0; i < config.levels.Length; i++)
                if (config.levels[i].mapPosition == targetPos) return i;
        }
        return -1;
    }

    /// <summary>Vero se delta punta nella stessa direzione cardinale di dir.</summary>
    static bool SameDirection(Vector2Int delta, Vector2Int dir)
    {
        if (dir == Vector2Int.right) return delta.x > 0 && delta.y == 0;
        if (dir == Vector2Int.left) return delta.x < 0 && delta.y == 0;
        if (dir == Vector2Int.up) return delta.y > 0 && delta.x == 0;
        if (dir == Vector2Int.down) return delta.y < 0 && delta.x == 0;
        return false;
    }

    public void NavigateTo(int index)
    {
        if (index < 0 || index >= config.levels.Length) return;
        if (index == currentIndex || isTransitioning) return;

        // "Indietro" = tornare al parent nel tree di navigazione
        bool isGoingBack = navigationParent.ContainsKey(currentIndex)
                        && navigationParent[currentIndex] == index;

        // Indietro: sempre libero.
        // Avanti: permesso se la zona target è sbloccata dalla SUA destinazione
        // (indipendente dalle altre uscite), con fallback al circuito completo.
        if (!isGoingBack)
        {
            var targetMapPos = config.levels[index].mapPosition;
            bool zoneUnlocked = IsZoneUnlocked(targetMapPos);
            bool fullySolved = levelViews[currentIndex].IsCurrentlySolved();
            if (!zoneUnlocked && !fullySolved) return;
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
            -entry.mapPosition.x * entry.levelData.nodeSpacing,
            -entry.mapPosition.y * entry.levelData.nodeSpacing);
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

    // ── Zone sbloccate dalle destinazioni di energia ──────────────────────
    // Una destinazione soddisfatta sblocca l'accesso al livello la cui
    // mapPosition è dichiarata nel LevelData (EnergyDestination.unlocksLevelAtMapPosition).
    readonly HashSet<Vector2Int> unlockedZones = new();

    /// <summary>Chiamato da GameManager quando una destinazione è soddisfatta.</summary>
    public void UnlockZone(Vector2Int mapPosition)
    {
        if (mapPosition == new Vector2Int(9999, 9999)) return;
        unlockedZones.Add(mapPosition);
    }

    /// <summary>
    /// Una zona è accessibile SOLO se, in questo momento, una destinazione del
    /// livello corrente che punta a quella mapPosition è soddisfatta.
    /// Check live e indipendente per ogni uscita: se il percorso si disattiva,
    /// la freccia sparisce; ogni uscita si sblocca da sola senza le altre.
    /// </summary>
    public bool IsZoneUnlocked(Vector2Int mapPosition)
    {
        var grid = CurrentGrid();
        if (grid == null || grid.level == null) return false;

        foreach (var d in grid.level.GetDestinations())
        {
            if (!d.HasUnlock) continue;
            if (d.unlocksLevelAtMapPosition != mapPosition) continue;
            if (CircuitSolver.IsDestinationSatisfied(grid, d)) return true;
        }
        return false;
    }

    GridManager CurrentGrid()
    {
        if (levelViews == null || currentIndex < 0 || currentIndex >= levelViews.Count) return null;
        return levelViews[currentIndex].GetComponentInChildren<GridManager>(true);
    }

    /// <summary>Indice del livello con quella mapPosition, o -1.</summary>
    public int IndexOfMapPosition(Vector2Int mapPosition)
    {
        for (int i = 0; i < config.levels.Length; i++)
            if (config.levels[i].mapPosition == mapPosition) return i;
        return -1;
    }

    void UpdateUI()
    {
        if (labelLevelName != null) labelLevelName.text = config.levels[currentIndex].displayName;
        bool solved = saveData[currentIndex].solved;
        if (labelSolvedIcon != null) labelSolvedIcon.color = solved ? colorSolved : colorUnsolved;
    }

    // Bauli già aperti — chiave: "levelIndex_gridX_gridY"
    readonly HashSet<string> openedChests = new();

    public int CurrentIndex => currentIndex;
    public LevelSaveData GetSaveData(int i) => saveData.ContainsKey(i) ? saveData[i] : null;
    public bool IsSolved(int i) => saveData.ContainsKey(i) && saveData[i].solved;
    public bool HasBeenVisited(int i) => visited.Contains(i);

    /// <summary>
    /// Chiamato da GameManager quando rileva un baule aperto.
    /// Se il baule non era già stato aperto, aggiunge i premi al pool globale
    /// e aggiorna il tray del livello corrente.
    /// </summary>
    public void OnChestOpened(int levelIdx, Vector2Int chestPos, PieceData chestData)
    {
        var key = $"{levelIdx}_{chestPos.x}_{chestPos.y}";
        if (openedChests.Contains(key)) return; // già aperto, non dare premi di nuovo
        openedChests.Add(key);

        // Aggiungi i premi al pool globale
        foreach (var reward in chestData.chestRewards)
        {
            if (reward.data == null) continue;
            var existing = config.globalPieces.Find(g => g.data == reward.data);
            if (existing != null)
                existing.quantity += reward.quantity;
            else
                config.globalPieces.Add(new WorldLevelConfig.GlobalPieceEntry
                { data = reward.data, quantity = reward.quantity });
        }

        // Aggiorna il tray di tutti i livelli inizializzati
        foreach (var view in levelViews)
            view.RefreshTray();
    }

    public bool IsChestOpen(int levelIdx, Vector2Int chestPos)
        => openedChests.Contains($"{levelIdx}_{chestPos.x}_{chestPos.y}");

    /// <summary>Ritorna true se il circuito del livello corrente è attivo in questo momento.</summary>
    public bool IsCurrentLevelSolved() => levelViews[currentIndex].IsCurrentlySolved();

    /// <summary>Ritorna true se candidateParent è il parent di child nel tree di navigazione.</summary>
    public bool IsParent(int candidateParent, int child)
        => navigationParent.ContainsKey(child) && navigationParent[child] == candidateParent;
}