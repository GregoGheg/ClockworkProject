using UnityEngine;

/// <summary>
/// Singleton che gestisce lo stato dell'input globale.
/// La rotella del mouse fa UNA cosa sola alla volta:
/// - Se ModificaMolla = true → SpringResizer la usa, TrayScrollHandler no
/// - Se ModificaMolla = false → TrayScrollHandler la usa, SpringResizer no
/// 
/// ModificaMolla si attiva automaticamente quando si seleziona una molla piazzata,
/// e si disattiva quando si deseleziona o si clicca sul tray.
/// </summary>
public class InputStateManager : MonoBehaviour
{
    public static InputStateManager Instance { get; private set; }

    [Header("Stato corrente")]
    [SerializeField] bool modificaMolla = false;

    public bool ModificaMolla
    {
        get => modificaMolla;
        set
        {
            if (modificaMolla == value) return;
            modificaMolla = value;
            OnModificaMollaChanged?.Invoke(value);
        }
    }

    public System.Action<bool> OnModificaMollaChanged;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }
}
