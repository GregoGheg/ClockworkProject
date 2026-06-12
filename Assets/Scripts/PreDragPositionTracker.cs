using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Registra la posizione del pezzo su GridManager prima che OnBeginDrag la cancelli.
/// Attacca sullo stesso GameObject di PieceDragger.
/// </summary>
[RequireComponent(typeof(PieceDragger))]
public class PreDragPositionTracker : MonoBehaviour, IBeginDragHandler
{
    PieceDragger dragger;

    void Awake() => dragger = GetComponent<PieceDragger>();

    public void OnBeginDrag(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;
        if (dragger.grid == null) return;
        // Salva la posizione PRIMA che PieceDragger.OnBeginDrag chiami grid.Remove
        dragger.grid.RegisterPreDragPosition(dragger.piece, dragger.piece.gridPosition);
    }
}
