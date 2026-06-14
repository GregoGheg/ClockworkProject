using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class LevelViewController : MonoBehaviour
{
    WorldLevelConfig.LevelEntry entry;
    int index;
    WorldNavigator navigator;
    GameObject levelPrefab;
    GameManager gameManagerPrefab;

    GameManager gameManager;
    CanvasGroup canvasGroup;

    public bool IsInitialized { get; private set; } = false;

    public void Init(WorldLevelConfig.LevelEntry e, int i,
                     WorldNavigator nav, GameObject lvlPrefab, GameManager gmPrefab)
    {
        entry = e;
        index = i;
        navigator = nav;
        levelPrefab = lvlPrefab;
        gameManagerPrefab = gmPrefab;
    }

    /// <summary>Inizializza il livello senza attivarlo — per il preload.</summary>
    public void EnsureInitialized(LevelSaveData save)
    {
        if (IsInitialized) return;
        SpawnLevel();
        IsInitialized = true;
        if (save != null && save.pieces.Count > 0)
            gameManager?.RestoreSaveData(save);
    }

    public void ActivateLevel(LevelSaveData save)
    {
        EnsureInitialized(save);
        gameObject.SetActive(true);
        SetInputEnabled(true);

        // Spawn dei preset SOLO ora: il GameObject è attivo, quindi Awake della
        // griglia è girato e l'array delle celle è allocato (grid.IsReady == true).
        var spawner = gameManager != null
            ? gameManager.GetComponent<LevelPresetSpawner>() : null;
        spawner?.SpawnNow();
    }

    public void SetInputEnabled(bool enabled)
    {
        if (canvasGroup == null)
            canvasGroup = GetComponentInChildren<CanvasGroup>(true);
        if (canvasGroup != null)
        {
            canvasGroup.blocksRaycasts = enabled;
            canvasGroup.interactable = enabled;
            return;
        }
        foreach (var dragger in GetComponentsInChildren<PieceDragger>(true))
            dragger.enabled = enabled;
    }

    /// <summary>Aggiorna il tray con le quantità calcolate dal pool globale.</summary>
    public void RefreshTray()
    {
        if (gameManager == null || navigator == null) return;
        foreach (var entry in navigator.config.globalPieces)
        {
            if (entry.data == null) continue;
            int available = navigator.GetAvailable(entry.data)
                          + CountPlaced(entry.data); // quelli in questo livello sono "disponibili" qui
            gameManager.SetTrayCount(entry.data, available);
        }
    }

    void SpawnLevel()
    {
        canvasGroup = gameObject.AddComponent<CanvasGroup>();

        GameObject levelInstance = null;
        if (levelPrefab != null)
        {
            levelInstance = Instantiate(levelPrefab, transform);
            levelInstance.SetActive(false); // blocca Awake finché level non è assegnato
            levelInstance.transform.localPosition = Vector3.zero;
        }

        if (gameManagerPrefab == null) return;
        gameManager = Instantiate(gameManagerPrefab, transform);

        gameManager.currentLevel = entry.levelData;
        gameManager.worldNavigator = navigator;
        gameManager.levelIndex = index;
        gameManager.onLevelSolved += () => navigator.OnLevelSolved(index);

        if (levelInstance == null) return;

        var gridMgr = levelInstance.GetComponentInChildren<GridManager>(true);
        if (gridMgr != null)
        {
            gridMgr.level = entry.levelData;
            gridMgr.cellSize = entry.levelData.cellSize;
            gameManager.gridManager = gridMgr;
            // ApplyLayout centra la griglia DOPO che cellSize è noto
            // (Awake viene chiamato subito dopo SetActive(true) qui sotto)
        }
        levelInstance.SetActive(true); // ora Awake trova level già assegnato
        // cellSize è già impostato → ApplyLayout può centrare correttamente
        gridMgr?.ApplyLayout();

        var scrollRect = levelInstance.GetComponentInChildren<ScrollRect>();
        if (scrollRect != null) gameManager.trayContainer = scrollRect.content;

        foreach (var btn in levelInstance.GetComponentsInChildren<Button>(true))
            if (btn.name.ToLower().Contains("undo"))
            { gameManager.undoButton = btn; break; }

        foreach (Transform child in levelInstance.transform)
            if (child.name.ToLower().Contains("vittoria") || child.name.ToLower().Contains("win"))
            { gameManager.winPanel = child.gameObject; break; }
    }

    /// <summary>Conta i pezzi di un tipo piazzati in questo livello.</summary>
    public int CountPlaced(PieceData data)
    {
        if (gameManager?.gridManager == null) return 0;
        int count = 0;
        foreach (var piece in gameManager.gridManager.PlacedPieces)
            if (piece.data == data && piece.gridPosition.x >= 0 && !piece.isPreset) count++;
        // Le cinghie non sono in PlacedPieces (non occupano celle): contale dai dragger
        foreach (var d in GetComponentsInChildren<PieceDragger>(true))
            if (d.isBelt && d.piece?.data == data && d.piece.gridPosition.x >= 0 && !d.piece.isPreset)
                count++;
        return count;
    }

    public LevelSaveData SaveLevel()
    {
        var save = new LevelSaveData { solved = navigator.IsSolved(index) };
        if (gameManager?.gridManager == null) return save;

        foreach (var piece in gameManager.gridManager.PlacedPieces)
        {
            if (piece.gridPosition.x < 0) continue;
            if (piece.isPreset) continue; // i preset vengono rispawnati dal LevelPresetSpawner
            save.pieces.Add(new LevelSaveData.PlacedPieceData
            {
                pieceDataName = piece.data.name,
                gridPosition = piece.gridPosition,
                rotation = piece.rotation,
                runtimeLength = piece.runtimeLength ?? piece.data.cells.Count
            });
        }

        // ── Salva anche le cinghie: non sono in PlacedPieces ─────────────
        foreach (var d in GetComponentsInChildren<PieceDragger>(true))
        {
            if (!d.isBelt) continue;
            if (d.piece == null || d.piece.gridPosition.x < 0) continue;
            if (d.piece.isPreset) continue;
            save.pieces.Add(new LevelSaveData.PlacedPieceData
            {
                pieceDataName = d.piece.data.name,
                gridPosition = d.piece.gridPosition,
                rotation = d.piece.rotation,
                runtimeLength = d.piece.runtimeLength ?? d.piece.data.cells.Count,
                isBelt = true,
                beltEndCell = d.beltEndCell,
            });
        }
        return save;
    }
    /// <summary>
    /// Ritorna true se il circuito del livello corrente è attualmente risolto.
    /// Richiamato da WorldNavigator prima di permettere la navigazione.
    /// </summary>
    public bool IsCurrentlySolved()
    {
        if (gameManager?.gridManager == null) return false;
        return CircuitSolver.SolveAll(gameManager.gridManager);
    }

}