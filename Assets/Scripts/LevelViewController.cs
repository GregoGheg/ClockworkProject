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
            levelInstance.transform.localPosition = Vector3.zero;
        }

        if (gameManagerPrefab == null) return;
        gameManager = Instantiate(gameManagerPrefab, transform);

        gameManager.currentLevel = entry.levelData;
        gameManager.worldNavigator = navigator;
        gameManager.levelIndex = index;
        gameManager.onLevelSolved += () => navigator.OnLevelSolved(index);

        if (levelInstance == null) return;

        var gridMgr = levelInstance.GetComponentInChildren<GridManager>();
        if (gridMgr != null) gameManager.gridManager = gridMgr;

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
            if (piece.data == data && piece.gridPosition.x >= 0) count++;
        return count;
    }

    public LevelSaveData SaveLevel()
    {
        var save = new LevelSaveData { solved = navigator.IsSolved(index) };
        if (gameManager?.gridManager == null) return save;

        foreach (var piece in gameManager.gridManager.PlacedPieces)
        {
            if (piece.gridPosition.x < 0) continue;
            save.pieces.Add(new LevelSaveData.PlacedPieceData
            {
                pieceDataName = piece.data.name,
                gridPosition = piece.gridPosition,
                rotation = piece.rotation,
                runtimeLength = piece.runtimeLength ?? piece.data.cells.Count
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
        var gm = gameManager;
        return CircuitSolver.Solve(
            gm.gridManager,
            gm.currentLevel.circuitSource,
            gm.currentLevel.circuitDestination);
    }

}