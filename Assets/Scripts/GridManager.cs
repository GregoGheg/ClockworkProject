using System.Collections.Generic;
using UnityEngine;

public class GridManager : MonoBehaviour
{
    // Stato di una singola cella logica
    public class CellState
    {
        public Piece occupant;   // pezzo che occupa fisicamente questa cella
        public bool isActive;    // cella visibile e interagibile
        // conductIn/Out rimossi — calcolati fresh da CircuitSolver.BuildConductMap
    }

    [Header("Setup")]
    public LevelData level;
    public GridCell cellPrefab;
    public float cellSize = 80f;

    CellState[,] grid;
    GridCell[,] cellViews;

    public int Width => level.gridWidth;
    public int Height => level.gridHeight;

    public System.Action OnGridChanged;

    readonly List<Piece> placedPieces = new();
    public IReadOnlyList<Piece> PlacedPieces => placedPieces;

    void Awake()
    {
        var rt = GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
        rt.sizeDelta = new Vector2(Width * cellSize, Height * cellSize);

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

        // Marca sorgente e destinazione
        if (IsInBounds(level.circuitSource))
            cellViews[level.circuitSource.x, level.circuitSource.y].SetAsSource();
        if (IsInBounds(level.circuitDestination))
            cellViews[level.circuitDestination.x, level.circuitDestination.y].SetAsDestination();
    }

    // ── Bounds & query ────────────────────────────────────────────────────
    public bool IsInBounds(int x, int y) =>
        x >= 0 && x < Width && y >= 0 && y < Height;

    public bool IsInBounds(Vector2Int v) => IsInBounds(v.x, v.y);

    public CellState GetCell(int x, int y) =>
        IsInBounds(x, y) ? grid[x, y] : null;

    public CellState GetCell(Vector2Int v) => GetCell(v.x, v.y);

    // Una cella è libera se è attiva, in bounds, e non ha un occupant fisico
    public bool IsFree(int x, int y)
    {
        if (!IsInBounds(x, y)) return false;
        var c = grid[x, y];
        return c.isActive && c.occupant == null;
    }

    // ── Placement ─────────────────────────────────────────────────────────
    public bool CanPlace(Piece piece, Vector2Int at)
    {
        var test = piece.Clone();
        test.gridPosition = at;
        foreach (var cell in test.WorldCells())
        {
            var coord = cell.localCoord;
            // Le celle non-fisiche possono andare ovunque — fuori bounds, sopra altri pezzi
            if (!cell.occupiesSpace) continue;
            // Le celle fisiche devono stare in bounds su una cella libera e attiva
            if (!IsFree(coord.x, coord.y)) return false;
        }
        return true;
    }

    public bool TryPlace(Piece piece, Vector2Int at)
    {
        if (!CanPlace(piece, at)) return false;
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
        OnGridChanged?.Invoke();
        return true;
    }

    public void Remove(Piece piece)
    {
        if (piece.gridPosition.x < 0) return;

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