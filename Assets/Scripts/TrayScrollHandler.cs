using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.InputSystem;

/// <summary>
/// Attacca su TrayPanel.
/// - Rotella mouse → scrolla sempre l'inventario
/// - Drag su area vuota → scrolla
/// - Drag su pezzo → il PieceDragger gestisce il drag
/// </summary>
[RequireComponent(typeof(ScrollRect))]
public class TrayScrollHandler : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("Scroll")]
    public float mouseWheelSpeed = 200f;

    ScrollRect scrollRect;
    bool isDraggingScroll;
    float lastDragY;

    void Awake() => scrollRect = GetComponent<ScrollRect>();

    // La rotella è gestita globalmente da GlobalScrollInput
    void Update() { }

    public void ScrollBy(float scrollY)
    {
        float contentH = GetContentHeight();
        if (contentH <= 0) return;
        float delta = scrollY * mouseWheelSpeed * Time.unscaledDeltaTime / contentH;
        scrollRect.verticalNormalizedPosition =
            Mathf.Clamp01(scrollRect.verticalNormalizedPosition + delta);
    }

    // Drag su area vuota → scrolla
    public void OnBeginDrag(PointerEventData e)
    {
        // Solo tasto sinistro scrolla il tray
        if (e.button != PointerEventData.InputButton.Left) { isDraggingScroll = false; return; }
        if (StartsOnPiece(e)) { isDraggingScroll = false; return; }
        isDraggingScroll = true;
        lastDragY = e.position.y;
    }

    public void OnDrag(PointerEventData e)
    {
        if (e.button != PointerEventData.InputButton.Left) return;
        if (!isDraggingScroll) return;
        float delta = e.position.y - lastDragY;
        lastDragY = e.position.y;
        float contentH = GetContentHeight();
        if (contentH <= 0) return;
        scrollRect.verticalNormalizedPosition =
            Mathf.Clamp01(scrollRect.verticalNormalizedPosition + delta / contentH);
    }

    public void OnEndDrag(PointerEventData e) => isDraggingScroll = false;

    bool StartsOnPiece(PointerEventData e)
    {
        var results = new System.Collections.Generic.List<RaycastResult>();
        EventSystem.current?.RaycastAll(e, results);
        foreach (var r in results)
            if (r.gameObject.GetComponentInParent<PieceDragger>() != null)
                return true;
        return false;
    }

    float GetContentHeight()
    {
        if (scrollRect.content == null) return 1f;
        float viewH = scrollRect.viewport != null
            ? scrollRect.viewport.rect.height
            : GetComponent<RectTransform>().rect.height;
        return Mathf.Max(1f, scrollRect.content.rect.height - viewH);
    }
}