using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class SpringResizer : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    const int MIN_LEN = 2;
    const int MAX_LEN = 4;

    PieceDragger dragger;
    GridManager grid;

    bool isResizing = false;
    float dragStartY = 0f;
    int lengthAtStart = 0;
    int lastTargetLen = 0;

    float PixelsPerCell => grid.cellSize;

    public void Init(PieceDragger pd, GridManager gm)
    {
        dragger = pd;
        grid = gm;
        if (dragger.piece.runtimeLength == null)
            dragger.piece.runtimeLength = dragger.piece.data.cells.Count;
    }

    public void SetHandlesVisible(bool v) { }
    public void RefreshHandles() { }

    Vector2Int Axis => (dragger.piece.rotation % 2) == 0 ? Vector2Int.right : Vector2Int.up;
    int Length => dragger.piece.runtimeLength ?? dragger.piece.data.cells.Count;

    // ── Drag tasto destro — resize ────────────────────────────────────────
    public void OnBeginDrag(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Right) return;
        if (!dragger.IsSelected()) return;
        if (dragger.piece.gridPosition.x < 0) return;

        isResizing = true;
        dragStartY = e.position.y;
        lengthAtStart = Length;
        lastTargetLen = Length;

        // Blocca la propagazione così PieceDragger non riceve questo drag
        e.Use();
    }

    public void OnDrag(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Right) return;
        if (!isResizing) return;

        e.Use();

        float deltaY = e.position.y - dragStartY;
        int deltaCells = Mathf.RoundToInt(deltaY / PixelsPerCell);
        int targetLen = Mathf.Clamp(lengthAtStart + deltaCells, MIN_LEN, MAX_LEN);

        if (targetLen == lastTargetLen) return;
        TryResize(targetLen);
        lastTargetLen = targetLen;
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Right) return;
        isResizing = false;
        e.Use();
    }

    public void TryResizePublic(int newLen) => TryResize(newLen);
    public void TryResizePublicFrom(int newLen, Vector2Int newOrigin) => TryResizeFrom(newLen, newOrigin);

    void TryResize(int newLen) => TryResizeFrom(newLen, dragger.piece.gridPosition);

    void TryResizeFrom(int newLen, Vector2Int newOrigin)
    {
        int oldLen = Length;
        var oldOrigin = dragger.piece.gridPosition;

        if (!CheckBounds(newLen, newOrigin)) return;

        grid.Remove(dragger.piece);
        dragger.piece.runtimeLength = newLen;

        if (grid.TryPlace(dragger.piece, newOrigin))
        {
            dragger.SnapToGridPublic(newOrigin);
            dragger.RedrawVisual();
        }
        else
        {
            dragger.piece.runtimeLength = oldLen;
            grid.TryPlace(dragger.piece, oldOrigin);
            dragger.SnapToGridPublic(oldOrigin);
        }
    }

    bool CheckBounds(int newLen, Vector2Int origin)
    {
        var axis = Axis;
        for (int i = 0; i < newLen; i++)
        {
            var coord = origin + axis * i;
            if (!grid.IsInBounds(coord)) return false;
            var cell = grid.GetCell(coord);
            if (cell != null && !cell.isActive) return false;
        }
        return true;
    }
}