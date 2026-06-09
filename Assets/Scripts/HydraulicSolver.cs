using System.Collections.Generic;
using UnityEngine;

public static class HydraulicSolver
{
    public struct FlowCell
    {
        public Vector2Int coord;
        public float velocity;
        public float energy;
        public bool isCascade;
        public int cellsFallen;
        public bool cameFromAbove;
    }

    const float BASE_ENERGY = 1f;
    const float VELOCITY_GAIN = 0.5f;
    const float ENERGY_GAIN = 0.3f;
    const float UP_ENERGY_COST = 0.8f;

    public static Dictionary<Vector2Int, FlowCell> GetFlowMap(
        Dictionary<Vector2Int, List<PieceData.EnergyChannel>> conductMap,
        Vector2Int source, int gridWidth, int gridHeight, GridManager grid = null)
    {
        var flow = new Dictionary<Vector2Int, FlowCell>();
        var visited = new HashSet<Vector2Int>();

        if (!conductMap.ContainsKey(source)) return flow;
        bool srcHydro = false;
        foreach (var ch in conductMap[source])
            if (ch.type == EnergyType.Hydraulic) { srcHydro = true; break; }
        if (!srcHydro) return flow;

        var pumpFired = new HashSet<Vector2Int>(); // gridPosition pompe già sperate

        var startCell = new FlowCell
        {
            coord = source,
            velocity = 0f,
            energy = BASE_ENERGY,
            isCascade = false,
            cellsFallen = 0,
            cameFromAbove = false
        };
        flow[source] = startCell;
        visited.Add(source);
        var queue = new Queue<FlowCell>();
        queue.Enqueue(startCell);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();

            // ── POMPA: se questa è la cella isPumpCell, lancia il getto ──
            if (grid != null)
            {
                var cs = grid.GetCell(cur.coord.x, cur.coord.y);
                if (cs?.occupant?.data != null && cs.occupant.data.isPump)
                {
                    var pumpPiece = cs.occupant;
                    // Controlla se questa cella specifica è isPumpCell
                    bool thisIsPumpCell = false;
                    foreach (var lc in pumpPiece.CurrentCells())
                    {
                        var absPos = pumpPiece.gridPosition + lc.localCoord;
                        if (absPos == cur.coord && lc.isPumpCell) { thisIsPumpCell = true; break; }
                    }

                    if (thisIsPumpCell && !pumpFired.Contains(pumpPiece.gridPosition))
                    {
                        pumpFired.Add(pumpPiece.gridPosition);
                        var pumpOut = pumpPiece.data.pumpOutDirection;
                        int rot = pumpPiece.rotation % 4;
                        for (int r = 0; r < rot; r++) pumpOut = RotateSide(pumpOut);

                        Vector2Int pumpDir = Vector2Int.zero;
                        if ((pumpOut & PieceData.ConnectionSides.Right) != 0) pumpDir = Vector2Int.right;
                        else if ((pumpOut & PieceData.ConnectionSides.Left) != 0) pumpDir = Vector2Int.left;
                        else if ((pumpOut & PieceData.ConnectionSides.Up) != 0) pumpDir = Vector2Int.up;
                        else if ((pumpOut & PieceData.ConnectionSides.Down) != 0) pumpDir = Vector2Int.down;

                        if (pumpDir != Vector2Int.zero)
                        {
                            var pumpPos = cur.coord + pumpDir;
                            while (pumpPos.x >= 0 && pumpPos.x < gridWidth &&
                                   pumpPos.y >= 0 && pumpPos.y < gridHeight &&
                                   !visited.Contains(pumpPos))
                            {
                                var pState = grid?.GetCell(pumpPos.x, pumpPos.y);
                                bool hasOcc = pState?.occupant != null;
                                if (hasOcc)
                                {
                                    if (conductMap.ContainsKey(pumpPos))
                                    {
                                        PieceData.ConnectionSides arr;
                                        if (pumpDir == Vector2Int.right) arr = PieceData.ConnectionSides.Left;
                                        else if (pumpDir == Vector2Int.left) arr = PieceData.ConnectionSides.Right;
                                        else if (pumpDir == Vector2Int.up) arr = PieceData.ConnectionSides.Down;
                                        else arr = PieceData.ConnectionSides.Up;
                                        if (HasHydraulicSide(conductMap[pumpPos], arr, isOut: false))
                                        {
                                            // Entra nel tubo con energia/cellsFallen alti per permettere risalita
                                            var ec = new FlowCell
                                            {
                                                coord = pumpPos,
                                                velocity = 5f,
                                                energy = 10f,
                                                isCascade = false,
                                                cellsFallen = 999,
                                                cameFromAbove = (pumpDir == Vector2Int.down)
                                            };
                                            flow[pumpPos] = ec; visited.Add(pumpPos); queue.Enqueue(ec);
                                        }
                                    }
                                    break;
                                }
                                // Cella vuota del getto: nessuna cascata, solo flusso diretto
                                var pc = new FlowCell
                                {
                                    coord = pumpPos,
                                    velocity = 5f,
                                    energy = 10f,
                                    isCascade = false,
                                    cellsFallen = 0,
                                    cameFromAbove = false
                                };
                                flow[pumpPos] = pc; visited.Add(pumpPos);
                                // NON aggiungiamo alla queue: il getto continua in linea retta
                                // solo il punto di arrivo (tubo con IN) viene accodato
                                pumpPos += pumpDir;
                            }
                        }
                    }
                }
            }

            // ── CELLE NORMALI ──────────────────────────────────────────────
            if (!conductMap.TryGetValue(cur.coord, out var curCh)) continue;
            bool curHydro = false;
            foreach (var ch in curCh)
                if (ch.type == EnergyType.Hydraulic) { curHydro = true; break; }
            if (!curHydro) continue;

            // Propagazione laterale
            foreach (var (dir, outS, inS) in new (Vector2Int, PieceData.ConnectionSides, PieceData.ConnectionSides)[]
            {
                (Vector2Int.right, PieceData.ConnectionSides.Right, PieceData.ConnectionSides.Left),
                (Vector2Int.left,  PieceData.ConnectionSides.Left,  PieceData.ConnectionSides.Right),
            })
            {
                var next = cur.coord + dir;
                if (visited.Contains(next)) continue;
                if (!conductMap.TryGetValue(next, out var nextCh)) continue;
                if (!HasHydraulicSide(curCh, outS, isOut: true)) continue;
                if (!HasHydraulicSide(nextCh, inS, isOut: false)) continue;
                var nc = new FlowCell
                {
                    coord = next,
                    velocity = cur.velocity,
                    energy = cur.energy,
                    isCascade = false,
                    cellsFallen = cur.cellsFallen,
                    cameFromAbove = false
                };
                flow[next] = nc; visited.Add(next); queue.Enqueue(nc);
            }

            // Propagazione verso l'alto (limitata a 2/3 della discesa)
            {
                var next = cur.coord + Vector2Int.up;
                if (!visited.Contains(next) && conductMap.TryGetValue(next, out var nextCh))
                {
                    if (HasHydraulicSide(curCh, PieceData.ConnectionSides.Up, isOut: true) &&
                        HasHydraulicSide(nextCh, PieceData.ConnectionSides.Down, isOut: false))
                    {
                        int canUp = (cur.cellsFallen * 2) / 3;
                        float newNrg = cur.energy - UP_ENERGY_COST;
                        int newFallen = Mathf.Max(0, cur.cellsFallen - 3);
                        if (canUp > 0 && newNrg > 0)
                        {
                            var uc = new FlowCell
                            {
                                coord = next,
                                velocity = cur.velocity,
                                energy = newNrg,
                                isCascade = false,
                                cellsFallen = newFallen,
                                cameFromAbove = false
                            };
                            flow[next] = uc; visited.Add(next); queue.Enqueue(uc);
                        }
                    }
                }
            }

            // ── Cascata verso il basso ─────────────────────────────────────
            bool hasOutDown = HasHydraulicSide(curCh, PieceData.ConnectionSides.Down, isOut: true);
            bool hasOutRight = HasHydraulicSide(curCh, PieceData.ConnectionSides.Right, isOut: true);
            bool hasOutLeft = HasHydraulicSide(curCh, PieceData.ConnectionSides.Left, isOut: true);

            var rightN = cur.coord + Vector2Int.right;
            var leftN = cur.coord + Vector2Int.left;
            bool rightHasTube = conductMap.ContainsKey(rightN) &&
                HasHydraulicSide(conductMap[rightN], PieceData.ConnectionSides.Left, isOut: false);
            bool leftHasTube = conductMap.ContainsKey(leftN) &&
                HasHydraulicSide(conductMap[leftN], PieceData.ConnectionSides.Right, isOut: false);

            bool endOfRight = hasOutRight && (!rightHasTube || visited.Contains(rightN));
            bool endOfLeft = hasOutLeft && (!leftHasTube || visited.Contains(leftN));
            bool doCascade = hasOutDown || cur.isCascade || ((endOfRight || endOfLeft) && !cur.isCascade);
            if (!doCascade) continue;

            var cascadeOrigins = new List<Vector2Int>();
            if (hasOutDown || cur.isCascade) cascadeOrigins.Add(cur.coord);
            if (!cur.isCascade)
            {
                if (endOfRight)
                {
                    var lp = cur.coord + Vector2Int.right;
                    if (!visited.Contains(lp) && lp.x >= 0 && lp.x < gridWidth)
                    {
                        flow[lp] = new FlowCell
                        {
                            coord = lp,
                            velocity = cur.velocity,
                            energy = cur.energy,
                            isCascade = false,
                            cellsFallen = cur.cellsFallen
                        }; visited.Add(lp); cascadeOrigins.Add(lp);
                    }
                }
                if (endOfLeft)
                {
                    var lp = cur.coord + Vector2Int.left;
                    if (!visited.Contains(lp) && lp.x >= 0 && lp.x < gridWidth)
                    {
                        flow[lp] = new FlowCell
                        {
                            coord = lp,
                            velocity = cur.velocity,
                            energy = cur.energy,
                            isCascade = false,
                            cellsFallen = cur.cellsFallen
                        }; visited.Add(lp); cascadeOrigins.Add(lp);
                    }
                }
            }
            if (cascadeOrigins.Count == 0) continue;

            foreach (var origin in cascadeOrigins)
            {
                float cVel = cur.velocity, cNrg = cur.energy; int cFallen = cur.cellsFallen;
                var pos = origin + Vector2Int.down;
                while (pos.y >= 0 && !visited.Contains(pos))
                {
                    var cs2 = grid?.GetCell(pos.x, pos.y);
                    bool hasOcc = cs2?.occupant != null;
                    bool hasHydIn = conductMap.ContainsKey(pos) &&
                        HasHydraulicSide(conductMap[pos], PieceData.ConnectionSides.Up, isOut: false);
                    if (hasOcc && !hasHydIn) break;
                    cVel += VELOCITY_GAIN; cNrg += ENERGY_GAIN; cFallen++;
                    var cc = new FlowCell
                    {
                        coord = pos,
                        velocity = cVel,
                        energy = cNrg,
                        isCascade = true,
                        cellsFallen = cFallen
                    };
                    flow[pos] = cc; visited.Add(pos);
                    if (hasHydIn)
                    {
                        var e = cc; e.isCascade = false; e.cameFromAbove = true;
                        flow[pos] = e; queue.Enqueue(e); break;
                    }
                    pos += Vector2Int.down;
                }
            }
        }
        return flow;
    }

    public static HashSet<Vector2Int> GetReachedCells(
        Dictionary<Vector2Int, List<PieceData.EnergyChannel>> conductMap,
        Vector2Int source, int gridWidth, int gridHeight, GridManager grid = null)
    {
        return new HashSet<Vector2Int>(GetFlowMap(conductMap, source, gridWidth, gridHeight, grid).Keys);
    }

    public static bool HasHydraulicSide(List<PieceData.EnergyChannel> channels,
        PieceData.ConnectionSides side, bool isOut)
    {
        foreach (var ch in channels)
        {
            if (ch.type != EnergyType.Hydraulic) continue;
            if (((isOut ? ch.conductOut : ch.conductIn) & side) != 0) return true;
        }
        return false;
    }

    static PieceData.ConnectionSides RotateSide(PieceData.ConnectionSides s)
    {
        PieceData.ConnectionSides r = PieceData.ConnectionSides.None;
        if ((s & PieceData.ConnectionSides.Up) != 0) r |= PieceData.ConnectionSides.Right;
        if ((s & PieceData.ConnectionSides.Right) != 0) r |= PieceData.ConnectionSides.Down;
        if ((s & PieceData.ConnectionSides.Down) != 0) r |= PieceData.ConnectionSides.Left;
        if ((s & PieceData.ConnectionSides.Left) != 0) r |= PieceData.ConnectionSides.Up;
        return r;
    }
}