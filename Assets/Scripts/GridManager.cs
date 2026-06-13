using System.Collections.Generic;
using UnityEngine;

public class GridManager : MonoBehaviour
{
    // Stato di una singola cella logica
    public class CellState
    {
        public Piece occupant;   // pezzo che occupa fisicamente questa cella
        public bool isActive;    // cella visibile e interagibile
        public bool isSourceOrDest; // blocco sorgente/destinazione: non piazzabile
        // conductIn/Out rimossi — calcolati fresh da CircuitSolver.BuildConductMap
    }

    [Header("Setup")]
    public LevelData level;
    public GridCell cellPrefab;
    [HideInInspector] public float cellSize = 80f; // impostato da LevelViewController via WorldLevelConfig

    CellState[,] grid;
    GridCell[,] cellViews;

    public int Width => level.gridWidth;
    public int Height => level.gridHeight;

    public System.Action OnGridChanged;
    /// <summary>Invocato quando un pezzo viene droppato su una cella occupata. Parametri: pezzo droppato, cella target, posizione precedente del pezzo.</summary>
    public System.Action<Piece, Vector2Int, Vector2Int> OnDropOnOccupied;
    /// <summary>Registra la posizione di un pezzo prima che venga rimosso per il drag.</summary>
    readonly System.Collections.Generic.Dictionary<Piece, Vector2Int> _preDragPos = new();
    readonly System.Collections.Generic.HashSet<Piece> _swapHandled = new();
    public void RegisterPreDragPosition(Piece piece, Vector2Int pos) => _preDragPos[piece] = pos;
    public Vector2Int GetPreDragPosition(Piece piece) => _preDragPos.TryGetValue(piece, out var p) ? p : new Vector2Int(-1, -1);

    readonly List<Piece> placedPieces = new();
    public IReadOnlyList<Piece> PlacedPieces => placedPieces;

    /// <summary>
    /// Chiamato da LevelViewController dopo aver impostato cellSize e level.
    /// Usa level.gridOffset (configurabile nel LevelData) per posizionare
    /// la griglia. Il CircuitParticleOverlay è figlio con stretch 0→1
    /// quindi si muove automaticamente insieme.
    /// </summary>
    public void ApplyLayout()
    {
        var rt = GetComponent<RectTransform>();
        rt.pivot = Vector2.zero;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(Width * cellSize, Height * cellSize);

        // gridOffset=0,0 → griglia centrata; valori positivi spostano destra/su
        rt.anchoredPosition = new Vector2(
            -Width * cellSize * 0.5f + (level != null ? level.gridOffset.x : 0f),
            -Height * cellSize * 0.5f + (level != null ? level.gridOffset.y : 0f));

        // Aggancia il CircuitParticleOverlay alla griglia dopo ogni riposizionamento
        var overlay = GetComponentInChildren<CircuitParticleOverlay>(true);
        if (overlay == null)
        {
            // Cerca anche tra i fratelli (caso in cui l'overlay non sia figlio della griglia)
            var parent = transform.parent;
            if (parent != null)
                overlay = parent.GetComponentInChildren<CircuitParticleOverlay>(true);
        }
        overlay?.AttachToGrid();
    }

    void Awake()
    {
        var rt = GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.sizeDelta = new Vector2(Width * cellSize, Height * cellSize);

        // ── Centratura automatica ─────────────────────────────────────────
        // Ancora la griglia al centro del parent e la sposta indietro di metà
        // della propria dimensione (il pivot resta (0,0) per non rompere le
        // conversioni screen→cella). Così qualsiasi griglia (4x4, 12x12...)
        // risulta centrata invece di restare ancorata in basso a sinistra.
        grid = new CellState[Width, Height];
        cellViews = new GridCell[Width, Height];

        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
            {
                bool active = level.IsCellActive(new Vector2Int(x, y));
                grid[x, y] = new CellState { isActive = active };

                // Istanzia la cella visiva solo se attiva
                var view = Instantiate(cellPrefab, transform);
                view.Init(new Vector2Int(x, y), cellSize, this, active);
                cellViews[x, y] = view;
            }

        // Marca sorgenti e destinazioni (multi). Sono blocchi conduttori:
        // occupano spazio, quindi non ci si può piazzare sopra un pezzo.
        foreach (var s in level.GetSources())
            if (IsInBounds(s.position))
            {
                cellViews[s.position.x, s.position.y].SetAsSource();
                grid[s.position.x, s.position.y].isSourceOrDest = true;
            }
        foreach (var d in level.GetDestinations())
            if (IsInBounds(d.position))
            {
                cellViews[d.position.x, d.position.y].SetAsDestination();
                grid[d.position.x, d.position.y].isSourceOrDest = true;
            }
    }

    // ── Bounds & query ────────────────────────────────────────────────────
    public bool IsInBounds(int x, int y) =>
        x >= 0 && x < Width && y >= 0 && y < Height;

    public bool IsInBounds(Vector2Int v) => IsInBounds(v.x, v.y);

    public CellState GetCell(int x, int y) =>
        IsInBounds(x, y) ? grid[x, y] : null;

    public CellState GetCell(Vector2Int v) => GetCell(v.x, v.y);

    // Una cella è libera se è attiva, in bounds, non occupata e non è source/dest
    public bool IsFree(int x, int y)
    {
        if (!IsInBounds(x, y)) return false;
        var c = grid[x, y];
        return c.isActive && c.occupant == null && !c.isSourceOrDest;
    }

    // ── Placement ─────────────────────────────────────────────────────────
    public bool CanPlace(Piece piece, Vector2Int at)
    {
        var test = piece.Clone();
        test.gridPosition = at;
        foreach (var cell in test.WorldCells())
        {
            var coord = cell.localCoord;
            if (!cell.occupiesSpace) continue;
            if (!IsFree(coord.x, coord.y)) return false;
        }
        return true;
    }

    public bool TryPlace(Piece piece, Vector2Int at)
    {
        // Se il pezzo è già piazzato qui (dallo swap), simula successo
        if (piece.gridPosition == at && GetCell(at)?.occupant == piece)
            return true;
        if (!CanPlace(piece, at))
        {
            // Notifica se la cella è occupata (per lo swap)
            var cell = GetCell(at);
            if (cell?.occupant != null && cell.occupant != piece)
            {
                // Usa la posizione pre-drag se disponibile (più accurata)
                var prevPos = _preDragPos.TryGetValue(piece, out var p) ? p : piece.gridPosition;
                // Invoca solo se non già gestito (evita doppio trigger dal fallback di PieceDragger)
                if (!_swapHandled.Contains(piece))
                {
                    _swapHandled.Add(piece);
                    OnDropOnOccupied?.Invoke(piece, at, prevPos);

                }
            }
            return false;
        }
        piece.gridPosition = at;

        foreach (var cell in piece.WorldCells())
        {
            var coord = cell.localCoord;

            if (!IsInBounds(coord)) continue;

            if (cell.occupiesSpace)
            {
                grid[coord.x, coord.y].occupant = piece;
                cellViews[coord.x, coord.y].SetOccupied(piece.data.color, cell, piece.data);
            }
            else
            {
                // Cella non-fisica: solo visual, conduttività gestita da CircuitSolver
                cellViews[coord.x, coord.y].SetNonPhysical(cell, piece.data);
            }
        }

        if (!placedPieces.Contains(piece)) placedPieces.Add(piece);
        _swapHandled.Remove(piece); // reset flag swap
        OnGridChanged?.Invoke();
        return true;
    }

    public void Remove(Piece piece)
    {
        if (piece.gridPosition.x < 0) return;
        // Salva posizione pre-rimozione per lo swap
        _preDragPos[piece] = piece.gridPosition;

        foreach (var cell in piece.WorldCells())
        {
            var coord = cell.localCoord;
            if (!IsInBounds(coord)) continue;

            var state = grid[coord.x, coord.y];
            if (cell.occupiesSpace) state.occupant = null;
            // Visual: pulisci solo se la cella è fisicamente libera
            if (state.occupant == null)
                cellViews[coord.x, coord.y].SetEmpty();
            else
                cellViews[coord.x, coord.y].SetOccupied(state.occupant.data.color, default, state.occupant.data);
        }

        placedPieces.Remove(piece);
        piece.gridPosition = new Vector2Int(-1, -1);
        OnGridChanged?.Invoke();
    }

    // RebuildConductivity rimossa — CircuitSolver calcola tutto fresh da PlacedPieces

    // ── Preview ───────────────────────────────────────────────────────────
    public void ShowPreview(Piece piece, Vector2Int at)
    {
        ClearPreview();
        var test = piece.Clone();
        test.gridPosition = at;
        bool valid = CanPlace(piece, at);

        foreach (var cell in test.WorldCells())
        {
            var coord = cell.localCoord;
            if (IsInBounds(coord))
                cellViews[coord.x, coord.y]?.SetPreview(valid);
        }
    }

    public void ClearPreview()
    {
        foreach (var view in cellViews)
            view?.ClearPreview();
    }

    // Accesso pubblico alle celle visive (per GridSanitizer)
    public GridCell GetCellView(int x, int y) =>
        IsInBounds(x, y) ? cellViews[x, y] : null;

    // ── Coordinate conversion ─────────────────────────────────────────────
    public Vector2Int ScreenToGridCoord(Vector2 screenPos, Camera cam)
    {
        var rt = GetComponent<RectTransform>();
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screenPos, null, out Vector2 local);
        // Con pivot (0,0) local è già relativo all'angolo in basso-sinistra
        return new Vector2Int(
            Mathf.FloorToInt(local.x / cellSize),
            Mathf.FloorToInt(local.y / cellSize));
    }
}