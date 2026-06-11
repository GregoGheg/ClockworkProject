using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "GearPuzzle/LevelData")]
public class LevelData : ScriptableObject
{
    public int gridWidth = 6;
    public int gridHeight = 6;

    [Tooltip("Queste coordinate sono disabilitate. Tutte le altre sono attive.")]
    public List<Vector2Int> inactiveCells = new();

    public Vector2Int circuitSource;
    public Vector2Int circuitDestination;

    [Tooltip("Tipi di energia che la sorgente emette. Vuoto = tutti e tre.")]
    public List<EnergyType> sourceEnergyTypes = new();

    [Tooltip("Tipi di energia che la destinazione accetta. Vuoto = tutti e tre.")]
    public List<EnergyType> destEnergyTypes = new();

    public bool SourceEmits(EnergyType type) =>
        sourceEnergyTypes.Count == 0 || sourceEnergyTypes.Contains(type);

    public bool DestAccepts(EnergyType type) =>
        destEnergyTypes.Count == 0 || destEnergyTypes.Contains(type);

    [Tooltip("Distanza in pixel tra i nodi della mappa per questo livello")]
    public float nodeSpacing = 1200f;

    [Tooltip("Dimensione in pixel di ogni cella della griglia per questo livello")]
    public float cellSize = 80f;

    public bool IsCellActive(Vector2Int coord) => !inactiveCells.Contains(coord);
}