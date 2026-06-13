using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(GridManager))]
public class CircuitVisualizer : MonoBehaviour
{
    [Header("Colori sorgente / destinazione")]
    public Color colorSource = new Color(0.2f, 1.0f, 0.4f, 1f);
    public Color colorDestReached = new Color(1.0f, 0.9f, 0.1f, 1f);
    public Color colorDestWaiting = new Color(0.9f, 0.3f, 0.2f, 1f);

    [Header("Colori energia meccanica")]
    // Marrone
    public Color colorMechanical = new Color(0.45f, 0.28f, 0.12f, 0.8f);

    [Header("Colori energia idrica")]
    // Blu
    public Color colorHydraulic = new Color(0.2f, 0.6f, 1.0f, 0.8f);

    [Header("Colori energia elettrica")]
    // Giallo fisso per l'energia elettrica
    public Color colorElectric = new Color(1.0f, 0.9f, 0.2f, 0.9f);

    GridManager grid;
    GridCell[,] cellViews;

    void Awake() => grid = GetComponent<GridManager>();

    void Start()
    {
        grid.OnGridChanged += () => StartCoroutine(RefreshNextFrame());
    }

    void OnDestroy()
    {
        if (grid != null) grid.OnGridChanged -= () => StartCoroutine(RefreshNextFrame());
    }

    System.Collections.IEnumerator RefreshNextFrame()
    {
        yield return null;
        yield return null;
        Refresh();
    }

    public void Refresh()
    {
        var srcs = grid.level.GetSources();
        var source = srcs.Count > 0 ? srcs[0].position : grid.level.circuitSource;
        var dest = grid.level.GetDestinations().Count > 0
            ? grid.level.GetDestinations()[0].position : grid.level.circuitDestination;
        var map = CircuitSolver.BuildConductMap(grid);

        // Calcola flussi
        var mechReached = MechanicalSolver.GetReachedCells(map, source);
        var elecInstab = ElectricSolver.GetReachedWithInstability(map, source);
        var hydrReached = HydraulicSolver.GetReachedCells(map, source, grid.Width, grid.Height, grid);
        var typedFlow = TypedConverterSolver.GetFlow(grid, source);

        bool solved = mechReached.Contains(dest) || elecInstab.ContainsKey(dest)
                   || hydrReached.Contains(dest) || typedFlow.ContainsKey(dest);

        // Reset tutti gli overlay
        cellViews = null;
        foreach (var cell in GetComponentsInChildren<GridCell>())
            cell.SetFlowColor(Color.clear);

        // Colora celle
        for (int x = 0; x < grid.Width; x++)
            for (int y = 0; y < grid.Height; y++)
            {
                var coord = new Vector2Int(x, y);
                var state = grid.GetCell(x, y);
                if (state == null || !state.isActive) continue;
                var view = GetCellView(x, y);
                if (view == null) continue;

                var coordIsSource = grid.level.IsSourceCell(coord);
                var coordIsDest = grid.level.IsDestCell(coord);
                if (coordIsSource) { view.SetFlowColor(colorSource); continue; }
                if (coordIsDest)
                {
                    // Stato per-destinazione
                    bool destOk = false;
                    foreach (var dd in grid.level.GetDestinations())
                        if (dd.position == coord)
                        { destOk = CircuitSolver.IsDestinationSatisfied(grid, dd); break; }
                    view.SetFlowColor(destOk ? colorDestReached : colorDestWaiting);
                    continue;
                }

                bool inMech = mechReached.Contains(coord);
                bool inElec = elecInstab.ContainsKey(coord);
                bool inHydr = hydrReached.Contains(coord);
                bool inTyped = typedFlow.ContainsKey(coord);
                if (!inMech && !inElec && !inHydr && !inTyped) continue;

                Color mixed = Color.clear;
                int count = 0;

                if (inMech) { mixed += colorMechanical; count++; } // marrone
                if (inHydr) { mixed += colorHydraulic; count++; }  // blu
                if (inElec)
                {
                    // giallo fisso per elettrico
                    mixed += colorElectric;
                    count++;
                }
                if (inTyped)
                {
                    // Convertitore tipizzato: colore del tipo di energia in uscita
                    mixed += typedFlow[coord] switch
                    {
                        EnergyType.Mechanical => colorMechanical,
                        EnergyType.Hydraulic => colorHydraulic,
                        _ => colorElectric,
                    };
                    count++;
                }

                if (count > 0) mixed /= count;
                view.SetFlowColor(mixed);
            }
    }

    GridCell GetCellView(int x, int y)
    {
        if (cellViews == null)
        {
            cellViews = new GridCell[grid.Width, grid.Height];
            foreach (var v in GetComponentsInChildren<GridCell>())
            {
                var c = v.Coord;
                if (grid.IsInBounds(c)) cellViews[c.x, c.y] = v;
            }
        }
        return (x >= 0 && x < grid.Width && y >= 0 && y < grid.Height) ? cellViews[x, y] : null;
    }
}