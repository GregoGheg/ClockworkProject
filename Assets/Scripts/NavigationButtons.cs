using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gestisce i bottoni di navigazione tra livelli.
///
/// - btnLeft/Right/Up/Down: frecce "AVANTI" verso le uscite sbloccate
///   (una destinazione soddisfatta in quella direzione, anche non adiacente).
/// - btnBack: UNICO bottone di ritorno. Si riposiziona automaticamente sul
///   lato corrispondente alla direzione del livello-parent e permette SEMPRE
///   di tornare a un livello già visitato/completato.
///
/// SETUP btnBack (opzionale ma consigliato):
/// - Assegna btnBack nell'Inspector.
/// - Imposta i 4 anchoredPosition per ogni lato (left/right/up/down) tramite
///   i campi backPos*; se li lasci a zero, il bottone resta dov'è e cambia
///   solo rotazione/visibilità.
/// </summary>
public class NavigationButtons : MonoBehaviour
{
    public WorldNavigator navigator;

    [Header("Frecce AVANTI (verso le uscite)")]
    public Button btnLeft;
    public Button btnRight;
    public Button btnUp;
    public Button btnDown;

    [Header("Bottone RITORNO unico (si riposiziona sul lato giusto)")]
    public Button btnBack;
    [Tooltip("Posizioni anchored del bottone ritorno per ciascun lato")]
    public Vector2 backPosLeft = new Vector2(-400f, 0f);
    public Vector2 backPosRight = new Vector2(400f, 0f);
    public Vector2 backPosUp = new Vector2(0f, 400f);
    public Vector2 backPosDown = new Vector2(0f, -400f);
    [Tooltip("Ruota il bottone ritorno in base alla direzione (0=Left di default)")]
    public bool rotateBackButton = true;

    RectTransform backRT;

    void Start()
    {
        if (btnLeft) btnLeft.onClick.AddListener(() => navigator.TryNavigate(Vector2Int.left));
        if (btnRight) btnRight.onClick.AddListener(() => navigator.TryNavigate(Vector2Int.right));
        if (btnUp) btnUp.onClick.AddListener(() => navigator.TryNavigate(Vector2Int.up));
        if (btnDown) btnDown.onClick.AddListener(() => navigator.TryNavigate(Vector2Int.down));

        if (btnBack)
        {
            backRT = btnBack.GetComponent<RectTransform>();
            btnBack.onClick.AddListener(() => navigator.NavigateBack());
        }
    }

    void Update()
    {
        if (navigator == null || navigator.config == null) return;

        int cur = navigator.CurrentIndex;
        var curPos = navigator.config.levels[cur].mapPosition;

        // ── Frecce AVANTI: solo uscite sbloccate (mai il ritorno) ─────────
        if (btnLeft) btnLeft.gameObject.SetActive(CanGoForward(curPos, Vector2Int.left));
        if (btnRight) btnRight.gameObject.SetActive(CanGoForward(curPos, Vector2Int.right));
        if (btnUp) btnUp.gameObject.SetActive(CanGoForward(curPos, Vector2Int.up));
        if (btnDown) btnDown.gameObject.SetActive(CanGoForward(curPos, Vector2Int.down));

        // ── Bottone RITORNO unico ─────────────────────────────────────────
        UpdateBackButton();
    }

    /// <summary>Una freccia avanti è visibile se in quella direzione c'è
    /// un'uscita sbloccata (zona soddisfatta), adiacente o lontana.</summary>
    bool CanGoForward(Vector2Int curPos, Vector2Int dir)
    {
        var targetPos = curPos + dir;

        // Livello adiacente con zona sbloccata
        for (int i = 0; i < navigator.config.levels.Length; i++)
        {
            if (navigator.config.levels[i].mapPosition != targetPos) continue;
            // NON è il ritorno (quello è gestito da btnBack)
            if (navigator.IsParent(i, navigator.CurrentIndex)) return false;
            return navigator.IsZoneUnlocked(targetPos);
        }

        // Uscita verso livello non adiacente in questa direzione
        return navigator.GetUnlockedTargetInDirection(dir) >= 0;
    }

    void UpdateBackButton()
    {
        if (btnBack == null) return;

        var backDir = navigator.GetReturnDirection();
        bool canReturn = navigator.GetReturnLevelIndex() >= 0 && backDir != Vector2Int.zero;

        btnBack.gameObject.SetActive(canReturn);
        if (!canReturn || backRT == null) return;

        // Riposiziona sul lato corrispondente
        if (backDir == Vector2Int.left) backRT.anchoredPosition = backPosLeft;
        else if (backDir == Vector2Int.right) backRT.anchoredPosition = backPosRight;
        else if (backDir == Vector2Int.up) backRT.anchoredPosition = backPosUp;
        else if (backDir == Vector2Int.down) backRT.anchoredPosition = backPosDown;

        // Ruota la freccia per puntare nella direzione di ritorno
        if (rotateBackButton)
        {
            float angle = 0f; // default: punta a sinistra
            if (backDir == Vector2Int.left) angle = 0f;
            else if (backDir == Vector2Int.right) angle = 180f;
            else if (backDir == Vector2Int.up) angle = -90f;
            else if (backDir == Vector2Int.down) angle = 90f;
            backRT.localEulerAngles = new Vector3(0, 0, angle);
        }
    }
}