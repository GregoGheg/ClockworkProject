using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Scene Refs")]
    public GridManager gridManager;
    public Transform trayContainer;
    public PieceDragger piecePrefab;

    [Header("Level")]
    public LevelData currentLevel;

    [Header("UI")]
    public GameObject winPanel;
    public Button undoButton;

    [Header("Scrollbar")]
    public float scrollbarWidth = 18f;
    public Color scrollbarBgColor = new Color(0.15f, 0.15f, 0.15f, 0.8f);
    public Color scrollbarHandleColor = new Color(0.6f, 0.6f, 0.6f, 0.9f);

    public System.Action onLevelSolved;
    [HideInInspector] public WorldNavigator worldNavigator;
    [HideInInspector] public int levelIndex;

    readonly List<PieceDragger> allDraggers = new();
    readonly List<TraySlot> traySlots = new();

    // ── Undo ──────────────────────────────────────────────────────────────
    class PieceSnapshot
    {
        public PieceDragger dragger;
        public Vector2Int gridPosition;
        public int rotation;
        public int? runtimeLength;
        public Transform trayParent;
        public bool everMoved;
    }

    readonly Stack<List<PieceSnapshot>> undoStack = new();

    public void SaveSnapshot()
    {
        var snap = new List<PieceSnapshot>();
        foreach (var d in allDraggers)
            snap.Add(new PieceSnapshot
            {
                dragger = d,
                gridPosition = d.piece.gridPosition,
                rotation = d.piece.rotation,
                runtimeLength = d.piece.runtimeLength,
                trayParent = (d.transform.GetComponentInParent<TraySlot>()?.transform)
                                ?? (d.piece.gridPosition.x < 0 ? d.transform.parent : null),
                everMoved = d.everMoved,
            });
        undoStack.Push(snap);
        if (undoButton) undoButton.interactable = true;
    }

    public void Undo()
    {
        if (undoStack.Count == 0) return;
        var snap = undoStack.Pop();

        foreach (var d in allDraggers)
            if (d.piece.gridPosition.x >= 0)
                gridManager.Remove(d.piece);

        foreach (var ps in snap)
        {
            if (ps.dragger == null) continue;
            ps.dragger.piece.rotation = ps.rotation;
            ps.dragger.piece.runtimeLength = ps.runtimeLength;
            ps.dragger.everMoved = ps.everMoved;

            bool shouldBeOnGrid = ps.gridPosition.x >= 0;
            bool forceToTray = !ps.everMoved;

            if (shouldBeOnGrid && !forceToTray)
            {
                gridManager.TryPlace(ps.dragger.piece, ps.gridPosition);
                ps.dragger.SnapToGridPublic(ps.gridPosition);
            }
            else if (!forceToTray)
            {
                ps.dragger.piece.gridPosition = new Vector2Int(-1, -1);
                var slot = ps.trayParent?.GetComponent<TraySlot>()
                        ?? ps.trayParent?.GetComponentInParent<TraySlot>();
                if (slot != null) slot.ReturnFromDrag(ps.dragger);
                else
                {
                    var parent = ps.trayParent != null ? ps.trayParent : trayContainer;
                    ps.dragger.transform.SetParent(parent, false);
                    ps.dragger.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
                    ps.dragger.canvasGroup.alpha = 1f;
                    ps.dragger.onReturnedToTray?.Invoke();
                }
            }
            else
            {
                ps.dragger.piece.gridPosition = new Vector2Int(-1, -1);
                ps.dragger.everMoved = false;
                var slot = ps.trayParent?.GetComponent<TraySlot>()
                        ?? ps.trayParent?.GetComponentInParent<TraySlot>();
                if (slot != null) slot.ReturnFromDrag(ps.dragger);
                else
                {
                    var parent = ps.trayParent != null ? ps.trayParent : trayContainer;
                    ps.dragger.transform.SetParent(parent, false);
                    ps.dragger.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
                    ps.dragger.canvasGroup.alpha = 1f;
                    ps.dragger.onReturnedToTray?.Invoke();
                }
            }

            ps.dragger.RedrawVisual();
            ps.dragger.GetComponentInChildren<SpringResizer>()?.RefreshHandles();
        }

        if (undoButton) undoButton.interactable = undoStack.Count > 0;
        gridManager.OnGridChanged?.Invoke();
        foreach (var slot in traySlots) slot.ForceRefresh();
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────
    void Awake() => Instance = this;

    void Start()
    {
        gridManager.level = currentLevel;
        gridManager.OnGridChanged += CheckCircuit;

        if (winPanel) winPanel.SetActive(false);
        if (undoButton)
        {
            undoButton.interactable = false;
            undoButton.onClick.AddListener(Undo);
        }

        CheckEventSystem();
        SetupViewportMask();
        StartCoroutine(SpawnNextFrame());
    }

    // ── RestoreSaveData ───────────────────────────────────────────────────
    public void RestoreSaveData(LevelSaveData save)
    {
        if (save == null || save.pieces.Count == 0) return;
        StartCoroutine(RestoreNextFrame(save));
    }

    IEnumerator RestoreNextFrame(LevelSaveData save)
    {
        yield return null;
        foreach (var d in allDraggers)
        {
            if (d.piece.gridPosition.x >= 0) gridManager.Remove(d.piece);
            d.piece.gridPosition = new Vector2Int(-1, -1);
        }

        foreach (var saved in save.pieces)
        {
            var dragger = allDraggers.Find(d =>
                d.piece.data != null && d.piece.data.name == saved.pieceDataName
                && d.piece.gridPosition.x < 0);
            if (dragger == null) continue;

            dragger.piece.rotation = saved.rotation;
            dragger.piece.runtimeLength = saved.runtimeLength;
            dragger.everMoved = true;

            if (gridManager.TryPlace(dragger.piece, saved.gridPosition))
                dragger.SnapToGridPublic(saved.gridPosition);
        }

        gridManager.OnGridChanged?.Invoke();
    }

    // ── Setup tray ────────────────────────────────────────────────────────
    void SetupViewportMask()
    {
        if (trayContainer == null) return;
        var viewport = trayContainer.parent;
        if (viewport == null) return;

        var vImg = viewport.GetComponent<Image>();
        if (vImg == null) vImg = viewport.gameObject.AddComponent<Image>();
        vImg.color = new Color(0f, 0f, 0f, 0.01f);
        vImg.raycastTarget = false;

        var mask = viewport.GetComponent<Mask>();
        if (mask == null) mask = viewport.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        var trayPanel = viewport.parent;
        var scrollRect = trayPanel?.GetComponent<ScrollRect>();
        if (scrollRect == null) return;

        var vrt = viewport.GetComponent<RectTransform>();
        vrt.anchorMin = Vector2.zero;
        vrt.anchorMax = Vector2.one;
        vrt.offsetMin = new Vector2(scrollbarWidth, 0f);
        vrt.offsetMax = Vector2.zero;
        scrollRect.viewport = vrt;

        if (scrollRect.verticalScrollbar != null) return;

        var sbGo = new GameObject("VerticalScrollbar");
        sbGo.transform.SetParent(trayPanel, false);
        var sbImg = sbGo.AddComponent<Image>();
        sbImg.color = scrollbarBgColor;
        var sbRt = sbGo.GetComponent<RectTransform>();
        sbRt.anchorMin = new Vector2(0f, 0f);
        sbRt.anchorMax = new Vector2(0f, 1f);
        sbRt.pivot = new Vector2(0f, 0.5f);
        sbRt.sizeDelta = new Vector2(scrollbarWidth, 0f);
        sbRt.anchoredPosition = Vector2.zero;

        var sb = sbGo.AddComponent<Scrollbar>();
        sb.direction = Scrollbar.Direction.BottomToTop;

        var slideArea = new GameObject("Sliding Area");
        slideArea.transform.SetParent(sbGo.transform, false);
        var saRt = slideArea.AddComponent<RectTransform>();
        saRt.anchorMin = Vector2.zero;
        saRt.anchorMax = Vector2.one;
        saRt.offsetMin = new Vector2(2f, 2f);
        saRt.offsetMax = new Vector2(-2f, -2f);

        var handleGo = new GameObject("Handle");
        handleGo.transform.SetParent(slideArea.transform, false);
        var hImg = handleGo.AddComponent<Image>();
        hImg.color = scrollbarHandleColor;
        var hRt = handleGo.GetComponent<RectTransform>();
        hRt.anchorMin = Vector2.zero;
        hRt.anchorMax = Vector2.one;
        hRt.offsetMin = hRt.offsetMax = Vector2.zero;

        sb.handleRect = hRt;
        sb.targetGraphic = hImg;
        scrollRect.verticalScrollbar = sb;
        scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        scrollRect.vertical = true;
    }

    IEnumerator SpawnNextFrame()
    {
        yield return null;
        SpawnTray();
        yield return null;
        worldNavigator?.NotifyInventoryChanged();
        yield return null;
        var contentRt = trayContainer.GetComponent<RectTransform>();
        if (contentRt != null)
        {
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.offsetMin = Vector2.zero;
            contentRt.offsetMax = Vector2.zero;
            contentRt.anchoredPosition = Vector2.zero;
        }
    }

    void SpawnTray()
    {
        var pieces = GetPieceList();
        foreach (var entry in pieces)
        {
            if (entry.data == null) continue;

            var slotGo = new GameObject($"Slot_{entry.data.name}");
            slotGo.transform.SetParent(trayContainer, false);
            var slotRt = slotGo.AddComponent<RectTransform>();
            slotRt.anchorMin = Vector2.zero;
            slotRt.anchorMax = Vector2.zero;
            slotRt.pivot = Vector2.zero;

            // Pezzi normali e cinghie — usa PieceDragger (con isBelt flag per cinghie)
            var slotDraggers = new List<PieceDragger>();
            for (int i = 0; i < entry.quantity; i++)
            {
                var piece = new Piece { data = entry.data, gridPosition = new Vector2Int(-1, -1) };
                var dragger = Instantiate(piecePrefab, slotGo.transform);
                dragger.Setup(piece, gridManager);
                dragger.isBelt = entry.data.isBelt; // cinghia usa logica speciale
                allDraggers.Add(dragger);
                slotDraggers.Add(dragger);
            }

            if (slotDraggers.Count > 0)
            {
                var rt = slotDraggers[0].GetComponent<RectTransform>();
                slotRt.sizeDelta = rt.sizeDelta + new Vector2(16, 16);
            }

            var slot = slotGo.AddComponent<TraySlot>();
            slot.Init(slotDraggers);
            traySlots.Add(slot);
        }
    }

    List<LevelData.PieceEntry> GetPieceList()
    {
        if (worldNavigator != null && worldNavigator.config?.globalPieces?.Count > 0)
        {
            var list = new List<LevelData.PieceEntry>();
            foreach (var gp in worldNavigator.config.globalPieces)
            {
                if (gp.data == null) continue;
                list.Add(new LevelData.PieceEntry { data = gp.data, quantity = gp.quantity });
            }
            return list;
        }
        return currentLevel.availablePieces;
    }

    public void RefreshTrayFromGlobalInventory(Dictionary<PieceData, int> inventory)
    {
        foreach (var slot in traySlots)
        {
            var draggers = slot.GetDraggers();
            if (draggers.Count == 0) continue;
            var data = draggers[0].piece.data;
            if (!inventory.ContainsKey(data)) continue;
            slot.SetAvailableCount(inventory[data]);
        }
    }

    public void ReturnDraggerToTray(PieceDragger dragger)
    {
        UnityEngine.Debug.Log($"[ReturnDraggerToTray] dragger={dragger.name} traySlots={traySlots.Count}");
        foreach (var slot in traySlots)
        {
            var slotDraggers = slot.GetDraggers();
            if (slotDraggers.Contains(dragger))
            {
                slot.ReturnFromDrag(dragger);
                return;
            }
        }
        UnityEngine.Debug.Log($"[ReturnDraggerToTray] NOT FOUND — forcing reparent to first slot");
        // Fallback: se non trovato nei slot, cerca il slot con lo stesso PieceData
        foreach (var slot in traySlots)
        {
            var slotDraggers = slot.GetDraggers();
            if (slotDraggers.Count > 0 && slotDraggers[0].piece.data == dragger.piece.data)
            {
                slot.ReturnFromDrag(dragger);
                return;
            }
        }
        UnityEngine.Debug.Log($"[ReturnDraggerToTray] TOTAL FAIL for {dragger.name}");
    }

    // ── Circuito ──────────────────────────────────────────────────────────
    void CheckCircuit()
    {
        worldNavigator?.NotifyInventoryChanged();

        // Controlla se il livello è risolto via meccanico (richiede gear ON alla dest)
        bool solvedViaMech = false;
        if (currentLevel.DestAccepts(EnergyType.Mechanical))
        {
            var mechReached = CircuitSolver.GetReachedCells(gridManager,
                currentLevel.circuitSource, EnergyType.Mechanical);
            if (mechReached.Contains(currentLevel.circuitDestination))
            {
                var gearVis = gridManager.GetComponent<GearVisualizer>();
                solvedViaMech = gearVis == null || gearVis.IsDestinationOn(currentLevel.circuitDestination);
            }
        }

        // Controlla via altri tipi (nessun check extra)
        bool solvedViaOther = false;
        foreach (var type in new[] { EnergyType.Electric, EnergyType.Hydraulic })
        {
            if (!currentLevel.DestAccepts(type)) continue;
            var reached = CircuitSolver.GetReachedCells(gridManager,
                currentLevel.circuitSource, type);
            if (reached.Contains(currentLevel.circuitDestination))
            { solvedViaOther = true; break; }
        }
        // Controlla anche via convertitori
        if (!solvedViaOther)
            solvedViaOther = CircuitSolver.Solve(gridManager,
                currentLevel.circuitSource, currentLevel.circuitDestination)
                && !solvedViaMech;

        bool solved = solvedViaMech || solvedViaOther;

        if (solved)
        {
            if (winPanel) winPanel.SetActive(true);
            onLevelSolved?.Invoke();
        }
    }

    public void SetTrayCount(PieceData data, int count)
    {
        foreach (var slot in traySlots)
        {
            var draggers = slot.GetDraggers();
            if (draggers.Count == 0 || draggers[0].piece.data != data) continue;
            slot.SetAvailableCount(count);
            return;
        }
    }

    void CheckEventSystem()
    {
        var es = FindFirstObjectByType<EventSystem>();
        if (es == null) { Debug.LogError("[GameManager] EventSystem mancante!"); return; }
        es.pixelDragThreshold = 1;
    }
}