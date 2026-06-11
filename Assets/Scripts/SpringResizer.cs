using UnityEngine;
using UnityEngine.EventSystems;

public class SpringResizer : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    const int MIN_LEN = 2;
    const int MAX_LEN = 4;

    PieceDragger dragger;
    GridManager grid;

    bool isResizing = false;
    Vector2 dragStartPos;
    Vector2Int pivotOrigin;
    int rotationAtStart;
    int lengthAtStart = 0;
    int lastTargetLen = 0;

    // Debug
    int dragFrameCount = 0;
    int resizeEventCount = 0;
    float lastResizeTime = 0f;

    float PixelsPerCell => grid.cellSize;
    int Length => dragger.piece.runtimeLength ?? dragger.piece.data.cells.Count;

    public void Init(PieceDragger pd, GridManager gm)
    {
        dragger = pd;
        grid = gm;
        if (dragger.piece.runtimeLength == null)
            dragger.piece.runtimeLength = dragger.piece.data.cells.Count;
    }

    public void SetHandlesVisible(bool v) { }
    public void RefreshHandles() { }

    public void OnBeginDrag(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Right) return;
        if (!dragger.IsSelected()) return;
        if (dragger.piece.gridPosition.x < 0) return;

        isResizing = true;
        dragStartPos = e.position;
        lengthAtStart = Length;
        lastTargetLen = Length;
        rotationAtStart = dragger.piece.rotation;
        pivotOrigin = dragger.piece.gridPosition;

        dragFrameCount = 0;
        resizeEventCount = 0;

        Debug.Log($"[Spring.BeginDrag] rot={rotationAtStart} len={lengthAtStart} pivot={pivotOrigin} cellSize={PixelsPerCell}");
        e.Use();
    }

    public void OnDrag(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Right) return;
        if (!isResizing) return;
        e.Use();

        dragFrameCount++;

        Vector2 delta = e.position - dragStartPos;
        float projection = rotationAtStart switch
        {
            1 => delta.y,
            2 => -delta.x,
            3 => -delta.y,
            _ => delta.x
        };

        int deltaCells = Mathf.RoundToInt(projection / PixelsPerCell);
        int targetLen = Mathf.Clamp(lengthAtStart + deltaCells, MIN_LEN, MAX_LEN);

        // Log ogni 10 frame per non spammare
        if (dragFrameCount % 10 == 0)
            Debug.Log($"[Spring.Drag] frame={dragFrameCount} delta=({delta.x:F0},{delta.y:F0}) proj={projection:F1} deltaCells={deltaCells} targetLen={targetLen} currentLen={Length}");

        if (targetLen == lastTargetLen)
        {
            // Spiega perché non cambia
            if (dragFrameCount % 10 == 0)
            {
                if (targetLen == MIN_LEN) Debug.Log($"[Spring.Drag] NO RESIZE: già al MIN ({MIN_LEN})");
                else if (targetLen == MAX_LEN) Debug.Log($"[Spring.Drag] NO RESIZE: già al MAX ({MAX_LEN})");
                else Debug.Log($"[Spring.Drag] NO RESIZE: targetLen={targetLen} == lastTargetLen={lastTargetLen} (serve più movimento)");
            }
            return;
        }

        float timeSinceLast = Time.unscaledTime - lastResizeTime;
        Debug.Log($"[Spring.Resize] {lastTargetLen}→{targetLen} timeSinceLast={timeSinceLast * 1000:F0}ms frame={dragFrameCount}");

        bool ok = TryResizeFromPivot(targetLen, pivotOrigin, rotationAtStart);
        if (ok)
        {
            resizeEventCount++;
            lastResizeTime = Time.unscaledTime;
            lastTargetLen = targetLen;
        }
        else
        {
            Debug.Log($"[Spring.Resize] FALLITO per bounds — targetLen={targetLen} pivot={pivotOrigin} rot={rotationAtStart}");
        }
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Right) return;
        isResizing = false;
        Debug.Log($"[Spring.EndDrag] frames={dragFrameCount} resizeEvents={resizeEventCount} finalLen={Length}");
        e.Use();
    }

    public void TryResizePublic(int newLen)
        => TryResizeFromPivot(newLen, dragger.piece.gridPosition, dragger.piece.rotation);

    // pivot = cella i=0 (testa)
    public void TryResizePublicFrom(int newLen, Vector2Int headPivot)
        => TryResizeFromPivot(newLen, headPivot, dragger.piece.rotation);

    // pivot = cella i=len-1 (coda) — usato quando l'utente trascina dalla testa
    public void TryResizePublicFromTail(int newLen, Vector2Int tailPivot)
    {
        int rotation = dragger.piece.rotation;
        // La coda è la cella i=newLen-1. Calcolo la testa (i=0) sottraendo cellDir*(newLen-1)
        Vector2Int cellDir = rotation switch
        {
            1 => new Vector2Int(0, -1),
            2 => new Vector2Int(-1, 0),
            3 => new Vector2Int(0, 1),
            _ => new Vector2Int(1, 0)
        };
        Vector2Int headPos = tailPivot - cellDir * (newLen - 1);
        TryResizeFromPivot(newLen, headPos, rotation);
    }

    bool TryResizeFromPivot(int newLen, Vector2Int pivot, int rotation)
    {
        int oldLen = Length;
        Vector2Int oldOrigin = dragger.piece.gridPosition;

        // Controlla bounds e spiega il motivo del fallimento
        for (int i = 0; i < newLen; i++)
        {
            Vector2Int local = rotation switch
            {
                1 => new Vector2Int(0, -i),
                2 => new Vector2Int(-i, 0),
                3 => new Vector2Int(0, i),
                _ => new Vector2Int(i, 0)
            };
            var coord = pivot + local;
            if (!grid.IsInBounds(coord))
            {
                Debug.Log($"[Spring.Bounds] FUORI BOUNDS: i={i} local={local} coord={coord} pivot={pivot}");
                return false;
            }
            var cell = grid.GetCell(coord);
            if (cell != null && !cell.isActive)
            {
                Debug.Log($"[Spring.Bounds] CELLA INATTIVA: i={i} coord={coord}");
                return false;
            }
            // Controlla se la cella è occupata da un altro pezzo
            if (cell?.occupant != null && cell.occupant != dragger.piece)
            {
                Debug.Log($"[Spring.Bounds] CELLA OCCUPATA: i={i} coord={coord} da {cell.occupant.data?.name}");
                return false;
            }
        }

        grid.Remove(dragger.piece);
        dragger.piece.runtimeLength = newLen;

        if (grid.TryPlace(dragger.piece, pivot))
        {
            dragger.SnapToGridPublic(pivot);
            dragger.RedrawVisual();
            return true;
        }
        else
        {
            Debug.Log($"[Spring.TryPlace] FALLITO dopo Remove: pivot={pivot} newLen={newLen}");
            dragger.piece.runtimeLength = oldLen;
            grid.TryPlace(dragger.piece, oldOrigin);
            dragger.SnapToGridPublic(oldOrigin);
            return false;
        }
    }
}