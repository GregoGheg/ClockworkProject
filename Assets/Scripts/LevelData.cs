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

    [System.Serializable]
    public struct PieceEntry
    {
        public PieceData data;
        [Min(1)] public int quantity;
    }

    public List<PieceEntry> availablePieces = new();

    public bool IsCellActive(Vector2Int coord) => !inactiveCells.Contains(coord);
}