using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Controlla quali bauli sulla griglia ricevono l'energia richiesta
/// dal lato configurato, e restituisce la lista di quelli "aperti".
/// </summary>
public static class ChestSolver
{
    /// <summary>
    /// Restituisce i gridPosition dei bauli che hanno ricevuto la loro energia richiesta.
    /// </summary>
    public static HashSet<Vector2Int> GetOpenChests(GridManager grid, Vector2Int source)
    {
        var open = new HashSet<Vector2Int>();
        if (grid == null) return open;

        foreach (var piece in grid.PlacedPieces)
        {
            if (piece.gridPosition.x < 0) continue;
            if (!piece.data.isChest) continue;

            if (IsChestReceivingEnergy(piece, grid, source))
                open.Add(piece.gridPosition);
        }
        return open;
    }

    static bool IsChestReceivingEnergy(Piece chest, GridManager grid, Vector2Int source)
    {
        var data      = chest.data;
        var inputSide = RotateSide(data.chestInputSide, chest.rotation);
        var dir       = SideToDir(inputSide);
        if (dir == Vector2Int.zero) return false;

        // La cella da cui deve arrivare l'energia è la cella adiacente nella direzione inputSide
        // (il baule riceve da fuori, quindi la cella source dell'energia è chest.gridPosition + dir)
        var energyCell = chest.gridPosition + dir;

        var reached = CircuitSolver.GetReachedCells(grid, source, data.chestRequiredEnergy);
        return reached.Contains(energyCell);
    }

    static PieceData.ConnectionSides RotateSide(PieceData.ConnectionSides side, int rotation)
    {
        // Stessa logica di HydraulicSolver.RotateSide
        for (int i = 0; i < rotation % 4; i++)
        {
            var next = PieceData.ConnectionSides.None;
            if ((side & PieceData.ConnectionSides.Up)    != 0) next |= PieceData.ConnectionSides.Right;
            if ((side & PieceData.ConnectionSides.Right) != 0) next |= PieceData.ConnectionSides.Down;
            if ((side & PieceData.ConnectionSides.Down)  != 0) next |= PieceData.ConnectionSides.Left;
            if ((side & PieceData.ConnectionSides.Left)  != 0) next |= PieceData.ConnectionSides.Up;
            side = next;
        }
        return side;
    }

    static Vector2Int SideToDir(PieceData.ConnectionSides side) => side switch
    {
        PieceData.ConnectionSides.Up    => Vector2Int.up,
        PieceData.ConnectionSides.Down  => Vector2Int.down,
        PieceData.ConnectionSides.Left  => Vector2Int.left,
        PieceData.ConnectionSides.Right => Vector2Int.right,
        _                               => Vector2Int.zero
    };
}
