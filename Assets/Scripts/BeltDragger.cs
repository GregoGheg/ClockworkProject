using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem;

/// <summary>
/// Dragger specializzato per la cinghia meccanica.
/// Si piazza su una cella ingranaggio e si estende di 1 in 8 direzioni.
/// Lo sprite si stretcha tra le due celle.
/// Non usa il sistema resizable standard.
/// </summary>
[RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
public class BeltDragger : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [HideInInspector] public Piece       piece;
    [HideInInspector] public GridManager grid;

    public System.Action onReturnedToTray;

    RectTransform rectTransform;
    CanvasGroup   canvasGroup;
    Canvas        rootCanvas;

    // Stato corrente
    Vector2Int startCell  = new Vector2Int(-1, -1);
    Vector2Int endCell    = new Vector2Int(-1, -1);
    bool       isPlaced   = false;

    Image spriteImage;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        canvasGroup   = GetComponent<CanvasGroup>();
        rootCanvas    = GetComponentInParent<Canvas>();

        // Crea sprite
        var go = new GameObject("belt_sprite");
        go.transform.SetParent(transform, false);
        spriteImage = go.AddComponent<Image>();
        spriteImage.raycastTarget = false;
        if (piece?.data?.pieceSprite != null)
            spriteImage.sprite = piece.data.pieceSprite;
    }

    public void Setup(Piece p, GridManager gm)
    {
        piece = p;
        grid  = gm;
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.zero;
        rectTransform.pivot     = Vector2.zero;
        var size = gm.cellSize;
        rectTransform.sizeDelta = Vector2.one * size;
        if (spriteImage != null && piece.data.pieceSprite != null)
            spriteImage.sprite = piece.data.pieceSprite;
    }

    public void OnBeginDrag(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;
        canvasGroup.blocksRaycasts = false;
        transform.SetParent(rootCanvas.transform, false);
    }

    public void OnDrag(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;
        rectTransform.anchoredPosition += e.delta / rootCanvas.scaleFactor;
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) { canvasGroup.blocksRaycasts = true; return; }
        canvasGroup.blocksRaycasts = true;

        // Converti posizione in cella griglia
        var gridRT  = grid.GetComponent<RectTransform>();
        Vector2 local;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            gridRT, e.position, e.pressEventCamera, out local);

        int cx = Mathf.FloorToInt(local.x / grid.cellSize);
        int cy = Mathf.FloorToInt(local.y / grid.cellSize);
        var dropCell = new Vector2Int(cx, cy);

        // Calcola da quale cella viene il drag (startCell)
        Vector2 startLocal;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            gridRT, e.pressPosition, e.pressEventCamera, out startLocal);
        int sx = Mathf.FloorToInt(startLocal.x / grid.cellSize);
        int sy = Mathf.FloorToInt(startLocal.y / grid.cellSize);
        startCell = new Vector2Int(sx, sy);

        // Distanza massima 1 in 8 direzioni
        int dx = dropCell.x - startCell.x;
        int dy = dropCell.y - startCell.y;
        if (Mathf.Abs(dx) > 1 || Mathf.Abs(dy) > 1 || (dx == 0 && dy == 0))
        {
            GoBackToTray(); return;
        }

        endCell = dropCell;
        PlaceBelt();
    }

    void PlaceBelt()
    {
        piece.gridPosition = startCell;
        isPlaced = true;
        UpdateVisual();
        transform.SetParent(grid.transform, false);
        grid.OnGridChanged?.Invoke();
    }

    void UpdateVisual()
    {
        if (!isPlaced) return;
        float size = grid.cellSize;

        // Posizione e dimensione dello sprite tra startCell e endCell
        var sPos = new Vector2(startCell.x * size, startCell.y * size);
        var ePos = new Vector2(endCell.x * size,   endCell.y * size);
        var mid  = (sPos + ePos) * 0.5f;
        var diff = ePos - sPos;
        float dist  = diff.magnitude + size; // include le celle finali
        float angle = Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg;

        rectTransform.anchoredPosition = sPos;
        rectTransform.sizeDelta        = new Vector2(dist, size * 0.5f);

        if (spriteImage != null)
        {
            var srt = spriteImage.GetComponent<RectTransform>();
            srt.anchorMin        = Vector2.zero;
            srt.anchorMax        = Vector2.one;
            srt.offsetMin        = srt.offsetMax = Vector2.zero;
        }

        // Ruota il container
        rectTransform.localEulerAngles = new Vector3(0, 0, angle);
    }

    public Vector2Int StartCell => startCell;
    public Vector2Int EndCell   => endCell;
    public bool       IsPlaced  => isPlaced;

    void GoBackToTray()
    {
        piece.gridPosition = new Vector2Int(-1, -1);
        isPlaced = false;
        onReturnedToTray?.Invoke();
    }

    public void Remove()
    {
        piece.gridPosition = new Vector2Int(-1, -1);
        isPlaced = false;
        grid.OnGridChanged?.Invoke();
        GoBackToTray();
    }
}
