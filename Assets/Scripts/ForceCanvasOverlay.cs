using UnityEngine;

/// Attacca questo script sul GameObject "Canvas".
/// Forza Screen Space - Overlay all'avvio e stampa un log di conferma.
[RequireComponent(typeof(Canvas))]
public class ForceCanvasOverlay : MonoBehaviour
{
    void Awake()
    {
        var c = GetComponent<Canvas>();
        if (c.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            Debug.LogWarning($"[ForceCanvasOverlay] Canvas era in modalita' {c.renderMode} — forzato a ScreenSpaceOverlay.");
            c.renderMode = RenderMode.ScreenSpaceOverlay;
        }
        else
        {
            Debug.Log("[ForceCanvasOverlay] Canvas OK — ScreenSpaceOverlay.");
        }
    }
}
