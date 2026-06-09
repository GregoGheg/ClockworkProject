using System.Collections.Generic;
using UnityEngine;

public static class ElectricSolver
{
    const float INSTABILITY_THRESHOLD = 10f;

    static readonly (Vector2Int dir, PieceData.ConnectionSides outS, PieceData.ConnectionSides inS)[] Dirs =
    {
        (Vector2Int.right, PieceData.ConnectionSides.Right, PieceData.ConnectionSides.Left),
        (Vector2Int.left,  PieceData.ConnectionSides.Left,  PieceData.ConnectionSides.Right),
        (Vector2Int.up,    PieceData.ConnectionSides.Up,    PieceData.ConnectionSides.Down),
        (Vector2Int.down,  PieceData.ConnectionSides.Down,  PieceData.ConnectionSides.Up),
    };

    public static HashSet<Vector2Int> GetReachedCells(
        Dictionary<Vector2Int, List<PieceData.EnergyChannel>> conductMap,
        Vector2Int source)
    {
        var dict = GetReachedWithInstability(conductMap, source);
        var set = new HashSet<Vector2Int>();
        foreach (var k in dict.Keys) set.Add(k);
        return set;
    }

    public static Dictionary<Vector2Int, float> GetReachedWithInstability(
        Dictionary<Vector2Int, List<PieceData.EnergyChannel>> conductMap,
        Vector2Int source)
    {
        var dist = new Dictionary<Vector2Int, float>();
        var finalized = new HashSet<Vector2Int>(); // nodi definitivamente processati

        if (!conductMap.ContainsKey(source)) return dist;

        var open = new List<(float cost, Vector2Int coord)>();
        dist[source] = 0f;
        open.Add((0f, source));

        int safetyLimit = conductMap.Count * 8; // max iterazioni = 8 * celle
        int iterations = 0;

        while (open.Count > 0 && iterations++ < safetyLimit)
        {
            // Trova il minimo
            int minIdx = 0;
            for (int i = 1; i < open.Count; i++)
                if (open[i].cost < open[minIdx].cost) minIdx = i;

            var (curCost, cur) = open[minIdx];
            open.RemoveAt(minIdx);

            // Già finalizzato — skip
            if (finalized.Contains(cur)) continue;
            finalized.Add(cur);

            if (!conductMap.TryGetValue(cur, out var curCh)) continue;

            foreach (var (dir, outS, inS) in Dirs)
            {
                var next = cur + dir;
                if (finalized.Contains(next)) continue;
                if (!conductMap.TryGetValue(next, out var nextCh)) continue;

                bool emits = HasElectricSide(curCh, outS, isOut: true);
                bool accepts = HasElectricSide(nextCh, inS, isOut: false);
                if (!emits || !accepts) continue;

                float addedInstab = 0f;
                foreach (var ch in nextCh)
                    if (ch.type == EnergyType.Electric) addedInstab += ch.instability;

                float nextCost = curCost + addedInstab;

                // Clamp: non permettere instabilità negativa (la resistenza azzera, non va sotto 0)
                nextCost = Mathf.Max(0f, nextCost);

                if (nextCost >= INSTABILITY_THRESHOLD) continue;

                if (!dist.TryGetValue(next, out float prevCost) || nextCost < prevCost)
                {
                    dist[next] = nextCost;
                    open.Add((nextCost, next));
                }
            }
        }

        return dist;
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