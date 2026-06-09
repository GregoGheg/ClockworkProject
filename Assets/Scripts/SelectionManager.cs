using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Attacca su un GameObject vuoto nella scena.
/// Deseleziona il pezzo corrente quando si clicca sul vuoto.
/// </summary>
public class SelectionManager : MonoBehaviour
{
    void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;
        if (!mouse.leftButton.wasPressedThisFrame) return;

        // Se il click ha colpito un PieceDragger lascia che sia lui a gestirlo
        var es = EventSystem.current;
        if (es == null) return;

        var results = new System.Collections.Generic.List<RaycastResult>();
        var ped = new PointerEventData(es) { position = mouse.position.ReadValue() };
        es.RaycastAll(ped, results);

        bool hitPiece = false;
        foreach (var r in results)
        {
            if (r.gameObject.GetComponentInParent<PieceDragger>() != null)
            {
                hitPiece = true;
                break;
            }
        }

        if (!hitPiece)
            DeselectAll();
    }

    static void DeselectAll()
    {
        foreach (var dragger in FindObjectsByType<PieceDragger>(FindObjectsSortMode.None))
            dragger.Deselect();

        // Azzera anche il campo static
        PieceDragger.ClearSelection();
    }
}
