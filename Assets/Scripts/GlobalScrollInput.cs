using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Gestisce la rotella del mouse globalmente.
/// Scrolla il TrayScrollHandler del livello attualmente attivo.
/// </summary>
public class GlobalScrollInput : MonoBehaviour
{
    void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Approximately(scroll, 0f)) return;

        // Trova il TrayScrollHandler attivo in questo frame
        // (quello del livello corrente, non FindFirst che trova sempre il primo)
        var handlers = FindObjectsByType<TrayScrollHandler>(FindObjectsSortMode.None);
        foreach (var h in handlers)
        {
            if (h.gameObject.activeInHierarchy)
            {
                h.ScrollBy(scroll);
                break;
            }
        }
    }
}