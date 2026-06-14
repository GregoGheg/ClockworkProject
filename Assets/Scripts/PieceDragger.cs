using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CanvasGroup))]
public class PieceDragger : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler,
    IPointerClickHandler, IPointerDownHandler, IPointerUpHandler,
    IPointerEnterHandler
{
    [HideInInspector] public Piece piece;
    [HideInInspector] public GridManager grid;

    public CanvasGroup canvasGroup { get; private set; }

    Canvas rootCanvas;
    RectTransform rectTransform;
    Vector2 originalAnchoredPos;
    Transform originalParent;

    static PieceDragger selected;
    static PieceDragger lastInteracted;      // ultimo pezzo con cui l'utente ha interagito
    static PieceDragger lastClickedPiece;    // pezzo dell'ultimo click sinistro
    static float lastLeftClickTime = -1f;
    const float doubleClickThreshold = 0.3f;
    bool isDragging = false;       // true se OnBeginDrag è scattato
    bool pendingDoubleClick = false; // doppio click rilevato al PointerDown
    public bool IsSelected() => selected == this;

    public System.Action onReturnedToTray;
    public System.Action onRemovedFromTray;

    GameObject dragGhost;

    // Ultima posizione valida prima del drag
    Vector2Int prevGridPosition = new Vector2Int(-1, -1);
    int prevRotation = 0;

    public bool everMoved = false;
    bool isResizing = false;
    float resizeDragStart = 0f;

    [HideInInspector] public bool isBelt = false;
    [HideInInspector] public Vector2Int beltEndCell = new Vector2Int(-1, -1);
    Vector2 dragStartScreenPos;
    Vector2? _beltPressPosition = null; // posizione screen del press per click-drop cinghia
    int resizeLenStart = 0;
    int resizeLastLen = 0;
    bool resizingFromTail = false;
    Vector2Int resizeHeadPos;

    // Usa il GameManager locale (dal parent) invece del singleton globale
    // per evitare conflitti quando più livelli sono in memoria contemporaneamente
    GameManager LocalGameManager => GetComponentInParent<GameManager>();

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.zero;
        rectTransform.pivot = Vector2.zero;
    }

    void OnDisable()
    {
        if (dragGhost != null) { Destroy(dragGhost); dragGhost = null; }
    }

    void OnDestroy()
    {
        if (dragGhost != null) { Destroy(dragGhost); dragGhost = null; }
    }

    public void Setup(Piece p, GridManager gm)
    {
        piece = p;
        grid = gm;

        // Garantisci i componenti anche se Awake non è ancora girato
        // (es. dragger istanziato in un GameObject disattivato durante il preload).
        if (rectTransform == null) rectTransform = GetComponent<RectTransform>();
        if (canvasGroup == null)
        {
            canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
        if (rectTransform != null)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.zero;
            rectTransform.pivot = Vector2.zero;
        }

        // Pulisci ghost rimasti da drag precedenti
        if (dragGhost != null) { Destroy(dragGhost); dragGhost = null; }

        // Distruggi eventuali DragGhost orfani nel canvas
        if (rootCanvas != null)
            foreach (Transform child in rootCanvas.transform)
                if (child.name == "DragGhost") Destroy(child.gameObject);
        rootCanvas = GetComponentInParent<Canvas>();
        if (rootCanvas == null)
            rootCanvas = FindFirstObjectByType<Canvas>();

        var img = GetComponent<Image>();
        if (img == null) img = gameObject.AddComponent<Image>();
        img.color = Color.clear;
        img.raycastTarget = true;

        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
        RedrawVisual();
    }

    // ── Pointer events ────────────────────────────────────────────────────
    public void OnPointerClick(PointerEventData e)
    {
        UnityEngine.Debug.Log($"[Click] button={e.button}");
        if (e.button == PointerEventData.InputButton.Left)
        {
            Select();
            lastInteracted = this;
        }
        else if (e.button == PointerEventData.InputButton.Middle)
        {
            ReturnToTray();
        }
    }

    // Tasto centrale tenuto premuto: elimina ogni pezzo su cui passa il mouse
    public void OnPointerEnter(PointerEventData e)
    {
        if (piece.gridPosition.x < 0) return;
        var mouse = Mouse.current;
        if (mouse == null) return;
        if (mouse.middleButton.isPressed)
            ReturnToTray();
    }

    public void OnPointerDown(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Left)
        {
            isDragging = false;
            lastInteracted = this;
            lastLeftClickTime = Time.unscaledTime;
            lastClickedPiece = this;
            pendingDoubleClick = false;
            // Per cinghia già ancorata (fase 2): salva la posizione del press
            // per poter calcolare la cella di drop se il drag non scatta
            if (isBelt && piece.gridPosition.x >= 0)
                _beltPressPosition = e.position;
            else
                _beltPressPosition = null;
        }
        if (e.button != PointerEventData.InputButton.Right) return;
        var resizer = GetComponentInChildren<SpringResizer>();
        if (resizer == null || !IsSelected() || piece.gridPosition.x < 0) return;

        isResizing = true;
        bool isVertical = piece.rotation % 2 == 1;
        resizeDragStart = isVertical ? e.position.y : e.position.x;
        resizeLenStart = piece.runtimeLength ?? piece.data.cells.Count;
        resizeLastLen = resizeLenStart;

        // Calcola la direzione reale delle celle secondo RotateCoord
        // rot=0: (i,0)   rot=1: (0,-i)   rot=2: (-i,0)   rot=3: (0,i)
        var cellDir = piece.rotation switch
        {
            1 => new Vector2Int(0, -1),
            2 => new Vector2Int(-1, 0),
            3 => new Vector2Int(0, 1),
            _ => new Vector2Int(1, 0)
        };
        var orig = piece.gridPosition;
        // La tail è la cella i=(len-1): gridPos + cellDir*(len-1)
        var tail = orig + cellDir * (resizeLenStart - 1);
        var gc = grid.ScreenToGridCoord(e.position, null);
        float dO = Vector2Int.Distance(gc, orig);
        float dT = Vector2Int.Distance(gc, tail);
        // resizingFromTail=true → l'utente trascina la coda, la testa è orig
        // resizingFromTail=false → l'utente trascina la testa, la coda cresce dall'altra parte
        resizingFromTail = dT < dO;
        resizeHeadPos = resizingFromTail ? orig : tail;
    }

    public void OnPointerUp(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Left)
        {
            pendingDoubleClick = false;

            // Cinghia ancorata: se il drag non è scattato (isDragging=false)
            // trattiamo il PointerUp come un click-drop verso la cella sotto il cursore
            if (isBelt && piece.gridPosition.x >= 0 && !isDragging && _beltPressPosition.HasValue)
            {
                _beltPressPosition = null;
                var gc = grid.ScreenToGridCoord(e.position, null);
                canvasGroup.blocksRaycasts = true;
                canvasGroup.alpha = 1f;
                HandleBeltDrop(gc);
                return;
            }
            _beltPressPosition = null;
            isDragging = false;
        }
        if (e.button != PointerEventData.InputButton.Right) return;
        isResizing = false;
    }

    public void Select()
    {
        if (selected != null && selected != this) selected.Deselect();
        selected = this;
        lastInteracted = this;
        LastSelectedDisplay.SetSprite(piece?.data?.pieceSprite);
        UnityEngine.Debug.Log($"[Select] lastInteracted={lastInteracted.piece?.data?.name} pos={lastInteracted.piece?.gridPosition}");
        SetHandlesVisible(true);
    }

    public void Deselect()
    {
        SetHandlesVisible(false);
    }

    public static void ClearSelection() => selected = null;

    /// <summary>
    /// Chiamato da GridCell su doppio click: piazza una nuova istanza
    /// dell'ultimo pezzo con cui l'utente ha interagito, se disponibile nel tray.
    /// </summary>
    void TryPlaceNextToThis()
    {
        var dirs = new[] { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };
        foreach (var dir in dirs)
        {
            var candidate = piece.gridPosition + dir;
            if (!grid.CanPlace(piece, candidate)) continue;
            TryPlaceLastSelected(grid, candidate);
            return;
        }
    }

    void TryPlaceOnFirstFreeCell()
    {
        for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                var coord = new Vector2Int(x, y);
                if (grid.CanPlace(piece, coord))
                {
                    TryPlaceLastSelected(grid, coord);
                    return;
                }
            }
    }

    public static void TryPlaceLastSelected(GridManager grid, Vector2Int coord)
    {
        if (lastInteracted == null) return;
        // Il pezzo selezionato deve appartenere a QUESTA griglia, altrimenti
        // un doppio click dopo il cambio livello piazzerebbe un dragger di un
        // altro livello (pezzo difettoso, cella irremovibile).
        if (lastInteracted.grid != null && lastInteracted.grid != grid) return;

        var data = lastInteracted.piece.data;

        // ── Controllo limite pool globale ────────────────────────────────
        // Anche se esistono dragger fisici non piazzati nel tray, non si può
        // superare la quantità globale disponibile (GetAvailable).
        var gmCheck = grid.GetComponentInParent<GameManager>();
        if (gmCheck == null)
        {
            foreach (var g in UnityEngine.Object.FindObjectsByType<GameManager>(
                UnityEngine.FindObjectsSortMode.None))
                if (g.gridManager == grid) { gmCheck = g; break; }
        }
        if (gmCheck != null && gmCheck.worldNavigator != null)
        {
            if (gmCheck.worldNavigator.GetAvailable(data) <= 0) return; // pool esaurito
        }

        // Trova un dragger dello stesso tipo disponibile nel tray DI QUESTA griglia
        var allDraggers = UnityEngine.Object.FindObjectsByType<PieceDragger>(
            UnityEngine.FindObjectsSortMode.None);
        PieceDragger available = null;
        foreach (var d in allDraggers)
        {
            if (d.grid != grid) continue; // solo dragger di questa griglia
            if (d.piece.data == data && d.piece.gridPosition.x < 0
                && d.gameObject.activeInHierarchy)
            { available = d; break; }
        }
        if (available == null) return;

        // Copia la rotazione dell'ultimo pezzo interagito
        available.piece.rotation = lastInteracted.piece.rotation;
        available.RedrawVisual();

        if (!grid.TryPlace(available.piece, coord)) return;

        // Sposta il dragger sulla griglia e rendilo visibile
        var slot = available.originalParent?.GetComponent<TraySlot>()
                ?? available.originalParent?.GetComponentInParent<TraySlot>();
        slot?.HideDuringDrag(available); // aggiorna contatore tray

        available.transform.SetParent(grid.transform, false);
        available.gameObject.SetActive(true);
        available.canvasGroup.alpha = 1f;
        available.canvasGroup.blocksRaycasts = true;
        available.canvasGroup.interactable = true;
        available.SetPlacedPosition(coord, grid);
        grid.OnGridChanged?.Invoke();
        available.Select();
        available.PlayPlaceFeedback();
    }

    void SetHandlesVisible(bool v)
    {
        GetComponentInChildren<SpringResizer>()?.SetHandlesVisible(v);
    }

    // ── Drag ──────────────────────────────────────────────────────────────
    public void OnBeginDrag(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;

        // Segna che il drag è iniziato — usato da OnPointerUp per escludere il doppio click
        isDragging = true;

        // ── Cinghia: drag dedicato (ancoraggio + corda) ──────────────────
        if (isBelt) { BeginBeltDrag(e); return; }

        Select();
        SetHandlesVisible(false);

        originalAnchoredPos = rectTransform.anchoredPosition;
        originalParent = transform.parent;

        // Salva la posizione precedente per il fallback
        prevGridPosition = piece.gridPosition;
        prevRotation = piece.rotation;

        var slot = originalParent?.GetComponent<TraySlot>();
        if (slot == null) slot = originalParent?.GetComponentInParent<TraySlot>();
        slot?.HideDuringDrag(this);

        canvasGroup.blocksRaycasts = false;
        canvasGroup.alpha = 0f;

        CreateDragGhost(e.position);
        onRemovedFromTray?.Invoke();

        if (piece.gridPosition.x >= 0)
            grid.Remove(piece);
    }

    public void OnDrag(PointerEventData e)
    {
        if (e.button == PointerEventData.InputButton.Right)
        {
            if (!isResizing) return;
            var resizer = GetComponentInChildren<SpringResizer>();
            if (resizer == null) return;

            bool isVertical = piece.rotation % 2 == 1;
            float current = isVertical ? e.position.y : e.position.x;
            float delta = current - resizeDragStart;

            var canvasScaler = grid.GetComponentInParent<UnityEngine.UI.CanvasScaler>();
            float scale = canvasScaler != null
                ? Screen.width / canvasScaler.referenceResolution.x
                : 1f;
            float pixPerCell = grid.cellSize * scale;

            bool invertDir = piece.rotation == 1 || piece.rotation == 2;
            float eff = resizingFromTail
                ? (invertDir ? -delta : delta)
                : (invertDir ? delta : -delta);
            int dc = Mathf.RoundToInt(eff / pixPerCell);
            int newLen = Mathf.Clamp(resizeLenStart + dc, 2, 4);
            if (newLen != resizeLastLen)
            {
                if (resizingFromTail)
                    resizer.TryResizePublicFrom(newLen, resizeHeadPos);
                else
                    resizer.TryResizePublicFromTail(newLen, resizeHeadPos);
                resizeLastLen = newLen;
            }
            return;
        }

        if (e.button != PointerEventData.InputButton.Left) return;

        // ── Cinghia: durante il drag disegna la corda, niente preview ───
        if (isBelt) { DragBelt(e); return; }

        if (dragGhost != null)
        {
            RectTransformUtility.ScreenPointToWorldPointInRectangle(
                rootCanvas.GetComponent<RectTransform>(),
                e.position, null, out Vector3 worldPoint);
            dragGhost.transform.position = worldPoint;
        }

        var gc = grid.ScreenToGridCoord(e.position, null);
        grid.ShowPreview(piece, gc);
    }

    public void OnEndDrag(PointerEventData e)
    {
        // Distruggi ghost SEMPRE come prima cosa
        if (dragGhost != null) { Destroy(dragGhost); dragGhost = null; }

        if (e.button == PointerEventData.InputButton.Right) { isResizing = false; return; }
        if (e.button != PointerEventData.InputButton.Left) return;

        grid.ClearPreview();

        var gc = grid.ScreenToGridCoord(e.position, null);

        // ── Logica cinghia ────────────────────────────────────────────
        if (isBelt)
        {
            // Per la cinghia ripristina visibilità DOPO il drop
            canvasGroup.blocksRaycasts = true;
            canvasGroup.alpha = 1f;
            UnityEngine.Debug.Log($"[BeltEndDrag] e.pos={e.position} gc={gc} anchor={piece.gridPosition}");
            HandleBeltDrop(gc);
            return;
        }

        canvasGroup.blocksRaycasts = true;
        canvasGroup.alpha = 1f;

        // Salva snapshot PRIMA del piazzamento
        bool canPlace = grid.CanPlace(piece, gc);
        if (canPlace) LocalGameManager?.SaveSnapshot();

        bool placed = grid.TryPlace(piece, gc);

        if (placed)
        {
            everMoved = true;
            SnapToGrid(gc);
            PlayPlaceFeedback();
            AttachResizer();
            GetComponentInChildren<SpringResizer>()?.SetHandlesVisible(selected == this);
        }
        else
        {
            if (prevGridPosition.x >= 0)
            {
                piece.rotation = prevRotation;
                if (grid.TryPlace(piece, prevGridPosition))
                {
                    SnapToGrid(prevGridPosition);
                    RedrawVisual();
                    AttachResizer();
                    canvasGroup.alpha = 1f;
                }
                else GoBackToTray();
            }
            else
            {
                piece.rotation = prevRotation;
                GoBackToTray();
            }
        }
    }

    // ── Cinghia ───────────────────────────────────────────────────────────
    // Interazione in due fasi:
    //  Fase 1 — trascini la cinghia dal tray su un ingranaggio: si "ancora" lì
    //           (anello attorno all'ingranaggio).
    //  Fase 2 — trascini la cinghia ancorata come una corda verso un ingranaggio
    //           adiacente (8 direzioni): i due ingranaggi vengono collegati e
    //           ruotano nello stesso verso (BeltSolver li forza allo stesso stato,
    //           MechanicalSolver esclude la coppia dai conflitti).

    void BeginBeltDrag(PointerEventData e)
    {
        Select();
        originalAnchoredPos = rectTransform.anchoredPosition;

        if (piece.gridPosition.x < 0)
        {
            // Fase 1: drag dal tray — comportamento simile ai pezzi normali
            originalParent = transform.parent;
            var slot = originalParent?.GetComponent<TraySlot>()
                    ?? originalParent?.GetComponentInParent<TraySlot>();
            slot?.HideDuringDrag(this);
            canvasGroup.blocksRaycasts = false;
            canvasGroup.alpha = 0f;
            CreateDragGhost(e.position);
            onRemovedFromTray?.Invoke();
        }
        else
        {
            // Fase 2: la cinghia è già ancorata — inizia il drag "a corda".
            // blocksRaycasts=false: il drop passa attraverso la cinghia e arriva alla griglia.
            beltEndCell = new Vector2Int(-1, -1);
            isDragging = true; // marca subito come drag così OnPointerUp non lo intercetta
            canvasGroup.blocksRaycasts = false;
            canvasGroup.alpha = 0.6f;
        }
    }

    void DragBelt(PointerEventData e)
    {
        if (piece.gridPosition.x >= 0)
        {
            // Corda elastica dal centro dell'ingranaggio ancorato al cursore
            var gridRT = grid.GetComponent<RectTransform>();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                gridRT, e.position, e.pressEventCamera, out Vector2 local);
            StretchToPoint(piece.gridPosition, local);
        }
        else if (dragGhost != null)
        {
            RectTransformUtility.ScreenPointToWorldPointInRectangle(
                rootCanvas.GetComponent<RectTransform>(),
                e.position, null, out Vector3 worldPoint);
            dragGhost.transform.position = worldPoint;
        }
    }

    void HandleBeltDrop(Vector2Int dropCell)
    {
        canvasGroup.blocksRaycasts = true;
        canvasGroup.alpha = 1f;

        bool wasAnchored = piece.gridPosition.x >= 0;
        var gearAtDrop = FindGearAt(dropCell);
        UnityEngine.Debug.Log($"[BeltDrop] dropCell={dropCell} wasAnchored={wasAnchored} anchor={piece.gridPosition} gearAtDrop={gearAtDrop?.ToString() ?? "NULL"}");

        if (!wasAnchored)
        {
            // ── Fase 1: ancoraggio su un ingranaggio ─────────────────────
            if (gearAtDrop == null) { GoBackToTray(); return; }

            piece.gridPosition = gearAtDrop.Value;
            beltEndCell = new Vector2Int(-1, -1);
            everMoved = true;
            transform.SetParent(grid.transform, false);
            ShowAnchoredVisual();
            PlayPlaceFeedback();
            grid.OnGridChanged?.Invoke();
            return;
        }

        // ── Fase 2: connessione a corda ──────────────────────────────────
        // La cinghia collega qualsiasi due gear distinti (anche diagonali/distanti)
        var anchor = piece.gridPosition;
        if (gearAtDrop != null && gearAtDrop.Value != anchor)
        {
            beltEndCell = gearAtDrop.Value;
            StretchBetween(anchor, beltEndCell);
            PlayPlaceFeedback();
            grid.OnGridChanged?.Invoke();
            return;
        }

        // Drop non valido → la cinghia resta ancorata senza connessione
        beltEndCell = new Vector2Int(-1, -1);
        ShowAnchoredVisual();
        grid.OnGridChanged?.Invoke();
    }

    /// <summary>Vero se i pezzi alle due gridPosition hanno almeno una coppia
    /// di celle fisiche a distanza Chebyshev ≤ 1 (adiacenti anche in diagonale).</summary>
    bool PiecesAdjacent(Vector2Int posA, Vector2Int posB)
    {
        var pa = grid.GetCell(posA)?.occupant;
        var pb = grid.GetCell(posB)?.occupant;
        if (pa == null || pb == null || pa == pb) return false;

        foreach (var ca in pa.WorldCells())
        {
            if (!ca.occupiesSpace) continue;
            foreach (var cb in pb.WorldCells())
            {
                if (!cb.occupiesSpace) continue;
                int dx = Mathf.Abs(ca.localCoord.x - cb.localCoord.x);
                int dy = Mathf.Abs(ca.localCoord.y - cb.localCoord.y);
                if (dx <= 1 && dy <= 1) return true;
            }
        }
        return false;
    }

    /// <summary>Visual "anello" attorno all'ingranaggio di ancoraggio (fase 1).</summary>
    public void ShowAnchoredVisual()
    {
        float size = grid.cellSize;
        float gearSize = GetGearVisualSize(piece.gridPosition);
        var center = new Vector2(
            piece.gridPosition.x * size + gearSize * 0.5f,
            piece.gridPosition.y * size + gearSize * 0.5f);

        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.zero;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = center;
        rectTransform.sizeDelta = Vector2.one * gearSize * 1.15f;
        rectTransform.localEulerAngles = Vector3.zero;

        SetupBeltSpriteFill();
        HideCellChildren();
    }

    /// <summary>Visual "corda" dal centro dell'ingranaggio a un punto locale della griglia.</summary>
    public void StretchToPoint(Vector2Int from, Vector2 toLocal)
    {
        float size = grid.cellSize;
        float sizeA = GetGearVisualSize(from);
        var sPos = new Vector2(from.x * size + sizeA * 0.5f, from.y * size + sizeA * 0.5f);
        var diff = toLocal - sPos;
        var mid = (sPos + toLocal) * 0.5f;
        float dist = Mathf.Max(diff.magnitude, size * 0.25f);
        float angle = Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg;
        float beltH = sizeA * 0.3f;

        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.zero;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = mid;
        rectTransform.sizeDelta = new Vector2(dist, beltH);
        rectTransform.localEulerAngles = new Vector3(0, 0, angle);

        SetupBeltSpriteFill();
        HideCellChildren();
    }

    /// <summary>Lo sprite della cinghia riempie tutto il rect del dragger.</summary>
    void SetupBeltSpriteFill()
    {
        var spriteT = transform.Find("piece_sprite");
        if (spriteT == null) return;
        var srt = spriteT.GetComponent<RectTransform>();
        if (srt == null) return;
        srt.anchorMin = Vector2.zero;
        srt.anchorMax = Vector2.one;
        srt.pivot = new Vector2(0.5f, 0.5f);
        srt.offsetMin = srt.offsetMax = Vector2.zero;
        srt.localEulerAngles = Vector3.zero;
    }

    /// <summary>Nasconde i quadrati "cell_" del RedrawVisual: per la cinghia
    /// si vede solo lo sprite stretchato.</summary>
    void HideCellChildren()
    {
        foreach (Transform child in transform)
        {
            if (!child.name.StartsWith("cell_")) continue;
            var img = child.GetComponent<Image>();
            if (img != null) img.enabled = false;
        }
    }

    public void StretchBetween(Vector2Int from, Vector2Int to)
    {
        float size = grid.cellSize;

        // Calcola la dimensione effettiva di ogni ingranaggio
        // (un ingranaggio grande occupa più celle)
        float sizeA = GetGearVisualSize(from);
        float sizeB = GetGearVisualSize(to);

        // Centro visivo di ogni ingranaggio (basato sulla sua dimensione)
        var sPos = new Vector2(from.x * size + sizeA * 0.5f, from.y * size + sizeA * 0.5f);
        var ePos = new Vector2(to.x * size + sizeB * 0.5f, to.y * size + sizeB * 0.5f);
        var diff = ePos - sPos;
        var mid = (sPos + ePos) * 0.5f;

        // Distanza tra i centri
        float dist = diff.magnitude;
        float angle = Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg;

        // Altezza della cinghia = media delle dimensioni dei due ingranaggi * 0.3
        float beltH = (sizeA + sizeB) * 0.15f;

        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.zero;
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = mid;
        rectTransform.sizeDelta = new Vector2(dist, beltH);
        rectTransform.localEulerAngles = new Vector3(0, 0, angle);

        HideCellChildren();

        var spriteT = transform.Find("piece_sprite");
        if (spriteT != null)
        {
            var srt = spriteT.GetComponent<RectTransform>();
            if (srt != null)
            {
                srt.anchorMin = Vector2.zero;
                srt.anchorMax = Vector2.one;
                srt.offsetMin = srt.offsetMax = Vector2.zero;
            }
        }
    }

    float GetGearVisualSize(Vector2Int coord)
    {
        float size = grid.cellSize;
        if (!grid.IsInBounds(coord)) return size;
        var st = grid.GetCell(coord.x, coord.y);
        if (st?.occupant == null) return size;

        // Conta quante celle occupa il pezzo per stimarne la dimensione visiva
        var cells = st.occupant.CurrentCells();
        int w = 1, h = 1;
        int minX = int.MaxValue, maxX = int.MinValue;
        int minY = int.MaxValue, maxY = int.MinValue;
        foreach (var c in cells)
        {
            if (!c.occupiesSpace) continue;
            minX = Mathf.Min(minX, c.localCoord.x); maxX = Mathf.Max(maxX, c.localCoord.x);
            minY = Mathf.Min(minY, c.localCoord.y); maxY = Mathf.Max(maxY, c.localCoord.y);
        }
        if (minX != int.MaxValue) { w = maxX - minX + 1; h = maxY - minY + 1; }
        return Mathf.Max(w, h) * size;
    }

    Vector2Int? FindGearAt(Vector2Int coord)
    {
        if (!grid.IsInBounds(coord)) { UnityEngine.Debug.Log($"[FindGear] {coord} OUT OF BOUNDS"); return null; }
        var st = grid.GetCell(coord.x, coord.y);
        UnityEngine.Debug.Log($"[FindGear] coord={coord} occupant={st?.occupant?.data?.name ?? "null"} isGear={st?.occupant?.data?.isGear}");
        if (st?.occupant != null && st.occupant.data.isGear) return st.occupant.gridPosition;
        // Cerca anche nelle celle adiacenti per ingranaggi grandi
        for (int dx2 = -1; dx2 <= 1; dx2++)
            for (int dy2 = -1; dy2 <= 1; dy2++)
            {
                if (dx2 == 0 && dy2 == 0) continue;
                var c = coord + new Vector2Int(dx2, dy2);
                if (!grid.IsInBounds(c)) continue;
                var s = grid.GetCell(c.x, c.y);
                if (s?.occupant != null && s.occupant.data.isGear)
                { UnityEngine.Debug.Log($"[FindGear] trovato gear vicino in {c} gridPos={s.occupant.gridPosition}"); return s.occupant.gridPosition; }
            }
        return null;
    }

    // ── Ghost ─────────────────────────────────────────────────────────────
    void CreateDragGhost(Vector2 screenPos)
    {
        // Distruggi ghost precedente se esiste ancora
        if (dragGhost != null) { Destroy(dragGhost); dragGhost = null; }

        dragGhost = new GameObject("DragGhost");
        dragGhost.transform.SetParent(rootCanvas.transform, false);
        dragGhost.transform.SetAsLastSibling();

        var ghostRt = dragGhost.AddComponent<RectTransform>();
        ghostRt.anchorMin = Vector2.zero;
        ghostRt.anchorMax = Vector2.zero;
        ghostRt.pivot = new Vector2(0.5f, 0.5f);
        ghostRt.sizeDelta = rectTransform.sizeDelta;

        Vector3 center = rectTransform.position + (Vector3)(rectTransform.sizeDelta * rootCanvas.scaleFactor * 0.5f);
        ghostRt.position = center;

        foreach (Transform child in transform)
        {
            if (child.name == "SpringResizer") continue;
            if (child.name.StartsWith("Handle_")) continue;
            var copy = Instantiate(child.gameObject, dragGhost.transform, false);
            foreach (var img in copy.GetComponentsInChildren<Image>())
                img.raycastTarget = false;
        }
    }

    // ── Rotazione ─────────────────────────────────────────────────────────
    void Update()
    {
        if (selected != this) return;
        var mouse = Mouse.current;
        if (mouse == null) return;
        if (mouse.forwardButton.wasPressedThisFrame) Rotate(1);
        if (mouse.backButton.wasPressedThisFrame) Rotate(-1);
    }

    void GoBackToTray()
    {
        rectTransform.localEulerAngles = Vector3.zero;
        rectTransform.pivot = Vector2.zero;
        rectTransform.anchoredPosition = originalAnchoredPos;
        piece.gridPosition = new Vector2Int(-1, -1);
        // piece.rotation già ripristinato dal chiamante
        var retSlot = originalParent?.GetComponent<TraySlot>()
                   ?? originalParent?.GetComponentInParent<TraySlot>();
        if (retSlot != null)
            retSlot.ReturnFromDrag(this);
        else
            transform.SetParent(originalParent, false);
        RedrawVisual(); // ridisegna con prevRotation già impostato
        onReturnedToTray?.Invoke();
    }

    public void ReturnToTray()
    {
        if (piece.gridPosition.x < 0) return;

        // ── Cinghia: non occupa celle, quindi NIENTE grid.Remove
        //    (cancellerebbe l'occupazione degli ingranaggi sottostanti) ────
        if (isBelt)
        {
            piece.gridPosition = new Vector2Int(-1, -1);
            beltEndCell = new Vector2Int(-1, -1);
            rectTransform.localEulerAngles = Vector3.zero;
            rectTransform.pivot = Vector2.zero;

            TraySlot beltSlot = originalParent?.GetComponent<TraySlot>()
                             ?? originalParent?.GetComponentInParent<TraySlot>();
            if (beltSlot == null)
                foreach (var slot in UnityEngine.Object.FindObjectsByType<TraySlot>(
                    UnityEngine.FindObjectsSortMode.None))
                    if (slot.GetDraggers().Contains(this)) { beltSlot = slot; break; }

            if (beltSlot != null) beltSlot.ReturnFromDrag(this);
            else if (originalParent != null) transform.SetParent(originalParent, false);

            RedrawVisual();
            Deselect();
            ClearSelection();
            grid.OnGridChanged?.Invoke();
            return;
        }

        // Nascondi tutte le celle secondarie dello stesso pezzo
        var allDraggers = grid.GetComponentsInChildren<PieceDragger>(true);
        foreach (var d in allDraggers)
            if (d.piece == piece && d != this)
                d.gameObject.SetActive(false);

        // Rimuovi dalla griglia logica
        grid.Remove(piece);

        // Trova il TraySlot: prima da originalParent, poi cerca nella scena
        TraySlot targetSlot = originalParent?.GetComponent<TraySlot>()
                           ?? originalParent?.GetComponentInParent<TraySlot>();

        if (targetSlot == null)
        {
            foreach (var slot in UnityEngine.Object.FindObjectsByType<TraySlot>(
                UnityEngine.FindObjectsSortMode.None))
            {
                if (slot.GetDraggers().Contains(this))
                {
                    targetSlot = slot;
                    break;
                }
            }
        }

        if (targetSlot != null)
            targetSlot.ReturnFromDrag(this);
        else
        {
            // Fallback estremo: torna al parent originale e reimposta posizione
            transform.SetParent(originalParent, false);
            piece.gridPosition = new Vector2Int(-1, -1);
        }

        Deselect();
        ClearSelection();
        grid.OnGridChanged?.Invoke();
    }

    void Rotate(int dir)
    {
        bool wasPlaced = piece.gridPosition.x >= 0;
        var savedPos = piece.gridPosition;

        if (wasPlaced) grid.Remove(piece);

        piece.rotation = (piece.rotation + dir + 4) % 4;
        RedrawVisual();

        if (wasPlaced)
        {
            if (!grid.TryPlace(piece, savedPos))
            {
                piece.rotation = (piece.rotation - dir + 4) % 4;
                RedrawVisual();
                grid.TryPlace(piece, savedPos);
                StartCoroutine(RotationFailFeedback());
                return;
            }
        }

        GetComponentInChildren<SpringResizer>()?.SetHandlesVisible(selected == this);
    }

    System.Collections.IEnumerator RotationFailFeedback()
    {
        float duration = 1f;
        float shakeAmt = 5f;
        float elapsed = 0f;
        var origPos = rectTransform.anchoredPosition;

        var images = GetComponentsInChildren<Image>();
        var origColors = new Color[images.Length];
        for (int i = 0; i < images.Length; i++)
            origColors[i] = images[i].color;

        for (int i = 0; i < images.Length; i++)
            if (!images[i].raycastTarget && images[i].color.a > 0.01f)
                images[i].color = new Color(1f, 0.2f, 0.2f, images[i].color.a);

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / Mathf.Min(duration, 0.5f);
            float shake = elapsed < 0.5f ? Mathf.Sin(t * Mathf.PI * 12f) * shakeAmt * (1f - t) : 0f;
            rectTransform.anchoredPosition = origPos + new Vector2(shake, 0f);
            yield return null;
        }

        rectTransform.anchoredPosition = origPos;
        var cur = GetComponentsInChildren<Image>();
        if (cur.Length == origColors.Length)
            for (int i = 0; i < cur.Length; i++) cur[i].color = origColors[i];
        else
            RedrawVisual();
    }

    // ── Feedback piazzamento (bounce + suono) ─────────────────────────────
    /// <summary>Piccolo bounce di scala + suono specifico del pezzo (PieceData.placeSound).</summary>
    public void PlayPlaceFeedback()
    {
        if (piece?.data?.placeSound != null)
            PlaceFeedbackAudio.Play(piece.data.placeSound, piece.data.placeSoundVolume);

        if (!gameObject.activeInHierarchy) return;
        StopCoroutine(nameof(BounceRoutine));
        StartCoroutine(nameof(BounceRoutine));
    }

    System.Collections.IEnumerator BounceRoutine()
    {
        const float duration = 0.22f;
        const float amplitude = 0.15f; // +15% al picco
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float s = 1f + amplitude * Mathf.Sin(t * Mathf.PI);
            transform.localScale = Vector3.one * s;
            yield return null;
        }
        transform.localScale = Vector3.one;
    }

    // ── Helpers ───────────────────────────────────────────────────────────
    public void SnapToGridPublic(Vector2Int g)
    {
        SnapToGrid(g);
        // Aggiorna prevGridPosition e prevRotation così il pezzo
        // ripristinato da save è subito rimovibile senza dover essere spostato prima
        prevGridPosition = g;
        prevRotation = piece.rotation;
    }

    void SnapToGrid(Vector2Int gc)
    {
        transform.SetParent(grid.transform, false);
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.zero;
        rectTransform.pivot = Vector2.zero;

        // Calcola pMin per compensare offset celle con coordinate negative
        var cells = piece.CurrentCells();
        int pMinX = int.MaxValue, pMinY = int.MaxValue;
        foreach (var cell in cells)
        {
            if (!cell.occupiesSpace) continue;
            pMinX = Mathf.Min(pMinX, cell.localCoord.x);
            pMinY = Mathf.Min(pMinY, cell.localCoord.y);
        }
        if (pMinX == int.MaxValue) { pMinX = 0; pMinY = 0; }

        // La posizione visiva parte dalla cella gc + offset pMin
        rectTransform.anchoredPosition = new Vector2(
            (gc.x + pMinX) * grid.cellSize,
            (gc.y + pMinY) * grid.cellSize);
    }

    /// <summary>Imposta la posizione visiva del dragger dopo un piazzamento programmatico.</summary>
    public void SetPlacedPosition(Vector2Int coord, GridManager g)
    {
        // Calcola offset della cella minima del pezzo (come in SnapVisualToGrid)
        int pMinX = int.MaxValue, pMinY = int.MaxValue;
        foreach (var cell in piece.CurrentCells())
        {
            if (!cell.occupiesSpace) continue;
            if (cell.localCoord.x < pMinX) pMinX = cell.localCoord.x;
            if (cell.localCoord.y < pMinY) pMinY = cell.localCoord.y;
        }
        if (pMinX == int.MaxValue) { pMinX = 0; pMinY = 0; }

        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.zero;
        rectTransform.pivot = Vector2.zero;
        rectTransform.anchoredPosition = new Vector2(
            (coord.x + pMinX) * g.cellSize,
            (coord.y + pMinY) * g.cellSize);
    }

    void AttachResizer()
    {
        if (!piece.data.resizable) return;
        var r = GetComponentInChildren<SpringResizer>();
        if (r == null)
        {
            var go = new GameObject("SpringResizer");
            go.transform.SetParent(transform, false);
            r = go.AddComponent<SpringResizer>();
            r.Init(this, grid);
        }
    }

    // ── Disegno ───────────────────────────────────────────────────────────
    public void RedrawVisual()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child.name == "SpringResizer") continue;
            Destroy(child.gameObject);
        }

        if (piece == null || grid == null) return;

        float size = grid.cellSize;
        var cells = piece.CurrentCells();

        int pMinX = int.MaxValue, pMinY = int.MaxValue;
        int pMaxX = int.MinValue, pMaxY = int.MinValue;
        foreach (var cell in cells)
        {
            if (!cell.occupiesSpace) continue;
            pMinX = Mathf.Min(pMinX, cell.localCoord.x);
            pMinY = Mathf.Min(pMinY, cell.localCoord.y);
            pMaxX = Mathf.Max(pMaxX, cell.localCoord.x);
            pMaxY = Mathf.Max(pMaxY, cell.localCoord.y);
        }
        if (pMinX == int.MaxValue) { pMinX = pMinY = 0; pMaxX = pMaxY = 0; }

        int physW = pMaxX - pMinX + 1;
        int physH = pMaxY - pMinY + 1;
        rectTransform.sizeDelta = new Vector2(physW * size, physH * size);

        foreach (var cell in cells)
        {
            var go = new GameObject($"cell_{cell.localCoord}");
            go.transform.SetParent(transform, false);
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.sizeDelta = Vector2.one * size;
            // Normalizza rispetto a pMin — la hitbox parte sempre da (0,0)
            rt.anchoredPosition = new Vector2(
                (cell.localCoord.x - pMinX) * size,
                (cell.localCoord.y - pMinY) * size);

            if (cell.occupiesSpace)
            {
                img.color = piece.data.color;
                if (cell.overrideSprite != null)
                {
                    var s = new GameObject("override");
                    s.transform.SetParent(go.transform, false);
                    var si = s.AddComponent<Image>();
                    si.sprite = cell.overrideSprite;
                    si.raycastTarget = false;
                    si.preserveAspect = true;
                    var sr = s.GetComponent<RectTransform>();
                    sr.anchorMin = Vector2.zero; sr.anchorMax = Vector2.one;
                    sr.offsetMin = sr.offsetMax = Vector2.zero;
                }
            }
            else
            {
                img.enabled = false;
                var sp = cell.nonPhysicalSprite ?? cell.overrideSprite;
                if (sp != null)
                {
                    var s = new GameObject("nonphys");
                    s.transform.SetParent(go.transform, false);
                    var si = s.AddComponent<Image>();
                    si.sprite = sp;
                    si.raycastTarget = false;
                    si.preserveAspect = true;
                    var sr = s.GetComponent<RectTransform>();
                    sr.anchorMin = Vector2.zero; sr.anchorMax = Vector2.one;
                    sr.offsetMin = sr.offsetMax = Vector2.zero;
                }
            }
        }

        if (piece.data.pieceSprite != null)
        {
            Vector2 rawOffset = piece.data.pieceSpriteOffset;
            bool isRotated = piece.rotation == 1 || piece.rotation == 3;
            Vector2 rawScale = isRotated ? piece.data.pieceSpriteScaleRotated : piece.data.pieceSpriteScale;

            // Con rotazione CSS di -90/90°, W e H si scambiano visivamente.
            // Per ottenere le dimensioni corrette post-rotazione, scambiamo physW/physH
            // quando lo sprite è ruotato, così dopo la rotazione CSS tornano corretti.
            float spriteW, spriteH;
            if (isRotated)
            {
                spriteW = physH * size * rawScale.x;
                spriteH = physW * size * rawScale.y;
            }
            else
            {
                spriteW = physW * size * rawScale.x;
                spriteH = physH * size * rawScale.y;
            }

            Vector2 offset = piece.rotation switch
            {
                1 => new Vector2(-rawOffset.y, rawOffset.x),
                2 => new Vector2(-rawOffset.x, -rawOffset.y),
                3 => new Vector2(rawOffset.y, -rawOffset.x),
                _ => rawOffset
            };

            // Con normalizzazione, il centro è sempre al centro della sizeDelta
            float cx = physW * size * 0.5f;
            float cy = physH * size * 0.5f;

            var sGo = new GameObject("piece_sprite");
            sGo.transform.SetParent(transform, false);
            var sImg = sGo.AddComponent<Image>();
            sImg.sprite = piece.data.pieceSprite;
            sImg.raycastTarget = false;
            sImg.preserveAspect = false;

            var sRt = sGo.GetComponent<RectTransform>();
            sRt.anchorMin = Vector2.zero;
            sRt.anchorMax = Vector2.zero;
            sRt.pivot = new Vector2(0.5f, 0.5f);
            sRt.anchoredPosition = new Vector2(cx + offset.x, cy + offset.y);
            sRt.sizeDelta = new Vector2(spriteW, spriteH);
            sRt.localEulerAngles = new Vector3(0, 0, -piece.rotation * 90f);
        }

        var resizer = GetComponentInChildren<SpringResizer>();
        if (resizer != null)
        {
            resizer.transform.SetAsLastSibling();
            resizer.RefreshHandles();
        }
    }
}

/// <summary>
/// Helper statico per i suoni di piazzamento: un solo AudioSource 2D
/// condiviso, creato al primo uso (PlayOneShot, nessun GameObject per suono).
/// </summary>
public static class PlaceFeedbackAudio
{
    static AudioSource source;

    public static void Play(AudioClip clip, float volume = 1f)
    {
        if (clip == null) return;
        if (source == null)
        {
            var go = new GameObject("PlaceFeedbackAudio");
            UnityEngine.Object.DontDestroyOnLoad(go);
            source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f; // suono 2D
        }
        source.PlayOneShot(clip, Mathf.Clamp01(volume));
    }
}