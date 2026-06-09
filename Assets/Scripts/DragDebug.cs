using UnityEngine;
using UnityEngine.EventSystems;

/// Script temporaneo di debug — attaccalo al prefab PieceDragger.
/// Stampa in Console ogni evento di pointer che riceve.
public class DragDebug : MonoBehaviour,
    IPointerDownHandler, IPointerUpHandler, IPointerEnterHandler
{
    public void OnPointerDown(PointerEventData e)
    {
        Debug.Log($"[DragDebug] PointerDOWN su {gameObject.name} | pos={e.position}");
    }
    public void OnPointerUp(PointerEventData e)
    {
        Debug.Log($"[DragDebug] PointerUP su {gameObject.name}");
    }
    public void OnPointerEnter(PointerEventData e)
    {
        Debug.Log($"[DragDebug] PointerENTER su {gameObject.name}");
    }
}
