using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gestisce il collettore elettrico.
/// Cerca in linea retta (4 direzioni) il primo blocco con OUT elettrico attivo
/// a instabilità ≤2. Se trovato senza ostacoli, crea un flusso verso il collettore
/// con instabilità 9 (indipendentemente dalla distanza).
/// </summary>
public static class CollectorSolver
{
    static readonly Vector2Int[] Dirs =
    {
        Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down
    };

    static readonly (Vector2Int dir, PieceData.ConnectionSides outS, PieceData.ConnectionSides inS)[] DirSides =
    {
        (Vector2Int.right, PieceData.ConnectionSides.Right, PieceData.ConnectionSides.Left),
        (Vector2Int.left,  PieceData.ConnectionSides.Left,  PieceData.ConnectionSides.Right),
        (Vector2Int.up,    PieceData.ConnectionSides.Up,    PieceData.ConnectionSides.Down),
        (Vector2Int.down,  PieceData.ConnectionSides.Down,  PieceData.ConnectionSides.Up),
    };

    const float MAX_SOURCE_INSTABILITY = 2f;
    const float COLLECTOR_INSTABILITY = 9f;

    /// <summary>
    /// Restituisce le celle raggiunte dai collettori con instabilità 9.
    /// Key = coordinata cella, Value = instabilità (sempre 9 per celle collettore).
    /// </summary>
    public static Dictionary<Vector2Int, float> GetCollectorFlow(
        Dictionary<Vector2Int, List<PieceData.EnergyChannel>> conductMap,
        Dictionary<Vector2Int, float> existingInstability,
        HashSet<Vector2Int> collectorCells,
        GridManager grid)
    {
        var result = new Dictionary<Vector2Int, float>();
        if (collectorCells == null || collectorCells.Count == 0) return result;

        foreach (var collCoord in collectorCells)
        {
            // Cerca in ogni direzione il primo blocco con OUT elettrico a instab ≤2
            foreach (var (dir, outS, inS) in DirSides)
            {
                var searchPos = collCoord + dir;
                Vector2Int? sourceFound = null;
                int maxSteps = grid.Width + grid.Height; // limite sicuro

                for (int step = 0; step < maxSteps && grid.IsInBounds(searchPos); step++)
                {
                    var cellState = grid.GetCell(searchPos.x, searchPos.y);
                    bool hasPhysical = cellState?.occupant != null;

                    if (hasPhysical)
                    {
                        // Pezzo fisico — controlla se ha OUT elettrico verso il collettore
                        // outS è la direzione dal vicino verso il collettore
                        if (conductMap.TryGetValue(searchPos, out var ch))
                        {
                            // Il blocco emette nella direzione VERSO il collettore
                            // dir = right → blocco è a destra del coll → deve emettere Left
                            // quindi cercaimo outS = inS (lato opposto)
                            bool hasOut = HasElectricSide(ch, inS, isOut: true);
                            float instab = existingInstability.ContainsKey(searchPos)
                                ? existingInstability[searchPos] : float.MaxValue;

                            if (hasOut && instab <= MAX_SOURCE_INSTABILITY)
                                sourceFound = searchPos;
                        }
                        break; // qualsiasi pezzo fisico blocca la ricerca
                    }

                    searchPos += dir;
                }

                if (sourceFound == null) continue;

                // Crea il flusso dalle celle tra source e collettore
                // dir = direzione dal collettore verso il source
                // quindi il flusso va da source VERSO il collettore = direzione -dir
                var pos = sourceFound.Value - dir; // prima cella dopo il source verso coll
                int steps2 = 0;
                while (pos != collCoord && steps2 < maxSteps)
                {
                    if (grid.IsInBounds(pos) && !result.ContainsKey(pos))
                        result[pos] = COLLECTOR_INSTABILITY;
                    pos -= dir;
                    steps2++;
                }
                if (!result.ContainsKey(collCoord))
                    result[collCoord] = COLLECTOR_INSTABILITY;
            }
        }

        return result;
    }

    public static HashSet<Vector2Int> BuildCollectorCells(GridManager grid)
    {
        var cells = new HashSet<Vector2Int>();
        foreach (var piece in grid.PlacedPieces)
        {
            if (piece.gridPosition.x < 0) continue;
            if (!piece.data.isCollector) continue;
            foreach (var cell in piece.WorldCells())
                if (grid.IsInBounds(cell.localCoord))
                    cells.Add(cell.localCoord);
        }
        return cells;
    }

    static bool HasElectricSide(List<PieceData.EnergyChannel> channels,
                                  PieceData.ConnectionSides side, bool isOut)
    {
        foreach (var ch in channels)
        {
            if (ch.type != EnergyType.Electric) continue;
            var s = isOut ? ch.conductOut : ch.conductIn;
            if ((s & side) != 0) return true;
        }
        return false;
    }
}