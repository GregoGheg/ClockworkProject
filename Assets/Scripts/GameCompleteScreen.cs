using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Schermata di vittoria finale: appare quando TUTTI i livelli del
/// WorldLevelConfig sono stati risolti.
///
/// COME USARLA:
/// 1. Attacca questo script sul GameObject "Canvas" (o su un GameObject UI qualsiasi
///    figlio del Canvas).
/// 2. Assegna in "Navigator" il WorldNavigator della scena (se lo lasci vuoto
///    viene cercato automaticamente).
/// 3. Opzionale: assegna un tuo pannello in "Victory Panel". Se vuoto, viene
///    costruito automaticamente (sfondo + titolo + bottone RIGIOCA).
///
/// FUNZIONAMENTO: ogni mezzo secondo controlla WorldNavigator.IsSolved(i) per
/// tutti i livelli del config (il flag viene impostato da GameManager →
/// onLevelSolved → WorldNavigator.OnLevelSolved, quindi resta vero anche se il
/// giocatore poi smonta il circuito). Quando sono tutti risolti mostra il
/// pannello una sola volta.
/// </summary>
public class GameCompleteScreen : MonoBehaviour
{
    [Header("Riferimenti")]
    public WorldNavigator navigator;

    [Tooltip("Pannello di vittoria personalizzato. Vuoto = generato automaticamente.")]
    public GameObject victoryPanel;

    [Header("Testi (per il pannello autogenerato)")]
    public string titleText = "HAI VINTO!";
    public string subtitleText = "Hai completato tutti i livelli disponibili";
    public string restartLabel = "RIGIOCA";

    [Header("Opzioni")]
    [Tooltip("Mostra il bottone che ricarica la scena da capo")]
    public bool showRestartButton = true;

    [Tooltip("Ogni quanti secondi controllare lo stato dei livelli")]
    public float checkInterval = 0.5f;

    [Header("Stile (per il pannello autogenerato)")]
    public Color backgroundColor = new Color(0f, 0f, 0f, 0.88f);
    public Color titleColor = new Color(1f, 0.85f, 0.3f, 1f);
    public Color buttonColor = new Color(0.2f, 0.65f, 0.3f, 1f);

    Canvas rootCanvas;
    bool shown = false;
    float timer = 0f;

    void Awake()
    {
        rootCanvas = GetComponent<Canvas>();
        if (rootCanvas == null) rootCanvas = GetComponentInParent<Canvas>();
        if (rootCanvas == null) rootCanvas = FindFirstObjectByType<Canvas>();
    }

    void Update()
    {
        if (shown) return;

        if (navigator == null)
        {
            navigator = FindFirstObjectByType<WorldNavigator>();
            if (navigator == null) return;
        }
        if (navigator.config == null || navigator.config.levels == null) return;

        timer += Time.unscaledDeltaTime;
        if (timer < checkInterval) return;
        timer = 0f;

        if (AllLevelsSolved()) Show();
    }

    bool AllLevelsSolved()
    {
        var levels = navigator.config.levels;
        if (levels.Length == 0) return false;

        for (int i = 0; i < levels.Length; i++)
            if (!navigator.IsSolved(i)) return false;

        return true;
    }

    public void Show()
    {
        shown = true;
        if (victoryPanel == null) victoryPanel = BuildPanel();
        victoryPanel.SetActive(true);
        victoryPanel.transform.SetAsLastSibling(); // sopra a tutto
    }

    public void Restart()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // ── Costruzione UI automatica ─────────────────────────────────────────
    GameObject BuildPanel()
    {
        var panel = new GameObject("VictoryPanel");
        panel.transform.SetParent(rootCanvas.transform, false);

        var prt = panel.AddComponent<RectTransform>();
        prt.anchorMin = Vector2.zero;
        prt.anchorMax = Vector2.one;
        prt.offsetMin = prt.offsetMax = Vector2.zero;

        // Sfondo scuro che blocca i click sul gioco
        var bg = panel.AddComponent<Image>();
        bg.color = backgroundColor;
        bg.raycastTarget = true;

        // Titolo
        var title = CreateText(panel.transform, "Title", titleText, 80, titleColor, FontStyle.Bold);
        var trt = title.GetComponent<RectTransform>();
        trt.anchorMin = new Vector2(0.5f, 0.62f);
        trt.anchorMax = new Vector2(0.5f, 0.62f);
        trt.sizeDelta = new Vector2(1000f, 130f);

        // Sottotitolo
        var sub = CreateText(panel.transform, "Subtitle", subtitleText, 30,
            new Color(1f, 1f, 1f, 0.85f), FontStyle.Normal);
        var srt = sub.GetComponent<RectTransform>();
        srt.anchorMin = new Vector2(0.5f, 0.5f);
        srt.anchorMax = new Vector2(0.5f, 0.5f);
        srt.sizeDelta = new Vector2(1000f, 60f);

        // Bottone RIGIOCA
        if (showRestartButton)
        {
            var btnGo = new GameObject("RestartButton");
            btnGo.transform.SetParent(panel.transform, false);
            var brt = btnGo.AddComponent<RectTransform>();
            brt.anchorMin = new Vector2(0.5f, 0.33f);
            brt.anchorMax = new Vector2(0.5f, 0.33f);
            brt.sizeDelta = new Vector2(320f, 90f);

            var bImg = btnGo.AddComponent<Image>();
            bImg.color = buttonColor;

            var btn = btnGo.AddComponent<Button>();
            btn.targetGraphic = bImg;
            btn.onClick.AddListener(Restart);

            var label = CreateText(btnGo.transform, "Label", restartLabel, 34, Color.white, FontStyle.Bold);
            var lrt = label.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        }

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
