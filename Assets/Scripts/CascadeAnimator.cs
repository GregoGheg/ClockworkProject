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

    List<CascadeColumn> columns = new();
    List<RectTransform> dotPool = new();

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
        var source = grid.level.circuitSource;
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

        // Ricicla o crea pallini
        ReturnAllDots();
        columns = newColumns;

        foreach (var col in columns)
        {
            // Numero di pallini proporzionale alla lunghezza della colonna
            int count = Mathf.Max(1, Mathf.FloorToInt(col.height / dotSpacing));
            for (int i = 0; i < count; i++)
            {
                var dot = GetOrCreateDot();
                col.dots.Add(dot);
            }
            col.offset = 0f;
        }
    }

    void Update()
    {
        if (grid == null || rootCanvas == null) return;
        float size = grid.cellSize;

        foreach (var col in columns)
        {
            // offset avanza in celle/sec assolute — stessa velocità per qualsiasi lunghezza
            // scrollSpeed celle/sec → offset in celle (non normalizzato)
            col.offset += scrollSpeed * Time.deltaTime;
            float totalHeight = col.height * size; // altezza totale in pixel
            if (col.offset * size > totalHeight) col.offset -= col.height;

            for (int i = 0; i < col.dots.Count; i++)
            {
                var dot = col.dots[i];
                if (dot == null) continue;

                // Posizione in celle dal top, distribuita uniformemente
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
                if (img != null)
                    img.color = dotColor;
            }
        }
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