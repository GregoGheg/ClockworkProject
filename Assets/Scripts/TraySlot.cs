using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class TraySlot : MonoBehaviour
{
    List<PieceDragger> draggers = new();
    Text badgeText;
    Image badgeBg;
    int available = 0; // contatore esplicito

    const float ROW_HEIGHT = 80f;

    public void Init(List<PieceDragger> pieces)
    {
        draggers = pieces;
        BuildRow();

        available = draggers.Count;

        foreach (var d in draggers)
        {
            d.transform.SetParent(transform, false);
            ResetDraggerPosition(d);
            var cg = d.canvasGroup ?? d.GetComponent<CanvasGroup>();
            if (cg != null) cg.alpha = 0f;
            d.onReturnedToTray += () => OnDraggerReturned(d);
            d.onRemovedFromTray += () => OnDraggerRemoved(d);
        }

        UpdateBadge();
        UpdateVisibility();
    }

    // Posizione fissa nel tray — sempre uguale
    void ResetDraggerPosition(PieceDragger d)
    {
        var rt = d.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
    }

    // Chiamato quando un dragger inizia il drag
    void OnDraggerRemoved(PieceDragger d) { } // gestito da HideDuringDrag

    // Chiamato quando un dragger torna nel tray (drop fallito)
    void OnDraggerReturned(PieceDragger d)
    {
        d.transform.SetParent(transform, false);
        ResetDraggerPosition(d);
        available = Mathf.Min(available + 1, draggers.Count);
        UpdateBadge();
        UpdateVisibility();
    }

    // API pubblica per PieceDragger
    public List<PieceDragger> GetDraggers() => draggers;

    public void HideDuringDrag(PieceDragger dragging)
    {
        available = Mathf.Max(0, available - 1);
        UpdateBadge();
        UpdateVisibility();
    }
    public void ReturnFromDrag(PieceDragger returning)
    {
        returning.transform.SetParent(transform, false);
        ResetDraggerPosition(returning);
        available = Mathf.Min(available + 1, draggers.Count);
        UpdateBadge();
        UpdateVisibility();
    }

    // Ripristina lo stato completo (chiamato dall'undo)
    public void SetAvailableCount(int count)
    {
        // Aggiorna available senza toccare i dragger sulla griglia
        int onGrid = 0;
        foreach (var d in draggers)
            if (d.piece.gridPosition.x >= 0) onGrid++;
        available = Mathf.Max(0, count - onGrid);
        UpdateBadge();
    }

    public void ForceRefresh()
    {
        int count = 0;
        foreach (var d in draggers)
        {
            if (d == null) continue;
            bool onGrid = d.piece.gridPosition.x >= 0;
            if (!onGrid)
            {
                d.transform.SetParent(transform, false);
                ResetDraggerPosition(d);
                count++;
            }
        }
        available = count;
        UpdateBadge();
        UpdateVisibility();
    }

    // ── Badge e visibilità ────────────────────────────────────────────────
    void UpdateBadge()
    {
        if (badgeText) badgeText.text = $"×{available}";
        if (badgeBg) badgeBg.color = available > 0
            ? new Color(0f, 0f, 0f, 0.7f)
            : new Color(0.6f, 0.1f, 0.1f, 0.8f);
    }

    void UpdateVisibility()
    {
        bool shownOne = false;
        foreach (var d in draggers)
        {
            if (d == null) continue;
            var cg = d.canvasGroup ?? d.GetComponent<CanvasGroup>();
            if (cg == null) continue;
            bool onGrid = d.piece.gridPosition.x >= 0;
            bool inTray = d.transform.parent == transform;
            if (onGrid) continue;
            if (inTray && !shownOne) { cg.alpha = 1f; shownOne = true; }
            else if (inTray) cg.alpha = 0f;
        }
    }

    // ── Layout ────────────────────────────────────────────────────────────
    void BuildRow()
    {
        var le = gameObject.AddComponent<LayoutElement>();
        le.minHeight = ROW_HEIGHT;
        le.preferredHeight = ROW_HEIGHT;
        le.flexibleWidth = 1f;

        var bg = gameObject.AddComponent<Image>();
        if (draggers.Count > 0 && draggers[0].piece?.data != null)
        {
            var c = draggers[0].piece.data.GetTrayColor();
            bg.color = new Color(c.r * 0.6f, c.g * 0.6f, c.b * 0.6f, 0.4f);
        }
        else bg.color = new Color(0.2f, 0.2f, 0.2f, 0.4f);
        bg.raycastTarget = false;

        // Linea separatrice
        var lineGo = new GameObject("Line");
        lineGo.transform.SetParent(transform, false);
        var lineImg = lineGo.AddComponent<Image>();
        lineImg.color = new Color(1f, 1f, 1f, 0.08f);
        lineImg.raycastTarget = false;
        var lrt = lineGo.GetComponent<RectTransform>();
        lrt.anchorMin = new Vector2(0f, 0f);
        lrt.anchorMax = new Vector2(1f, 0f);
        lrt.pivot = new Vector2(0.5f, 0f);
        lrt.sizeDelta = new Vector2(0f, 1f);
        lrt.anchoredPosition = Vector2.zero;

        // Badge
        var badgeGo = new GameObject("Badge");
        badgeGo.transform.SetParent(transform, false);
        badgeBg = badgeGo.AddComponent<Image>();
        badgeBg.color = new Color(0f, 0f, 0f, 0.7f);
        badgeBg.raycastTarget = false;
        var brt = badgeGo.GetComponent<RectTransform>();
        brt.anchorMin = new Vector2(1f, 0.5f);
        brt.anchorMax = new Vector2(1f, 0.5f);
        brt.pivot = new Vector2(1f, 0.5f);
        brt.sizeDelta = new Vector2(64f, 36f);
        brt.anchoredPosition = new Vector2(-4f, 0f);

        var textGo = new GameObject("Count");
        textGo.transform.SetParent(badgeGo.transform, false);
        badgeText = textGo.AddComponent<Text>();
        badgeText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        badgeText.fontSize = 18;
        badgeText.fontStyle = FontStyle.Bold;
        badgeText.color = Color.white;
        badgeText.alignment = TextAnchor.MiddleCenter;
        badgeText.raycastTarget = false;
        badgeText.resizeTextForBestFit = false;
        // Evita blur: usa dimensioni pixel-perfect
        var trt = textGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
    }
}