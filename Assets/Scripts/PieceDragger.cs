using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CanvasGroup))]
public class PieceDragger : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler,
    IPointerClickHandler, IPointerDownHandler, IPointerUpHandler
{
    [HideInInspector] public Piece piece;
    [HideInInspector] public GridManager grid;

    public CanvasGroup canvasGroup { get; private set; }

    Canvas rootCanvas;
    RectTransform rectTransform;
    Vector2 originalAnchoredPos;
    Transform originalParent;

    static PieceDragger selected;
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
        UnityEngine.Debug.Log($"[OnPointerClick] button={e.button} gridPos={piece.gridPosition}");
        if (e.button == PointerEventData.InputButton.Left)
            Select();
        else if (e.button == PointerEventData.InputButton.Middle)
        {
            // Usa solo il dragger che è effettivamente il "proprietario" del pezzo
            // (quello la cui gridPosition coincide con la gridPosition del pezzo)
            // così i PieceDragger delle altre celle non interferiscono
            if (piece.gridPosition.x >= 0 && piece.gridPosition == piece.gridPosition)
                ReturnToTray();
        }
    }

    public void OnPointerDown(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Right) return;
        var resizer = GetComponentInChildren<SpringResizer>();
        if (resizer == null || !IsSelected() || piece.gridPosition.x < 0) return;

        isResizing = true;
        bool isVertical = piece.rotation % 2 == 1;
        resizeDragStart = isVertical ? e.position.y : e.position.x;
        resizeLenStart = piece.runtimeLength ?? piece.data.cells.Count;
        resizeLastLen = resizeLenStart;

        bool iv2 = piece.rotation % 2 == 1;
        var ax = iv2 ? Vector2Int.up : Vector2Int.right;
        var orig = piece.gridPosition;
        var head = orig + ax * (resizeLenStart - 1);
        var gc = grid.ScreenToGridCoord(e.position, null);
        float dO = Vector2Int.Distance(gc, orig);
        float dH = Vector2Int.Distance(gc, head);
        resizingFromTail = dO < dH;
        resizeHeadPos = resizingFromTail ? head : orig;
    }

    public void OnPointerUp(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Right) return;
        isResizing = false;
    }

    public void Select()
    {
        if (selected != null && selected != this) selected.Deselect();
        selected = this;
        SetHandlesVisible(true);
    }

    public void Deselect()
    {
        SetHandlesVisible(false);
    }

    public static void ClearSelection() => selected = null;

    void SetHandlesVisible(bool v)
    {
        GetComponentInChildren<SpringResizer>()?.SetHandlesVisible(v);
    }

    // ── Drag ──────────────────────────────────────────────────────────────
    public void OnBeginDrag(PointerEventData e)
    {

        if (e.button != PointerEventData.InputButton.Left) return;

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

            float eff = resizingFromTail ? -delta : delta;
            int dc = Mathf.RoundToInt(eff / pixPerCell);
            int newLen = Mathf.Clamp(resizeLenStart + dc, 2, 4);
            if (newLen != resizeLastLen)
            {
                bool iv3 = piece.rotation % 2 == 1;
                var ax3 = iv3 ? Vector2Int.up : Vector2Int.right;
                Vector2Int no = resizingFromTail
                    ? resizeHeadPos - ax3 * (newLen - 1)
                    : piece.gridPosition;
                resizer.TryResizePublicFrom(newLen, no);
                resizeLastLen = newLen;
            }
            return;
        }

        if (e.button != PointerEventData.InputButton.Left) return;

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

        canvasGroup.blocksRaycasts = true;
        canvasGroup.alpha = 1f;
        grid.ClearPreview();

        var gc = grid.ScreenToGridCoord(e.position, null);
        // Salva snapshot PRIMA del piazzamento (con la posizione precedente)
        // ma solo se il drop avrà successo — usiamo CanPlace per verificare
        bool canPlace = grid.CanPlace(piece, gc);
        if (canPlace) LocalGameManager?.SaveSnapshot();

        // ── Logica cinghia ────────────────────────────────────────────
        if (isBelt)
        {
            PlaceBelt(gc);
            return;
        }

        bool placed = grid.TryPlace(piece, gc);

        if (placed)
        {
            everMoved = true;
            SnapToGrid(gc);
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
    void PlaceBelt(Vector2Int dropCell)
    {
        // Trova il gear più vicino alla cella di drop (end)
        var gearB = FindGearAt(dropCell);
        if (gearB == null) { GoBackToTray(); return; }

        // Cerca un secondo gear adiacente (distanza 1 in 8 direzioni)
        // Prende il più vicino che non sia lo stesso
        Vector2Int? gearA = null;
        float minDist = float.MaxValue;
        for (int dx2 = -1; dx2 <= 1; dx2++)
            for (int dy2 = -1; dy2 <= 1; dy2++)
            {
                if (dx2 == 0 && dy2 == 0) continue;
                var neighbor = gearB.Value + new Vector2Int(dx2, dy2);
                if (!grid.IsInBounds(neighbor)) continue;
                var st = grid.GetCell(neighbor.x, neighbor.y);
                if (st?.occupant == null || !st.occupant.data.isGear) continue;
                var gPos = st.occupant.gridPosition;
                if (gPos == gearB.Value) continue;

                // Preferisce il gear nella direzione del drag
                float d = Vector2Int.Distance(gPos, dropCell);
                if (d < minDist) { minDist = d; gearA = gPos; }
            }

        if (gearA == null) { GoBackToTray(); return; }

        piece.gridPosition = gearA.Value;
        beltEndCell = gearB.Value;
        everMoved = true;

        transform.SetParent(grid.transform, false);
        StretchBetween(gearA.Value, gearB.Value);
        grid.OnGridChanged?.Invoke();
    }

    void StretchBetween(Vector2Int from, Vector2Int to)
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
        if (!grid.IsInBounds(coord)) return null;
        var st = grid.GetCell(coord.x, coord.y);
        if (st?.occupant != null && st.occupant.data.isGear) return coord;
        // Cerca anche nelle celle adiacenti per ingranaggi grandi
        for (int dx2 = -1; dx2 <= 1; dx2++)
            for (int dy2 = -1; dy2 <= 1; dy2++)
            {
                if (dx2 == 0 && dy2 == 0) continue;
                var c = coord + new Vector2Int(dx2, dy2);
                if (!grid.IsInBounds(c)) continue;
                var s = grid.GetCell(c.x, c.y);
                if (s?.occupant != null && s.occupant.data.isGear) return s.occupant.gridPosition;
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

        // 1. Nascondi tutti i dragger extra dello stesso pezzo (celle secondarie)
        var allDraggers = grid.GetComponentsInChildren<PieceDragger>(true);
        foreach (var d in allDraggers)
            if (d.piece == piece && d != this)
                d.gameObject.SetActive(false);

        // 2. Rimuovi dalla griglia
        var localGM = LocalGameManager;
        localGM?.SaveSnapshot();
        grid.Remove(piece);

        // 3. Trova il TraySlot di appartenenza risalendo l'originalParent,
        //    oppure cercandolo nel GameManager — senza dipendere dal singleton
        TraySlot targetSlot = originalParent?.GetComponent<TraySlot>()
                           ?? originalParent?.GetComponentInParent<TraySlot>();

        if (targetSlot == null && localGM != null)
            localGM.ReturnDraggerToTray(this);
        else if (targetSlot != null)
            targetSlot.ReturnFromDrag(this);
        else
        {
            // Fallback: cerca un TraySlot nella scena che contenga questo dragger
            foreach (var slot in UnityEngine.Object.FindObjectsByType<TraySlot>(
                UnityEngine.FindObjectsSortMode.None))
            {
                if (slot.GetDraggers().Contains(this))
                {
                    slot.ReturnFromDrag(this);
                    break;
                }
            }
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

            float spriteW, spriteH;
            if (isRotated && piece.data.resizable)
            {
                int len = piece.runtimeLength ?? piece.data.cells.Count;
                float t = (len - 2) / 2f;
                float scaleX = Mathf.Lerp(2.27f, 4.80f, t);
                float scaleY = Mathf.Lerp(2.15f, 1.18f, t);
                spriteW = physW * size * scaleX;
                spriteH = physH * size * scaleY;
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