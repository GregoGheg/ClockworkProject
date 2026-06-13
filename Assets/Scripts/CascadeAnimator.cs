using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Anima la cascata idrica con pallini che scorrono verso il basso in loop.
/// Attacca sullo stesso GameObject di CircuitParticleOverlay.
/// </summary>
[RequireComponent(typeof(GridManager))]
public class CascadeAnimator : MonoBehaviour
{
    [Header("Animazione cascata")]
    [Tooltip("Distanza in celle tra un pallino e l'altro")]
    public float dotSpacing = 1.5f; // ogni 1.5 celle un pallino
    [Tooltip("Velocità scorrimento in celle/secondo")]
    public float scrollSpeed = 2f;
    [Tooltip("Dimensione pallino (0-1 rispetto alla cella)")]
    public float dotSize = 0.25f;
    [Tooltip("Colore pallini cascata")]
    public Color dotColor = new Color(0.5f, 0.85f, 1f, 0.9f);

    GridManager grid;
    Canvas rootCanvas;
    RectTransform canvasRect;

    // Ogni colonna di cascata ha una lista di pallini
    class CascadeColumn
    {
        public Vector2Int top;    // cella più in alto della cascata
        public int height; // numero di celle
        public List<RectTransform> dots = new();
        public float offset; // offset verticale corrente (0..1)
    }

    // Colonne getto pompa (direzione arbitraria)
    class JetColumn
    {
        public Vector2Int start;     // prima cella del getto
        public List<Vector2Int> cells = new();
        public List<RectTransform> dots = new();
        public float offset;
        public Vector2Int dir;       // direzione del getto
    }

    List<CascadeColumn> columns = new();
    List<JetColumn> jetColumns = new();
    List<RectTransform> dotPool = new();
    List<RectTransform> linePool = new();
    List<RectTransform> activeLines = new();

    void Awake()
    {
        grid = GetComponent<GridManager>();
        rootCanvas = GetComponentInParent<Canvas>();
        if (rootCanvas != null)
            canvasRect = rootCanvas.GetComponent<RectTransform>();
    }

    void Start()
    {
        grid.OnGridChanged += RefreshColumns;
    }

    void OnDestroy()
    {
        if (grid != null) grid.OnGridChanged -= RefreshColumns;
    }

    void RefreshColumns()
    {
        // Ottieni le celle in cascata dall'HydraulicSolver
        var map = CircuitSolver.BuildConductMap(grid);
        var pumps = CircuitSolver.BuildPumpCells(grid);
        var _srcs = grid.level.GetSources();
        var source = _srcs.Count > 0 ? _srcs[0].position : grid.level.circuitSource;
        var flowMap = HydraulicSolver.GetFlowMap(map, source, grid.Width, grid.Height, grid);

        // Celle di cascata (isCascade=true) + celle laterali di partenza cascata
        // (isCascade=false ma senza tubo in mappa = cella laterale prima della caduta)
        var conductMap = CircuitSolver.BuildConductMap(grid);
        var cascadeCells = new HashSet<Vector2Int>();
        foreach (var kv in flowMap)
        {
            if (kv.Value.isCascade)
            {
                cascadeCells.Add(kv.Key);
            }
            else if (!conductMap.ContainsKey(kv.Key))
            {
                // Cella senza tubo = punto di partenza cascata laterale
                // Includila se ha una cella di cascata sotto di lei
                var below = kv.Key + Vector2Int.down;
                if (flowMap.ContainsKey(below) && flowMap[below].isCascade)
                    cascadeCells.Add(kv.Key);
            }
        }

        // Trova le colonne: celle adiacenti verticalmente
        var visited = new HashSet<Vector2Int>();
        var newColumns = new List<CascadeColumn>();

        foreach (var cell in cascadeCells)
        {
            if (visited.Contains(cell)) continue;
            // Cerca la cima della colonna
            var top = cell;
            while (cascadeCells.Contains(top + Vector2Int.up)) top += Vector2Int.up;

            // Scendi fino al fondo
            var col = new CascadeColumn { top = top };
            var cur = top;
            while (cascadeCells.Contains(cur))
            {
                visited.Add(cur);
                col.height++;
                cur += Vector2Int.down;
            }
            if (col.height > 0) newColumns.Add(col);
        }

        // Getti pompa: celle isCascade=false, non in conductMap, non cascata normale
        var newJets = new List<JetColumn>();
        foreach (var kv in flowMap)
        {
            if (kv.Value.isCascade) continue;
            if (conductMap.ContainsKey(kv.Key)) continue;
            if (cascadeCells.Contains(kv.Key)) continue;
            // Questa è una cella di getto pompa — trova a quale getto appartiene
            // cercando la direzione dal predecessore
            // Raggruppa celle consecutive nella stessa direzione
            // (le aggiungeremo per colonna sotto)
        }

        // Trova i getti: gruppi di celle vuote consecutive di getto pompa
        // Ogni gruppo ha una direzione comune
        var jetVisited = new HashSet<Vector2Int>();
        var pumpJetCells = new HashSet<Vector2Int>();
        foreach (var kv in flowMap)
            if (!kv.Value.isCascade && !conductMap.ContainsKey(kv.Key) && !cascadeCells.Contains(kv.Key))
                pumpJetCells.Add(kv.Key);

        foreach (var startCell in pumpJetCells)
        {
            if (jetVisited.Contains(startCell)) continue;
            // Determina la direzione: cerca il vicino che non è nel jet per trovare da dove viene
            Vector2Int jetDir = Vector2Int.zero;
            foreach (var d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.right, Vector2Int.left })
            {
                var prev = startCell - d;
                if (pumpJetCells.Contains(prev) || conductMap.ContainsKey(prev) || cascadeCells.Contains(prev)) continue;
                // startCell non ha predecessore in direzione d → d è la direzione del getto
                // Verifica che il successore esista nel jet
                if (pumpJetCells.Contains(startCell + d)) { jetDir = d; break; }
            }
            // Fallback: troviamo la direzione guardando dove punta la sequenza
            if (jetDir == Vector2Int.zero)
            {
                foreach (var d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.right, Vector2Int.left })
                    if (pumpJetCells.Contains(startCell + d) && !pumpJetCells.Contains(startCell - d))
                    { jetDir = d; break; }
            }
            if (jetDir == Vector2Int.zero) { jetVisited.Add(startCell); continue; }

            // Risali all'inizio della sequenza
            var head = startCell;
            while (pumpJetCells.Contains(head - jetDir)) head -= jetDir;

            // Costruisci la colonna
            var jet = new JetColumn { start = head, dir = jetDir };
            var cur2 = head;
            while (pumpJetCells.Contains(cur2))
            {
                jet.cells.Add(cur2);
                jetVisited.Add(cur2);
                cur2 += jetDir;
            }
            if (jet.cells.Count > 0) newJets.Add(jet);
        }

        // Ricicla o crea pallini
        ReturnAllDots();
        columns = newColumns;
        jetColumns = newJets;

        foreach (var col in columns)
        {
            int count = Mathf.Max(1, Mathf.FloorToInt(col.height / dotSpacing));
            for (int i = 0; i < count; i++)
            {
                var dot = GetOrCreateDot();
                col.dots.Add(dot);
            }
            col.offset = 0f;
        }

        foreach (var jet in jetColumns)
        {
            int count = Mathf.Max(1, Mathf.FloorToInt(jet.cells.Count / dotSpacing));
            for (int i = 0; i < count; i++)
            {
                var dot = GetOrCreateDot();
                jet.dots.Add(dot);
            }
            jet.offset = 0f;
        }

        // Linee getto: una per ogni segmento tra celle adiacenti del getto
        ReturnAllLines();
        float cellSize = grid.cellSize;
        foreach (var jet in jetColumns)
        {
            for (int i = 0; i < jet.cells.Count - 1; i++)
            {
                var line = GetOrCreateLine();
                PositionLine(line, jet.cells[i], jet.cells[i + 1], cellSize);
                activeLines.Add(line);
            }
        }
    }

    void Update()
    {
        if (grid == null || rootCanvas == null) return;
        float size = grid.cellSize;

        foreach (var col in columns)
        {
            col.offset += scrollSpeed * Time.deltaTime;
            float totalHeight = col.height * size;
            if (col.offset * size > totalHeight) col.offset -= col.height;

            for (int i = 0; i < col.dots.Count; i++)
            {
                var dot = col.dots[i];
                if (dot == null) continue;

                float cellPos = (col.offset + (float)i * col.height / col.dots.Count) % col.height;
                if (cellPos < 0) cellPos += col.height;

                int cellIdx = Mathf.FloorToInt(cellPos);
                float frac = cellPos - cellIdx;
                cellIdx = Mathf.Clamp(cellIdx, 0, col.height - 1);

                var cellCoord = col.top + Vector2Int.down * cellIdx;
                var worldPos = CellToCanvasPos(cellCoord, size);
                worldPos.y -= frac * size;

                float radius = size * dotSize;
                dot.sizeDelta = Vector2.one * radius * 2f;
                dot.anchoredPosition = new Vector2(
                    worldPos.x + size * 0.5f - radius,
                    worldPos.y + size * 0.5f - radius);
                dot.gameObject.SetActive(true);

                var img = dot.GetComponent<Image>();
                if (img != null) img.color = dotColor;
            }
        }

        // Animazione getti pompa
        foreach (var jet in jetColumns)
        {
            if (jet.cells.Count == 0) continue;
            jet.offset += scrollSpeed * Time.deltaTime;
            if (jet.offset >= jet.cells.Count) jet.offset -= jet.cells.Count;

            for (int i = 0; i < jet.dots.Count; i++)
            {
                var dot = jet.dots[i];
                if (dot == null) continue;

                float cellPos = (jet.offset + (float)i * jet.cells.Count / jet.dots.Count) % jet.cells.Count;
                if (cellPos < 0) cellPos += jet.cells.Count;

                int cellIdx = Mathf.FloorToInt(cellPos);
                float frac = cellPos - cellIdx;
                cellIdx = Mathf.Clamp(cellIdx, 0, jet.cells.Count - 1);

                var cellCoord = jet.cells[cellIdx];
                var worldPos = CellToCanvasPos(cellCoord, size);
                // Offset sub-cella nella direzione del getto
                worldPos.x += frac * size * jet.dir.x;
                worldPos.y += frac * size * jet.dir.y;

                float radius = size * dotSize;
                dot.sizeDelta = Vector2.one * radius * 2f;
                dot.anchoredPosition = new Vector2(
                    worldPos.x + size * 0.5f - radius,
                    worldPos.y + size * 0.5f - radius);
                dot.gameObject.SetActive(true);

                var img = dot.GetComponent<Image>();
                if (img != null) img.color = dotColor;
            }
        }
    }

    void PositionLine(RectTransform rt, Vector2Int from, Vector2Int to, float size)
    {
        float half = size * 0.5f;
        float thickness = size * 0.06f;
        Vector2 fromCenter = new Vector2(from.x * size + half, from.y * size + half);
        Vector2 toCenter = new Vector2(to.x * size + half, to.y * size + half);
        Vector2 dir = (toCenter - fromCenter).normalized;
        float length = Vector2.Distance(fromCenter, toCenter);
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        rt.anchoredPosition = fromCenter;
        rt.sizeDelta = new Vector2(length, thickness);
        rt.localEulerAngles = new Vector3(0, 0, angle);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.gameObject.SetActive(true);
        var img = rt.GetComponent<Image>();
        if (img != null) img.color = new Color(dotColor.r, dotColor.g, dotColor.b, 0.5f);
    }

    RectTransform GetOrCreateLine()
    {
        for (int i = 0; i < linePool.Count; i++)
        {
            if (linePool[i] != null && !linePool[i].gameObject.activeSelf)
            {
                var l = linePool[i];
                linePool.RemoveAt(i);
                return l;
            }
        }
        var go = new GameObject("JetLine");
        go.transform.SetParent(transform, false);
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        img.color = new Color(dotColor.r, dotColor.g, dotColor.b, 0.5f);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        return rt;
    }

    void ReturnAllLines()
    {
        foreach (var l in activeLines)
            if (l != null) { l.gameObject.SetActive(false); linePool.Add(l); }
        activeLines.Clear();
    }

    Vector2 CellToCanvasPos(Vector2Int cell, float size)
    {
        // Stessa logica di CircuitParticleOverlay
        return new Vector2(cell.x * size, cell.y * size);
    }

    void ReturnAllDots()
    {
        foreach (var col in columns)
            foreach (var dot in col.dots)
                if (dot != null) { dot.gameObject.SetActive(false); dotPool.Add(dot); }
        columns.Clear();
        foreach (var jet in jetColumns)
            foreach (var dot in jet.dots)
                if (dot != null) { dot.gameObject.SetActive(false); dotPool.Add(dot); }
        jetColumns.Clear();
        ReturnAllLines();
    }

    RectTransform GetOrCreateDot()
    {
        for (int i = 0; i < dotPool.Count; i++)
        {
            if (dotPool[i] != null && !dotPool[i].gameObject.activeSelf)
            {
                var d = dotPool[i];
                dotPool.RemoveAt(i);
                return d;
            }
        }
        return CreateDot();
    }

    RectTransform CreateDot()
    {
        var go = new GameObject("CascadeDot");
        go.transform.SetParent(transform, false);
        var img = go.AddComponent<Image>();
        img.sprite = CreateCircleSprite();
        img.color = dotColor;
        img.raycastTarget = false;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        return rt;
    }

    Sprite CreateCircleSprite()
    {
        int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float r = size / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(r, r));
                tex.SetPixel(x, y, d < r ? Color.white : Color.clear);
            }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }
}