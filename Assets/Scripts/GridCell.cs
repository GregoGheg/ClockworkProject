using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[RequireComponent(typeof(Image))]
public class GridCell : MonoBehaviour, IPointerClickHandler
{
    float lastClickTime = -1f;
    const float doubleClickThreshold = 0.3f;

    public void OnPointerClick(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;

        float now = Time.unscaledTime;
        bool isDouble = (now - lastClickTime) < doubleClickThreshold;
        UnityEngine.Debug.Log($"[GridCell] click coord={coord} isDouble={isDouble} diff={now - lastClickTime:F3} occupant={grid?.GetCell(coord)?.occupant?.data?.name ?? "null"}");
        lastClickTime = now;

        if (!isDouble) return;
        if (grid == null || !active) return;
        if (grid.GetCell(coord)?.occupant != null) return;

        PieceDragger.TryPlaceLastSelected(grid, coord);
    }

    [Header("Colori")]
    public Color emptyColor = new Color(0.15f, 0.15f, 0.2f, 1f);
    public Color inactiveColor = new Color(0f, 0f, 0f, 0f);        // trasparente
    public Color previewValid = new Color(0.2f, 0.8f, 0.3f, 0.5f);
    public Color previewInvalid = new Color(0.9f, 0.2f, 0.2f, 0.5f);
    public Color sourceColor = new Color(0.2f, 0.9f, 0.4f, 1f);
    public Color destColor = new Color(0.9f, 0.3f, 0.2f, 1f);

    // Layer separati
    Image bg;           // sfondo della cella (texture casella)
    Image spriteImg;    // sprite del componente sopra il bg
    Image overlayImg;   // overlay colorato (preview, circuit visualizer)

    Vector2Int coord;
    GridManager grid;
    bool active;

    public Vector2Int Coord => coord;

    enum CellType { Normal, Source, Destination }
    CellType cellType = CellType.Normal;

    public void Init(Vector2Int c, float size, GridManager gm, bool isActive)
    {
        coord = c;
        grid = gm;
        active = isActive;

        var rt = GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.sizeDelta = Vector2.one * size;
        rt.anchoredPosition = new Vector2(c.x * size, c.y * size);

        // Layer 1: background
        bg = GetComponent<Image>();
        bg.raycastTarget = isActive; // le celle attive ricevono click
        bg.color = isActive ? emptyColor : inactiveColor;

        // Layer 2: sprite del componente
        var sGo = new GameObject("Sprite");
        sGo.transform.SetParent(transform, false);
        spriteImg = sGo.AddComponent<Image>();
        spriteImg.raycastTarget = false;
        spriteImg.gameObject.SetActive(false);
        var srt = sGo.GetComponent<RectTransform>();
        srt.anchorMin = new Vector2(0.1f, 0.1f);
        srt.anchorMax = new Vector2(0.9f, 0.9f);
        srt.offsetMin = srt.offsetMax = Vector2.zero;

        // Layer 3: overlay colore (preview / circuit)
        var oGo = new GameObject("Overlay");
        oGo.transform.SetParent(transform, false);
        overlayImg = oGo.AddComponent<Image>();
        overlayImg.raycastTarget = false;
        overlayImg.color = Color.clear;
        var ort = oGo.GetComponent<RectTransform>();
        ort.anchorMin = Vector2.zero;
        ort.anchorMax = Vector2.one;
        ort.offsetMin = ort.offsetMax = Vector2.zero;
    }

    // ── Stato cella ───────────────────────────────────────────────────────

    public void SetEmpty()
    {
        if (!active) { ForceClean(); return; }
        bg.color = cellType switch
        {
            CellType.Source => sourceColor,
            CellType.Destination => destColor,
            _ => emptyColor
        };
        spriteImg.gameObject.SetActive(false);
        overlayImg.color = Color.clear;
    }

    public void SetOccupied(Color pieceColor, PieceData.CellDef cellDef, PieceData pieceData = null)
    {
        if (!active) { ForceClean(); return; }
        // Il visual è gestito da PieceDragger — la GridCell non cambia aspetto
        spriteImg.gameObject.SetActive(false);
        overlayImg.color = Color.clear;
    }

    public void SetNonPhysical(PieceData.CellDef cell, PieceData data)
    {
        if (!active) { ForceClean(); return; }
        var sprite = cell.nonPhysicalSprite ?? cell.overrideSprite ?? data.pieceSprite;
        if (sprite == null) return;
        spriteImg.sprite = sprite;
        spriteImg.gameObject.SetActive(true);
    }

    public void SetPreview(bool valid)
    {
        if (!active) { ForceClean(); return; }
        overlayImg.color = valid ? previewValid : previewInvalid;
    }

    public void ClearPreview()
    {
        if (!active) { ForceClean(); return; }
        overlayImg.color = Color.clear;
        var cell = grid.GetCell(coord.x, coord.y);
        if (cell?.occupant != null)
            SetOccupied(cell.occupant.data.color, default, cell.occupant.data);
        else
            SetEmpty();
    }

    // Forza pulizia su casella inattiva — nessuna immagine visibile
    public void ForceClean()
    {
        bg.color = inactiveColor;
        spriteImg.gameObject.SetActive(false);
        overlayImg.color = Color.clear;
    }

    public void SetAsSource() { cellType = CellType.Source; bg.color = sourceColor; }
    public void SetAsDestination() { cellType = CellType.Destination; bg.color = destColor; }

    // Per CircuitVisualizer — usa l'overlay, non il bg
    public void SetFlowColor(Color c)
    {
        if (overlayImg == null) return;
        if (!active && c != Color.clear) return; // inattive: solo reset permesso
        overlayImg.color = c == Color.clear ? Color.clear : new Color(c.r, c.g, c.b, 0.6f);
    }
}