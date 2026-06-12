using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Estende CollectorSolver propagando il flusso elettrico
/// dalle celle del collector nelle direzioni conductOut.
/// Questo permette a pezzi come la resistenza di ricevere
/// energia dal collector e continuare il circuito.
/// </summary>
public static class CollectorPropagator
{
    static readonly (Vector2Int dir, PieceData.ConnectionSides outS, PieceData.ConnectionSides inS)[] Dirs =
    {
        (Vector2Int.right, PieceData.ConnectionSides.Right, PieceData.ConnectionSides.Left),
        (Vector2Int.left,  PieceData.ConnectionSides.Left,  PieceData.ConnectionSides.Right),
        (Vector2Int.up,    PieceData.ConnectionSides.Up,    PieceData.ConnectionSides.Down),
        (Vector2Int.down,  PieceData.ConnectionSides.Down,  PieceData.ConnectionSides.Up),
    };

    const float COLLECTOR_INSTABILITY = 9f;
    const float INSTABILITY_THRESHOLD = 10f;

    /// <summary>
    /// Prende le celle già raggiunte dal collector (instabilità 9)
    /// e propaga l'elettricità nelle direzioni conductOut del collector,
    /// sommando l'instabilità dei pezzi successivi (come fa ElectricSolver).
    /// </summary>
    public static Dictionary<Vector2Int, float> PropagateFromCollector(
        Dictionary<Vector2Int, List<PieceData.EnergyChannel>> conductMap,
        Dictionary<Vector2Int, float> collectorFlow,
        GridManager grid)
    {
        var result = new Dictionary<Vector2Int, float>(collectorFlow);

        if (collectorFlow.Count == 0) return result;

        // Usa le celle del collector come seed per una nuova propagazione Dijkstra
        var open = new List<(float cost, Vector2Int coord)>();
        var finalized = new HashSet<Vector2Int>();

        foreach (var kv in collectorFlow)
        {
            open.Add((kv.Value, kv.Key));
        }

        int safetyLimit = conductMap.Count * 8;
        int iterations = 0;

        while (open.Count > 0 && iterations++ < safetyLimit)
        {
            // Trova il minimo
            int minIdx = 0;
            for (int i = 1; i < open.Count; i++)
                if (open[i].cost < open[minIdx].cost) minIdx = i;

            var (curCost, cur) = open[minIdx];
            open.RemoveAt(minIdx);

            if (finalized.Contains(cur)) continue;
            finalized.Add(cur);

            if (!conductMap.TryGetValue(cur, out var curCh)) continue;

            foreach (var (dir, outS, inS) in Dirs)
            {
                var next = cur + dir;
                if (finalized.Contains(next)) continue;
                if (!conductMap.TryGetValue(next, out var nextCh)) continue;

                // Il collector o la cella corrente deve emettere in questa direzione
                bool emits = HasElectricSide(curCh, outS, isOut: true);
                bool accepts = HasElectricSide(nextCh, inS, isOut: false);
                if (!emits || !accepts) continue;

                // Somma instabilità del prossimo pezzo
                float addedInstab = 0f;
                foreach (var ch in nextCh)
                    if (ch.type == EnergyType.Electric)
                        addedInstab += ch.instability;

                float nextCost = Mathf.Max(0f, curCost + addedInstab);

                if (nextCost >= INSTABILITY_THRESHOLD) continue;

                if (!result.TryGetValue(next, out float prevCost) || nextCost < prevCost)
                {
                    result[next] = nextCost;
                    open.Add((nextCost, next));
                }
            }
        }

        return result;
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