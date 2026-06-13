using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Schermata di start del gioco.
///
/// COME USARLA:
/// 1. Attacca questo script sul GameObject "Canvas" (lo stesso con ForceCanvasOverlay).
/// 2. (Consigliato) Assegna in "World Root" il GameObject che contiene il WorldNavigator:
///    verrà disattivato all'avvio e riattivato solo quando premi GIOCA, così il mondo
///    parte davvero dopo la schermata di start.
/// 3. Opzionale: assegna un tuo pannello in "Start Panel". Se lo lasci vuoto,
///    il pannello viene costruito automaticamente (sfondo + titolo + bottone GIOCA).
/// </summary>
public class StartScreen : MonoBehaviour
{
    [Header("Riferimenti")]
    [Tooltip("GameObject con il WorldNavigator: disattivato finché non si preme GIOCA. Opzionale ma consigliato.")]
    public GameObject worldRoot;

    [Tooltip("Pannello di start personalizzato. Vuoto = generato automaticamente.")]
    public GameObject startPanel;

    [Header("Testi (per il pannello autogenerato)")]
    public string gameTitle = "GEAR PUZZLE";
    public string subtitle = "Collega la sorgente alla destinazione";
    public string playLabel = "GIOCA";

    [Header("Stile (per il pannello autogenerato)")]
    public Color backgroundColor = new Color(0.07f, 0.08f, 0.12f, 1f);
    public Color titleColor = new Color(1f, 0.85f, 0.3f, 1f);
    public Color buttonColor = new Color(0.2f, 0.65f, 0.3f, 1f);

    Canvas rootCanvas;

    void Awake()
    {
        rootCanvas = GetComponent<Canvas>();
        if (rootCanvas == null) rootCanvas = GetComponentInParent<Canvas>();
        if (rootCanvas == null) rootCanvas = FindFirstObjectByType<Canvas>();

        // Blocca il mondo finché il giocatore non preme GIOCA
        if (worldRoot != null) worldRoot.SetActive(false);

        if (startPanel == null) startPanel = BuildPanel();
        startPanel.SetActive(true);
        startPanel.transform.SetAsLastSibling(); // sopra a tutto
    }

    /// <summary>Chiamala dal bottone GIOCA (il pannello autogenerato lo fa da solo).</summary>
    public void StartGame()
    {
        if (startPanel != null) startPanel.SetActive(false);
        if (worldRoot != null) worldRoot.SetActive(true);
    }

    // ── Costruzione UI automatica ─────────────────────────────────────────
    GameObject BuildPanel()
    {
        var panel = new GameObject("StartPanel");
        panel.transform.SetParent(rootCanvas.transform, false);

        var prt = panel.AddComponent<RectTransform>();
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.one;
        prt.offsetMin = prt.offsetMax = Vector2.zero;

        // Sfondo opaco che blocca i click sul gioco sottostante
        var bg = panel.AddComponent<Image>();
        bg.color = backgroundColor;
        bg.raycastTarget = true;

        // Titolo
        var title = CreateText(panel.transform, "Title", gameTitle, 72, titleColor, FontStyle.Bold);
        var trt = title.GetComponent<RectTransform>();
        trt.anchorMin = new Vector2(0.5f, 0.65f);
        trt.anchorMax = new Vector2(0.5f, 0.65f);
        trt.sizeDelta = new Vector2(900f, 120f);

        // Sottotitolo
        var sub = CreateText(panel.transform, "Subtitle", subtitle, 26,
            new Color(1f, 1f, 1f, 0.7f), FontStyle.Normal);
        var srt = sub.GetComponent<RectTransform>();
        srt.anchorMin = new Vector2(0.5f, 0.55f);
        srt.anchorMax = new Vector2(0.5f, 0.55f);
        srt.sizeDelta = new Vector2(900f, 50f);

        // Bottone GIOCA
        var btnGo = new GameObject("PlayButton");
        btnGo.transform.SetParent(panel.transform, false);
        var brt = btnGo.AddComponent<RectTransform>();
        brt.anchorMin = new Vector2(0.5f, 0.35f);
        brt.anchorMax = new Vector2(0.5f, 0.35f);
        brt.sizeDelta = new Vector2(320f, 90f);

        var bImg = btnGo.AddComponent<Image>();
        bImg.color = buttonColor;

        var btn = btnGo.AddComponent<Button>();
        btn.targetGraphic = bImg;
        btn.onClick.AddListener(StartGame);

        var label = CreateText(btnGo.transform, "Label", playLabel, 36, Color.white, FontStyle.Bold);
        var lrt = label.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;

        return panel;
    }

    static GameObject CreateText(Transform parent, string name, string content,
        int size, Color color, FontStyle style)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var txt = go.AddComponent<Text>();
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        txt.text = content;
        txt.fontSize = size;
        txt.fontStyle = style;
        txt.color = color;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.raycastTarget = false;
        return go;
    }
}
