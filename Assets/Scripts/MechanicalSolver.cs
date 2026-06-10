using System.Collections.Generic;
using UnityEngine;

public static class MechanicalSolver
{
    static readonly (Vector2Int dir, PieceData.ConnectionSides outS, PieceData.ConnectionSides inS)[] Dirs =
    {
        (Vector2Int.right, PieceData.ConnectionSides.Right, PieceData.ConnectionSides.Left),
        (Vector2Int.left,  PieceData.ConnectionSides.Left,  PieceData.ConnectionSides.Right),
        (Vector2Int.up,    PieceData.ConnectionSides.Up,    PieceData.ConnectionSides.Down),
        (Vector2Int.down,  PieceData.ConnectionSides.Down,  PieceData.ConnectionSides.Up),
    };

    /// <summary>BFS standard — celle raggiunte dall'energia meccanica.</summary>
    public static HashSet<Vector2Int> GetReachedCells(
        Dictionary<Vector2Int, List<PieceData.EnergyChannel>> conductMap,
        Vector2Int source)
    {
        var reached = new HashSet<Vector2Int>();
        if (!conductMap.ContainsKey(source)) return reached;

        reached.Add(source);
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(source);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            if (!conductMap.TryGetValue(cur, out var curCh)) continue;

            foreach (var (dir, outS, inS) in Dirs)
            {
                var next = cur + dir;
                if (reached.Contains(next)) continue;
                if (!conductMap.TryGetValue(next, out var nextCh)) continue;

                // Se la cella ha allSides (gear sintetico da cinghia) bypassa il check
                bool allSidesCur = IsAllSides(curCh);
                bool allSidesNext = IsAllSides(nextCh);
                bool emits = allSidesCur || HasMechSide(curCh, outS, isOut: true);
                bool accepts = allSidesNext || HasMechSide(nextCh, inS, isOut: false);
                if (!emits || !accepts) continue;

                reached.Add(next);
                queue.Enqueue(next);
            }
        }
        return reached;
    }

    /// <summary>
    /// Aggiunge alla conductMap i canali meccanici di TUTTI i gear piazzati
    /// e delle celle intermedie tra i gear collegati via cinghia.
    /// </summary>
    public static void AddBeltGearsToMap(
        Dictionary<Vector2Int, List<PieceData.EnergyChannel>> conductMap,
        GridManager grid)
    {
        if (grid == null) return;

        var allSides = PieceData.ConnectionSides.Up | PieceData.ConnectionSides.Down
                     | PieceData.ConnectionSides.Left | PieceData.ConnectionSides.Right;

        var mechChannel = new PieceData.EnergyChannel
        {
            type = EnergyType.Mechanical,
            conductIn = allSides,
            conductOut = allSides,
            instability = 0f
        };

        // Aggiungi TUTTI i gear piazzati nella conductMap
        foreach (var piece in grid.PlacedPieces)
        {
            if (piece.gridPosition.x < 0) continue;
            if (!piece.data.isGear) continue;
            foreach (var cell in piece.WorldCells())
            {
                var absPos = cell.localCoord;
                if (!conductMap.ContainsKey(absPos))
                    conductMap[absPos] = new List<PieceData.EnergyChannel> { mechChannel };
            }
        }

        // Celle intermedie tra i gear collegati via cinghia
        var draggers = UnityEngine.Object.FindObjectsByType<PieceDragger>(
            UnityEngine.FindObjectsSortMode.None);

        foreach (var d in draggers)
        {
            if (!d.isBelt) continue;
            if (d.piece.gridPosition.x < 0 || d.beltEndCell.x < 0) continue;

            var posA = d.piece.gridPosition;
            var posB = d.beltEndCell;

            var cur = posA;
            int steps = 0;
            while (cur != posB && steps < 30)
            {
                steps++;
                int dx = posB.x > cur.x ? 1 : posB.x < cur.x ? -1 : 0;
                int dy = posB.y > cur.y ? 1 : posB.y < cur.y ? -1 : 0;
                cur = cur + new Vector2Int(dx, dy);
                if (!conductMap.ContainsKey(cur))
                    conductMap[cur] = new List<PieceData.EnergyChannel> { mechChannel };
            }
        }
    }

    /// <summary>
    /// Restituisce lo stato on/off per gridPosition di ogni pezzo meccanico.
    /// </summary>
    public static Dictionary<Vector2Int, bool> GetGearStates(
        Dictionary<Vector2Int, List<PieceData.EnergyChannel>> conductMap,
        Vector2Int source,
        GridManager grid = null)
    {
        var cellStates = new Dictionary<Vector2Int, bool>();
        var pieceStates = new Dictionary<Vector2Int, bool>();

        if (grid != null) AddBeltGearsToMap(conductMap, grid);

        if (!conductMap.ContainsKey(source)) return pieceStates;

        cellStates[source] = true;
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(source);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            bool curOn = cellStates[cur];
            if (!conductMap.TryGetValue(cur, out var curCh)) continue;

            Vector2Int curPiecePos = cur;
            if (grid != null)
            {
                var cs = grid.GetCell(cur.x, cur.y);
                if (cs?.occupant != null) curPiecePos = cs.occupant.gridPosition;
            }

            bool pieceOn = curOn;
            if (pieceStates.ContainsKey(curPiecePos))
                pieceOn = pieceStates[curPiecePos];
            else
                pieceStates[curPiecePos] = curOn;

            foreach (var (dir, outS, inS) in Dirs)
            {
                var next = cur + dir;
                if (cellStates.ContainsKey(next)) continue;
                if (!conductMap.TryGetValue(next, out var nextCh)) continue;

                bool allSidesCur2 = IsAllSides(curCh);
                bool allSidesNext2 = IsAllSides(nextCh);
                bool emits = allSidesCur2 || HasMechSide(curCh, outS, isOut: true);
                bool accepts = allSidesNext2 || HasMechSide(nextCh, inS, isOut: false);
                if (!emits || !accepts) continue;

                Vector2Int nextPiecePos = next;
                if (grid != null)
                {
                    var ns = grid.GetCell(next.x, next.y);
                    if (ns?.occupant != null) nextPiecePos = ns.occupant.gridPosition;
                }

                bool nextOn = (nextPiecePos == curPiecePos) ? pieceOn : !pieceOn;

                cellStates[next] = nextOn;
                if (!pieceStates.ContainsKey(nextPiecePos))
                    pieceStates[nextPiecePos] = nextOn;

                queue.Enqueue(next);
            }
        }

        // Applica le cinghie e propaga ai gear adiacenti
        if (grid != null)
        {
            BeltSolver.ApplyBelts(pieceStates, grid);

            bool changed = true;
            int safetyLimit = 20;
            while (changed && safetyLimit-- > 0)
            {
                changed = false;
                foreach (var kv in new Dictionary<Vector2Int, bool>(pieceStates))
                {
                    if (!conductMap.TryGetValue(kv.Key, out var kvCh)) continue;
                    foreach (var (dir, outS, inS) in Dirs)
                    {
                        var nb = kv.Key + dir;
                        if (pieceStates.ContainsKey(nb)) continue;
                        if (!conductMap.TryGetValue(nb, out var nbCh)) continue;
                        if (!IsAllSides(kvCh) && !HasMechSide(kvCh, outS, isOut: true)) continue;
                        if (!IsAllSides(nbCh) && !HasMechSide(nbCh, inS, isOut: false)) continue;

                        Vector2Int nbPiecePos = nb;
                        if (grid != null)
                        {
                            var ns = grid.GetCell(nb.x, nb.y);
                            if (ns?.occupant != null) nbPiecePos = ns.occupant.gridPosition;
                        }
                        if (!pieceStates.ContainsKey(nbPiecePos))
                        {
                            pieceStates[nbPiecePos] = !kv.Value;
                            changed = true;
                        }
                    }
                }
                BeltSolver.ApplyBelts(pieceStates, grid);
            }
        }

        return pieceStates;
    }

    /// <summary>
    /// Restituisce i gridPosition dei gear in conflitto (adiacenti con stesso stato di rotazione).
    /// I gear in conflitto bloccano tutta la catena a cui appartengono.
    /// </summary>
    public static HashSet<Vector2Int> GetConflicts(
        Dictionary<Vector2Int, bool> pieceStates,
        GridManager grid)
    {
        var conflicts = new HashSet<Vector2Int>();
        if (grid == null) return conflicts;

        foreach (var kv in pieceStates)
        {
            var coord = kv.Key;
            var cs = grid.GetCell(coord.x, coord.y);
            if (cs?.occupant == null || !cs.occupant.data.isGear) continue;

            // Controlla i 4 vicini diretti
            foreach (var dir in new[] { Vector2Int.right, Vector2Int.up })
            {
                var nb = coord + dir;
                if (!pieceStates.TryGetValue(nb, out var nbState)) continue;
                var nbCs = grid.GetCell(nb.x, nb.y);
                if (nbCs?.occupant == null || !nbCs.occupant.data.isGear) continue;
                // Stesso stato = stesso verso di rotazione = conflitto
                if (kv.Value == nbState)
                {
                    conflicts.Add(coord);
                    conflicts.Add(nb);
                }
            }
        }
        return conflicts;
    }

    /// <summary>
    /// Propaga i conflitti: tutti i gear raggiungibili dai gear in conflitto
    /// vengono aggiunti al set (l'intera catena si blocca).
    /// </summary>
    public static HashSet<Vector2Int> PropagateConflicts(
        HashSet<Vector2Int> conflicts,
        Dictionary<Vector2Int, bool> pieceStates,
        Dictionary<Vector2Int, List<PieceData.EnergyChannel>> conductMap,
        GridManager grid)
    {
        if (conflicts.Count == 0) return conflicts;

        var blocked = new HashSet<Vector2Int>(conflicts);
        var queue = new Queue<Vector2Int>(conflicts);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            if (!conductMap.ContainsKey(cur)) continue;

            foreach (var dir in new[] { Vector2Int.right, Vector2Int.left,
                                        Vector2Int.up,    Vector2Int.down })
            {
                var nb = cur + dir;
                if (blocked.Contains(nb)) continue;
                if (!pieceStates.ContainsKey(nb)) continue;
                if (!conductMap.ContainsKey(nb)) continue;
                blocked.Add(nb);
                queue.Enqueue(nb);
            }
        }
        return blocked;
    }

    static bool IsAllSides(List<PieceData.EnergyChannel> channels)
    {
        var all = PieceData.ConnectionSides.Up | PieceData.ConnectionSides.Down
                | PieceData.ConnectionSides.Left | PieceData.ConnectionSides.Right;
        foreach (var ch in channels)
            if (ch.type == EnergyType.Mechanical &&
                ch.conductIn == all && ch.conductOut == all) return true;
        return false;
    }

    static bool HasMechSide(List<PieceData.EnergyChannel> channels,
                             PieceData.ConnectionSides side, bool isOut)
    {
        foreach (var ch in channels)
        {
            if (ch.type != EnergyType.Mechanical) continue;
            var s = isOut ? ch.conductOut : ch.conductIn;
            if ((s & side) != 0) return true;
        }
        return false;
    }
}