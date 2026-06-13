using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attacca su un GameObject figlio del GridContainer (o parallelo).
/// Crea una griglia overlay trasparente con:
///   - Un pallino animato su ogni cella che ha energia
///   - Una linea animata verso la cella successiva nel flusso
/// Si aggiorna ad ogni OnGridChanged tramite CircuitVisualizer.
/// </summary>
public class CircuitParticleOverlay : MonoBehaviour
{
    [Header("Colori per tipo energia")]
    public Color colorMechanical = new Color(0.45f, 0.25f, 0.05f, 1f);
    public Color colorMechanicalLine = new Color(0.45f, 0.25f, 0.05f, 0.6f);
    public Color colorMechanicalPulse = new Color(0.7f, 0.45f, 0.1f, 1f);

    public Color colorHydraulic = new Color(0.15f, 0.5f, 1f, 1f);
    public Color colorHydraulicLine = new Color(0.15f, 0.5f, 1f, 0.6f);
    public Color colorHydraulicPulse = new Color(0.5f, 0.85f, 1f, 1f);

    public Color colorElectric = new Color(1f, 0.95f, 0.1f, 1f);
    public Color colorElectricLine = new Color(1f, 0.95f, 0.1f, 0.6f);
    public Color colorElectricPulse = new Color(1f, 1f, 0.6f, 1f);

    // Retrocompatibilità (usati da GetOrCreateDot/Line)
    Color dotColor = Color.white;
    Color lineColor = Color.white;
    Color pulseColor = Color.white;

    [Header("Dimensioni")]
    [Range(0.05f, 0.4f)] public float dotRadius = 0.15f;   // frazione di cellSize
    [Range(0.02f, 0.2f)] public float lineThickness = 0.06f;  // frazione di cellSize
    [Range(0.5f, 3f)] public float pulseSpeed = 1.5f;

    GridManager grid;

    // Pool di elementi visivi
    readonly List<RectTransform> dots = new();
    readonly List<RectTransform> lines = new();
    int dotIdx, lineIdx;

    // Connessioni attive (da → verso)
    readonly List<(Vector2Int from, Vector2Int to)> activeConnections = new();

    // Direzioni per il BFS
    static readonly (Vector2Int dir, PieceData.ConnectionSides outS, PieceData.ConnectionSides inS)[] Dirs =
    {
        (Vector2Int.right, PieceData.ConnectionSides.Right, PieceData.ConnectionSides.Left),
        (Vector2Int.left,  PieceData.ConnectionSides.Left,  PieceData.ConnectionSides.Right),
        (Vector2Int.up,    PieceData.ConnectionSides.Up,    PieceData.ConnectionSides.Down),
        (Vector2Int.down,  PieceData.ConnectionSides.Down,  PieceData.ConnectionSides.Up),
    };

    void Awake()
    {
        // Cerca il GridManager nella stessa gerarchia del livello (fratello nel prefab)
        // Risale fino a trovare un Transform che contenga un GridManager come figlio
        grid = GetComponentInParent<GridManager>(true);
        if (grid == null)
        {
            var t = transform.parent;
            while (t != null && grid == null)
            {
                grid = t.GetComponentInChildren<GridManager>(true);
                t = t.parent;
            }
        }

        // Forza il RectTransform già in Awake — prima che Unity usi i valori Inspector
        var rt = GetComponent<RectTransform>();
        if (rt == null) rt = gameObject.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    void Start()
    {
        AttachToGrid();
        grid.OnGridChanged += Refresh;
        grid.OnGridChanged += BringToFront;
    }

    /// <summary>
    /// Reparenta l'overlay al RectTransform della griglia e lo fa
    /// coprire esattamente tutta la griglia con stretch 0→1.
    /// Viene chiamato anche da GridManager.ApplyLayout() dopo ogni
    /// riposizionamento della griglia.
    /// </summary>
    public void AttachToGrid()
    {
        if (grid == null) return;

        // Reparenta al GridManager se non è già suo figlio diretto
        if (transform.parent != grid.transform)
            transform.SetParent(grid.transform, false);

        var rt = GetComponent<RectTransform>();
        if (rt == null) rt = gameObject.AddComponent<RectTransform>();

        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        transform.SetAsLastSibling();
    }

    void OnDestroy()
    {
        if (grid != null)
        {
            grid.OnGridChanged -= Refresh;
            grid.OnGridChanged -= BringToFront;
        }
    }

    void BringToFront() => transform.SetAsLastSibling();

    [Tooltip("Colore elettrico ad instabilità massima")]
    public Color colorElectricMax = new Color(1f, 0.15f, 0.05f, 1f);

    (Color dot, Color pulse) ColorForType(EnergyType type) => type switch
    {
        EnergyType.Mechanical => (colorMechanical, colorMechanicalPulse),
        EnergyType.Hydraulic => (colorHydraulic, colorHydraulicPulse),
        EnergyType.Electric => (colorElectric, colorElectricPulse),
        _ => (Color.white, Color.white)
    };

    (Color line, Color pulse) LineColorForType(EnergyType type) => type switch
    {
        EnergyType.Mechanical => (colorMechanicalLine, colorMechanicalPulse),
        EnergyType.Hydraulic => (colorHydraulicLine, colorHydraulicPulse),
        EnergyType.Electric => (colorElectricLine, colorElectricPulse),
        _ => (Color.white, Color.white)
    };

    Color ElectricColorForInstability(float instab, float alpha = 1f)
    {
        float t = Mathf.Clamp01(instab / 10f);
        Color c = t < 0.5f
            ? Color.Lerp(colorElectric, new Color(1f, 0.5f, 0.05f, 1f), t * 2f)
            : Color.Lerp(new Color(1f, 0.5f, 0.05f, 1f), colorElectricMax, (t - 0.5f) * 2f);
        c.a = alpha;
        return c;
    }

    // ── Refresh ───────────────────────────────────────────────────────────
    public void Refresh()
    {
        activeConnections.Clear();
        dotIdx = 0;
        lineIdx = 0;

        var srcs = grid.level.GetSources();
        var source = srcs.Count > 0 ? srcs[0].position : grid.level.circuitSource;

        // Celle e link per ogni tipo
        var mechReached = CircuitSolver.GetReachedCells(grid, source, EnergyType.Mechanical);
        var elecInstab = CircuitSolver.GetElectricInstability(grid, source);
        var hydrReached = CircuitSolver.GetReachedCells(grid, source, EnergyType.Hydraulic);
        var genericFlow = CircuitSolver.GetGenericFlow(grid, source);
        var collectorFlow = CircuitSolver.GetCollectorFlow(grid, source);

        var mechLinks = CircuitSolver.GetEnergyLinks(grid, source, EnergyType.Mechanical);
        var elecLinks = CircuitSolver.GetEnergyLinks(grid, source, EnergyType.Electric);
        var hydrLinks = CircuitSolver.GetEnergyLinks(grid, source, EnergyType.Hydraulic);

        var typedFlow = TypedConverterSolver.GetFlow(grid, source);

        var allReached = new HashSet<Vector2Int>(mechReached);
        foreach (var k in elecInstab.Keys) allReached.Add(k);
        allReached.UnionWith(hydrReached);
        foreach (var k in genericFlow.Keys) allReached.Add(k);
        foreach (var k in typedFlow.Keys) allReached.Add(k);

        if (allReached.Count == 0) { HideUnused(); return; }

        // Dot meccanici
        foreach (var coord in mechReached)
        {
            dotColor = colorMechanical;
            pulseColor = colorMechanicalPulse;
            GetOrCreateDot(coord);
        }
        // Dot elettrici
        foreach (var kv in elecInstab)
        {
            if (mechReached.Contains(kv.Key)) continue;
            dotColor = ElectricColorForInstability(kv.Value);
            pulseColor = ElectricColorForInstability(kv.Value, 0.6f);
            GetOrCreateDot(kv.Key);
        }
        // Dot idrici — solo celle con tubo (getto e cascata li gestisce CascadeAnimator)
        var hydrMap = CircuitSolver.BuildConductMap(grid);
        foreach (var coord in hydrReached)
        {
            if (mechReached.Contains(coord) || elecInstab.ContainsKey(coord)) continue;
            if (!hydrMap.ContainsKey(coord)) continue;
            dotColor = colorHydraulic;
            pulseColor = colorHydraulicPulse;
            GetOrCreateDot(coord);
        }
        // Dot collettore — colore elettrico con instabilità 9
        foreach (var kv in collectorFlow)
        {
            if (mechReached.Contains(kv.Key) || elecInstab.ContainsKey(kv.Key)) continue;
            if (hydrReached.Contains(kv.Key)) continue;
            dotColor = ElectricColorForInstability(kv.Value);
            pulseColor = ElectricColorForInstability(kv.Value, 0.6f);
            GetOrCreateDot(kv.Key);
        }

        // Linee collettore
        var collectorVisited = new HashSet<(Vector2Int, Vector2Int)>();
        foreach (var kv in collectorFlow)
        {
            foreach (var dir in new[] { Vector2Int.right, Vector2Int.up })
            {
                var neighbor = kv.Key + dir;
                if (!collectorFlow.ContainsKey(neighbor)) continue;
                var pair = (kv.Key, neighbor);
                if (collectorVisited.Contains(pair)) continue;
                collectorVisited.Add(pair);
                float avg = (kv.Value + collectorFlow[neighbor]) * 0.5f;
                lineColor = ElectricColorForInstability(avg, 0.7f);
                pulseColor = ElectricColorForInstability(avg, 0.5f);
                GetOrCreateLine(kv.Key, neighbor);
            }
        }

        // Dot convertitori tipizzati — celle non già coperte dagli altri flussi
        foreach (var kv in typedFlow)
        {
            if (mechReached.Contains(kv.Key) || elecInstab.ContainsKey(kv.Key)
                || hydrReached.Contains(kv.Key) || genericFlow.ContainsKey(kv.Key)
                || collectorFlow.ContainsKey(kv.Key)) continue;
            (dotColor, pulseColor) = ColorForType(kv.Value);
            GetOrCreateDot(kv.Key);
        }

        // Celle raggiunte via convertitore elettrico
        // Usa GetGenericElecInstability per avere l'instabilità reale per cella
        var genericElecInstab = CircuitSolver.GetGenericElecInstability(grid, source);

        // Dot generici — colore basato sul tipo adottato
        foreach (var kv in genericFlow)
        {
            if (mechReached.Contains(kv.Key) || elecInstab.ContainsKey(kv.Key) || hydrReached.Contains(kv.Key)) continue;
            if (kv.Value == EnergyType.Electric)
            {
                // Se la cella non è nel dizionario instabilità è oltre soglia — salta
                if (!genericElecInstab.ContainsKey(kv.Key)) continue;
                float instab = genericElecInstab[kv.Key];
                if (instab >= 10f) continue;
                dotColor = ElectricColorForInstability(instab);
                pulseColor = ElectricColorForInstability(instab, 0.6f);
            }
            else
            {
                (dotColor, pulseColor) = ColorForType(kv.Value);
            }
            GetOrCreateDot(kv.Key);
        }

        // Linee meccaniche
        foreach (var (from, to) in mechLinks)
        {
            lineColor = colorMechanicalLine;
            pulseColor = colorMechanicalPulse;
            GetOrCreateLine(from, to);
        }
        // Linee elettriche
        foreach (var (from, to) in elecLinks)
        {
            float instabFrom = elecInstab.ContainsKey(from) ? elecInstab[from] : 0f;
            float instabTo = elecInstab.ContainsKey(to) ? elecInstab[to] : 0f;
            float avg = (instabFrom + instabTo) * 0.5f;
            lineColor = ElectricColorForInstability(avg, 0.7f);
            pulseColor = ElectricColorForInstability(avg, 0.5f);
            GetOrCreateLine(from, to);
        }
        // Linee idriche — tutte incluse cascata
        foreach (var (from, to) in hydrLinks)
        {
            lineColor = colorHydraulicLine;
            pulseColor = colorHydraulicPulse;
            GetOrCreateLine(from, to);
        }
        // Linee generiche — collega celle adiacenti nel genericFlow con colore del tipo adottato
        // Salta celle elettriche con instabilità >= 10
        var genericVisited = new HashSet<(Vector2Int, Vector2Int)>();
        foreach (var kv in genericFlow)
        {
            var coord = kv.Key;
            // Salta questa cella se è elettrica oltre soglia
            if (kv.Value == EnergyType.Electric &&
                (!genericElecInstab.ContainsKey(coord) || genericElecInstab[coord] >= 10f)) continue;

            foreach (var dir in new[] { Vector2Int.right, Vector2Int.up })
            {
                var neighbor = coord + dir;
                if (!genericFlow.ContainsKey(neighbor)) continue;
                // Salta il neighbor se è elettrico oltre soglia
                var neighborType = genericFlow[neighbor];
                if (neighborType == EnergyType.Electric &&
                    (!genericElecInstab.ContainsKey(neighbor) || genericElecInstab[neighbor] >= 10f)) continue;

                var pair = (coord, neighbor);
                if (genericVisited.Contains(pair)) continue;
                genericVisited.Add(pair);
                var adoptedType = kv.Value != EnergyType.Generic ? kv.Value : neighborType;
                (lineColor, pulseColor) = LineColorForType(adoptedType);
                GetOrCreateLine(coord, neighbor);
            }
        }

        HideUnused();
    }

    // ── Dot ───────────────────────────────────────────────────────────────
    void GetOrCreateDot(Vector2Int coord)
    {
        RectTransform rt;
        if (dotIdx < dots.Count)
        {
            rt = dots[dotIdx];
            rt.gameObject.SetActive(true);
        }
        else
        {
            rt = CreateDot();
            dots.Add(rt);
        }
        dotIdx++;

        // Aggiorna SEMPRE il colore — anche quando riutilizza dal pool
        var img = rt.GetComponent<Image>();
        if (img != null) img.color = dotColor;
        var pulse = rt.GetComponent<PulseBehaviour>();
        if (pulse != null) { pulse.baseColor = dotColor; pulse.pulseColor = pulseColor; }

        float size = grid.cellSize;
        float radius = size * dotRadius;
        rt.sizeDelta = Vector2.one * radius * 2f;
        rt.anchoredPosition = new Vector2(
            coord.x * size + size * 0.5f - radius,
            coord.y * size + size * 0.5f - radius);
    }

    RectTransform CreateDot()
    {
        var go = new GameObject("Dot");
        go.transform.SetParent(transform, false);

        var img = go.AddComponent<Image>();
        img.color = dotColor;
        img.raycastTarget = false;
        // Cerchio — usa un sprite circolare se disponibile, altrimenti quadrato
        img.sprite = CreateCircleSprite();

        var pulse = go.AddComponent<PulseBehaviour>();
        pulse.baseColor = dotColor;
        pulse.pulseColor = pulseColor;
        pulse.speed = pulseSpeed;

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        return rt;
    }

    // ── Line ──────────────────────────────────────────────────────────────
    void GetOrCreateLine(Vector2Int from, Vector2Int to)
    {
        // Aggiorna colore dopo aver ottenuto/creato
        RectTransform rt;
        if (lineIdx < lines.Count)
        {
            rt = lines[lineIdx];
            rt.gameObject.SetActive(true);
        }
        else
        {
            rt = CreateLine();
            lines.Add(rt);
        }
        lineIdx++;

        // Aggiorna SEMPRE il colore della linea
        var lineImg = rt.GetComponent<Image>();
        if (lineImg != null) lineImg.color = lineColor;
        foreach (var flow in rt.GetComponentsInChildren<FlowBehaviour>())
            flow.color = lineColor;

        float size = grid.cellSize;
        float thickness = size * lineThickness;
        float half = size * 0.5f;

        Vector2 fromCenter = new Vector2(from.x * size + half, from.y * size + half);
        Vector2 toCenter = new Vector2(to.x * size + half, to.y * size + half);

        Vector2 dir = (toCenter - fromCenter).normalized;
        float length = Vector2.Distance(fromCenter, toCenter);
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        rt.anchoredPosition = fromCenter;
        rt.sizeDelta = new Vector2(length, thickness);
        rt.localEulerAngles = new Vector3(0, 0, angle);
        rt.pivot = new Vector2(0f, 0.5f);
    }

    RectTransform CreateLine()
    {
        var go = new GameObject("Line");
        go.transform.SetParent(transform, false);

        var img = go.AddComponent<Image>();
        img.color = lineColor;
        img.raycastTarget = false;

        var flow = go.AddComponent<FlowBehaviour>();
        flow.color = lineColor;
        flow.speed = pulseSpeed;

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        return rt;
    }

    void HideUnused()
    {
        for (int i = dotIdx; i < dots.Count; i++) dots[i].gameObject.SetActive(false);
        for (int i = lineIdx; i < lines.Count; i++) lines[i].gameObject.SetActive(false);
    }

    // ── Sprite cerchio procedurale ─────────────────────────────────────────
    static Sprite cachedCircle;
    static Sprite CreateCircleSprite()
    {
        if (cachedCircle != null) return cachedCircle;
        int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float r = size * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x - r + 0.5f;
                float dy = y - r + 0.5f;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01(1f - (dist - (r - 1.5f)) / 1.5f);
                tex.SetPixel(x, y, new Color(1, 1, 1, alpha));
            }
        tex.Apply();
        cachedCircle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        return cachedCircle;
    }
}

// ── Comportamenti animati ──────────────────────────────────────────────────

/// Pulsa il colore del dot tra baseColor e pulseColor
public class PulseBehaviour : MonoBehaviour
{
    public Color baseColor;
    public Color pulseColor;
    public float speed = 1.5f;
    Image img;
    float offset;

    void Awake()
    {
        img = GetComponent<Image>();
        offset = Random.Range(0f, Mathf.PI * 2f);
    }

    void Update()
    {
        if (img == null) return;
        float t = (Mathf.Sin(Time.time * speed * Mathf.PI * 2f + offset) + 1f) * 0.5f;
        img.color = Color.Lerp(baseColor, pulseColor, t);
    }
}

/// Anima l'opacità della linea come onda che scorre
public class FlowBehaviour : MonoBehaviour
{
    public Color color;
    public float speed = 1.5f;
    Image img;
    float offset;

    void Awake()
    {
        img = GetComponent<Image>();
        offset = Random.Range(0f, 1f);
    }

    void Update()
    {
        if (img == null) return;
        float t = (Time.time * speed + offset) % 1f;
        float alpha = Mathf.Clamp01(Mathf.Sin(t * Mathf.PI));
        img.color = new Color(color.r, color.g, color.b, alpha * color.a);
    }
}