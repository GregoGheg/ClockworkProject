using System.Reflection;
using UnityEngine;

[RequireComponent(typeof(PieceDragger))]
public class PieceSwapHandler : MonoBehaviour
{
    PieceDragger dragger;
    GridManager grid;

    static readonly FieldInfo _origParent = typeof(PieceDragger)
        .GetField("originalParent", BindingFlags.NonPublic | BindingFlags.Instance);

    void Awake() => dragger = GetComponent<PieceDragger>();

    void Update()
    {
        if (grid != null) return;
        if (dragger.grid == null) return;
        grid = dragger.grid;
        grid.OnDropOnOccupied += OnDropOnOccupied;
    }

    void OnDisable()
    {
        if (grid != null) grid.OnDropOnOccupied -= OnDropOnOccupied;
        grid = null;
    }

    void OnDropOnOccupied(Piece dropped, Vector2Int targetCoord, Vector2Int prevPos)
    {
        if (dropped != dragger.piece) return;

        var other = grid.GetCell(targetCoord)?.occupant;
        if (other == null || other == dropped) return;

        PieceDragger otherDragger = null;
        foreach (var d in grid.GetComponentsInChildren<PieceDragger>(true))
            if (d.piece == other) { otherDragger = d; break; }
        if (otherDragger == null) return;

        bool fromTray = prevPos.x < 0;
        bool otherIsMulti = (other.data?.cells?.Count ?? 1) > 1;
        var otherPos = other.gridPosition;

        // Rimuovi l'altro
        grid.Remove(other);

        // Piazza il pezzo trascinato nella cella target
        // Lo facciamo noi qui così TryPlace di PieceDragger troverà la cella occupata
        // da se stesso e non ripiazzerà
        bool placedDropped = grid.TryPlace(dropped, targetCoord);
        if (!placedDropped)
        {
            // Non si può piazzare — rimetti l'altro e annulla
            grid.TryPlace(other, otherPos);
            return;
        }

        // Aggiorna visual del pezzo trascinato
        dragger.SnapToGridPublic(targetCoord);
        dragger.canvasGroup.alpha = 1f;
        dragger.canvasGroup.blocksRaycasts = true;
        dragger.transform.SetParent(grid.transform, false);

        // Gestisci l'altro pezzo
        if (fromTray || otherIsMulti)
        {
            SendOtherToTray(otherDragger);
        }
        else
        {
            if (prevPos.x >= 0 && grid.TryPlace(other, prevPos))
            {
                otherDragger.transform.SetParent(grid.transform, false);
                otherDragger.SnapToGridPublic(prevPos);
                otherDragger.canvasGroup.alpha = 1f;
                otherDragger.canvasGroup.blocksRaycasts = true;
                otherDragger.gameObject.SetActive(true);
                otherDragger.RedrawVisual();
            }
            else
            {
                SendOtherToTray(otherDragger);
            }
        }

        grid.OnGridChanged?.Invoke();
    }

    void SendOtherToTray(PieceDragger other)
    {
        var origParent = (Transform)(_origParent?.GetValue(other));
        TraySlot slot = origParent?.GetComponent<TraySlot>()
                     ?? origParent?.GetComponentInParent<TraySlot>();

        other.piece.gridPosition = new Vector2Int(-1, -1);

        if (slot != null)
            slot.ReturnFromDrag(other);
        else
        {
            if (origParent != null)
                other.transform.SetParent(origParent, false);
            other.canvasGroup.alpha = 1f;
            other.canvasGroup.blocksRaycasts = true;
            other.gameObject.SetActive(true);
        }
        other.RedrawVisual();
    }
}