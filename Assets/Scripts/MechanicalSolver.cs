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

    /// <summary>BFS standard — celle raggiunge dall'energia meccanica.</summary>
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

                bool emits = HasMechSide(curCh, outS, isOut: true);
                bool accepts = HasMechSide(nextCh, inS, isOut: false);
                if (!emits || !accepts) continue;

                reached.Add(next);
                queue.Enqueue(next);
            }
        }
        return reached;
    }

    /// <summary>
    /// Aggiunge alla conductMap i canali meccanici dei gear collegati via cinghia
    /// che non sono già nella mappa (gear non connessi direttamente).
    /// </summary>
    public static void AddBeltGearsToMap(
        Dictionary<Vector2Int, List<PieceData.EnergyChannel>> conductMap,
        GridManager grid)
    {
        if (grid == null) return;
        var draggers = UnityEngine.Object.FindObjectsByType<PieceDragger>(
            UnityEngine.FindObjectsSortMode.None);

        var allSides = PieceData.ConnectionSides.Up | PieceData.ConnectionSides.Down
                     | PieceData.ConnectionSides.Left | PieceData.ConnectionSides.Right;

        foreach (var d in draggers)
        {
            if (!d.isBelt) continue;
            if (d.piece.gridPosition.x < 0 || d.beltEndCell.x < 0) continue;

            var posA = d.piece.gridPosition;
            var posB = d.beltEndCell;

            // Assicura che entrambe le celle esistano nella conductMap
            foreach (var gearPos in new[] { posA, posB })
            {
                if (!conductMap.ContainsKey(gearPos))
                {
                    conductMap[gearPos] = new List<PieceData.EnergyChannel>
                    {
                        new PieceData.EnergyChannel
                        { type = EnergyType.Mechanical, conductIn = allSides,
                          conductOut = allSides, instability = 0f }
                    };
                }
            }

            // Crea celle intermedie lungo il percorso tra A e B
            // così il BFS può attraversare anche diagonali (passo per passo)
            var cur = posA;
            int steps = 0;
            while (cur != posB && steps < 10)
            {
                steps++;
                int dx = posB.x > cur.x ? 1 : posB.x < cur.x ? -1 : 0;
                int dy = posB.y > cur.y ? 1 : posB.y < cur.y ? -1 : 0;
                var next = cur + new Vector2Int(dx, dy);
                if (!conductMap.ContainsKey(next))
                    conductMap[next] = new List<PieceData.EnergyChannel>
                    {
                        new PieceData.EnergyChannel
                        { type = EnergyType.Mechanical, conductIn = allSides,
                          conductOut = allSides, instability = 0f }
                    };
                cur = next;
            }
        }
    }

    /// <summary>
    /// Restituisce lo stato on/off per gridPosition di ogni pezzo meccanico.
    /// Un pezzo multi-cella ha un solo stato — determinato dalla sua cella principale.
    /// </summary>
    public static Dictionary<Vector2Int, bool> GetGearStates(
        Dictionary<Vector2Int, List<PieceData.EnergyChannel>> conductMap,
        Vector2Int source,
        GridManager grid = null)
    {
        var cellStates = new Dictionary<Vector2Int, bool>(); // stato per cella
        var pieceStates = new Dictionary<Vector2Int, bool>(); // stato per gridPosition

        // Aggiungi i gear collegati via cinghia alla mappa prima del BFS
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

            // Determina il gridPosition del pezzo che occupa questa cella
            Vector2Int curPiecePos = cur;
            if (grid != null)
            {
                var cs = grid.GetCell(cur.x, cur.y);
                if (cs?.occupant != null) curPiecePos = cs.occupant.gridPosition;
            }

            // Tutte le celle dello stesso pezzo hanno lo stesso stato
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

                bool emits = HasMechSide(curCh, outS, isOut: true);
                bool accepts = HasMechSide(nextCh, inS, isOut: false);
                if (!emits || !accepts) continue;

                // Cella adiacente — appartiene allo stesso pezzo?
                Vector2Int nextPiecePos = next;
                if (grid != null)
                {
                    var ns = grid.GetCell(next.x, next.y);
                    if (ns?.occupant != null) nextPiecePos = ns.occupant.gridPosition;
                }

                bool nextOn;
                if (nextPiecePos == curPiecePos)
                    nextOn = pieceOn;       // stessa entità → stesso stato
                else
                    nextOn = !pieceOn;      // pezzo diverso → stato opposto

                cellStates[next] = nextOn;
                if (!pieceStates.ContainsKey(nextPiecePos))
                    pieceStates[nextPiecePos] = nextOn;

                queue.Enqueue(next);
            }
        }

        // Applica le cinghie
        if (grid != null)
        {
            BeltSolver.ApplyBelts(pieceStates, grid);

            // Se un gear è stato aggiunto via cinghia, rilancia il BFS da quel gear
            // per propagare ai gear adiacenti successivi
            bool changed = true;
            int safetyLimit = 20;
            while (changed && safetyLimit-- > 0)
            {
                changed = false;
                foreach (var kv in new Dictionary<Vector2Int, bool>(pieceStates))
                {
                    if (!conductMap.ContainsKey(kv.Key)) continue;
                    if (!conductMap.TryGetValue(kv.Key, out var kvCh)) continue;
                    foreach (var (dir, outS, inS) in Dirs)
                    {
                        var nb = kv.Key + dir;
                        if (pieceStates.ContainsKey(nb)) continue;
                        if (!conductMap.TryGetValue(nb, out var nbCh)) continue;
                        if (!HasMechSide(kvCh, outS, isOut: true)) continue;
                        if (!HasMechSide(nbCh, inS, isOut: false)) continue;
                        // Nuovo gear adiacente — stato opposto
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