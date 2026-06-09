using System.Collections.Generic;
using UnityEngine;

public static class GenericSolver
{
    static readonly (Vector2Int dir, PieceData.ConnectionSides outS, PieceData.ConnectionSides inS)[] Dirs =
    {
        (Vector2Int.right, PieceData.ConnectionSides.Right, PieceData.ConnectionSides.Left),
        (Vector2Int.left,  PieceData.ConnectionSides.Left,  PieceData.ConnectionSides.Right),
        (Vector2Int.up,    PieceData.ConnectionSides.Up,    PieceData.ConnectionSides.Down),
        (Vector2Int.down,  PieceData.ConnectionSides.Down,  PieceData.ConnectionSides.Up),
    };

    /// <summary>
    /// Cerca convertitori adiacenti a celle già raggiunte dai solver normali.
    /// Un convertitore è "attivato" se una cella raggiunta emette verso di lui
    /// e lui ha IN Generic su quel lato.
    /// Da lì propaga Generic verso i pezzi non ancora raggiunti.
    /// </summary>
    public static Dictionary<Vector2Int, EnergyType> GetReachedFromConverters(
        Dictionary<Vector2Int, List<PieceData.EnergyChannel>> conductMap,
        HashSet<Vector2Int> alreadyReached,
        HashSet<Vector2Int> converterCells)
    {
        var result = new Dictionary<Vector2Int, EnergyType>();
        if (converterCells == null || converterCells.Count == 0) return result;

        var queue = new Queue<(Vector2Int coord, EnergyType incomingType)>();

        // Cerca convertitori adiacenti a celle già raggiunte
        foreach (var conv in converterCells)
        {
            if (!conductMap.TryGetValue(conv, out var convCh)) continue;

            foreach (var (dir, outS, inS) in Dirs)
            {
                var neighbor = conv + dir;
                if (!alreadyReached.Contains(neighbor)) continue;

                EnergyType emittedType = EnergyType.Generic;
                bool found = false;

                if (conductMap.TryGetValue(neighbor, out var neighborCh))
                {
                    // Vicino con tubo — controlla OUT verso convertitore
                    // Il convertitore ha IN Generic sul lato verso il vicino (inS)?
                    if (!HasSide(convCh, inS, isOut: false, EnergyType.Generic)) continue;

                    foreach (var ch in neighborCh)
                    {
                        if (ch.type == EnergyType.Generic) continue;
                        if ((ch.conductOut & outS) == 0) continue;
                        emittedType = ch.type;
                        found = true;
                        break;
                    }
                }
                else
                {
                    // Vicino senza tubo = cella cascata idrica
                    // La cascata arriva dall'alto (dir = down dal convertitore = vicino è sopra)
                    // dir=up → neighbor è sopra → cascata scende verso il convertitore
                    if (dir == Vector2Int.up)
                    {
                        // Controlla che il convertitore abbia IN Generic sul lato Up (inS=Down? no)
                        // dir=up, inS=Down → il convertitore riceve dall'alto con IN:Up
                        if (!HasSide(convCh, PieceData.ConnectionSides.Up, isOut: false, EnergyType.Generic)) continue;
                        emittedType = EnergyType.Hydraulic;
                        found = true;
                    }
                }

                if (!found) continue;

                if (!result.ContainsKey(conv))
                {
                    result[conv] = emittedType;
                    queue.Enqueue((conv, emittedType));
                }
            }
        }

        // BFS dal convertitore verso i pezzi non ancora raggiunti
        while (queue.Count > 0)
        {
            var (cur, curType) = queue.Dequeue();
            if (!conductMap.TryGetValue(cur, out var curCh)) continue;

            bool curIsConverter = converterCells.Contains(cur);

            foreach (var (dir, outS, inS) in Dirs)
            {
                var next = cur + dir;
                if (alreadyReached.Contains(next)) continue;
                if (result.ContainsKey(next)) continue;
                if (!conductMap.TryGetValue(next, out var nextCh)) continue;

                // Cella corrente emette su questo lato?
                bool emits = curIsConverter
                    ? HasSide(curCh, outS, isOut: true, EnergyType.Generic)
                    : HasSide(curCh, outS, isOut: true, curType);
                if (!emits) continue;

                bool nextIsConverter = converterCells.Contains(next);
                EnergyType nextType;

                if (nextIsConverter)
                {
                    // Altro convertitore — mantiene il tipo corrente
                    if (!HasSide(nextCh, inS, isOut: false, EnergyType.Generic)) continue;
                    nextType = curType;
                }
                else if (curIsConverter)
                {
                    // Uscita dal convertitore → il prossimo pezzo DEVE avere IN
                    // del tipo adottato (non accetta tipi diversi)
                    // Trova il tipo del canale del prossimo pezzo su inS
                    EnergyType channelType = EnergyType.Generic;
                    bool accepted = false;
                    foreach (var ch in nextCh)
                    {
                        if ((ch.conductIn & inS) == 0) continue;
                        if (ch.type == EnergyType.Generic) continue; // il prossimo non può essere generico
                        channelType = ch.type;
                        accepted = true;
                        break;
                    }
                    if (!accepted) continue;
                    // Adotta il tipo del prossimo pezzo — da qui in poi è energia tipizzata
                    nextType = channelType;
                }
                else
                {
                    // Propagazione già tipizzata — il prossimo deve avere ESATTAMENTE curType
                    if (!HasSide(nextCh, inS, isOut: false, curType)) continue;
                    nextType = curType;
                }

                result[next] = nextType;
                queue.Enqueue((next, nextType));
            }
        }

        return result;
    }

    static bool HasSide(List<PieceData.EnergyChannel> channels,
                        PieceData.ConnectionSides side, bool isOut, EnergyType type)
    {
        foreach (var ch in channels)
        {
            if (ch.type != type) continue;
            var s = isOut ? ch.conductOut : ch.conductIn;
            if ((s & side) != 0) return true;
        }
        return false;
    }
}