using UnityEngine;

/// <summary>
/// Attacca su GridContainer insieme a GridManager.
/// Ogni frame scorre tutte le celle e corregge incongruenze visive:
/// - Celle marcate come libere ma con colore sbagliato
/// - Celle occupate da pezzi che non esistono più
/// </summary>
[RequireComponent(typeof(GridManager))]
public class GridSanitizer : MonoBehaviour
{
    GridManager grid;

    void Awake() => grid = GetComponent<GridManager>();

    void LateUpdate()
    {
        for (int x = 0; x < grid.Width; x++)
            for (int y = 0; y < grid.Height; y++)
            {
                var state = grid.GetCell(x, y);
                if (state == null || !state.isActive) continue;

                var view = grid.GetCellView(x, y);
                if (view == null) continue;

                // Se la cella ha un occupant ma il pezzo non è più piazzato lì, pulisci
                if (state.occupant != null)
                {
                    bool stillValid = false;
                    foreach (var cell in state.occupant.WorldCells())
                    {
                        if (cell.localCoord.x == x && cell.localCoord.y == y && cell.occupiesSpace)
                        {
                            stillValid = true;
                            break;
                        }
                    }
                    if (!stillValid)
                    {
                        state.occupant = null;
                        view.SetEmpty();
                    }
                }

                // Se la cella è fisicamente libera, resetta il visual
                if (state.occupant == null)
                    view.SetEmpty();
            }
    }
}