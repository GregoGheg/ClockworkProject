using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Gestisce la rotella del mouse globalmente.
/// Attacca su un GameObject nella scena.
/// </summary>
public class GlobalScrollInput : MonoBehaviour
{
    TrayScrollHandler trayScroll;

    void Start()
    {
        trayScroll = FindFirstObjectByType<TrayScrollHandler>();
    }

    void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Approximately(scroll, 0f)) return;

        // Scrolla sempre l'inventario con la rotella
        trayScroll?.ScrollBy(scroll);
    }
}
