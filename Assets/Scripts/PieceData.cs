using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "GearPuzzle/PieceData")]
public class PieceData : ScriptableObject
{
    [System.Flags]
    public enum ConnectionSides
    {
        None = 0,
        Right = 1 << 0,
        Left = 1 << 1,
        Up = 1 << 2,
        Down = 1 << 3,
        All = Right | Left | Up | Down
    }

    [System.Serializable]
    public struct EnergyChannel
    {
        public EnergyType type;
        public ConnectionSides conductIn;
        public ConnectionSides conductOut;
        public float instability;
    }

    [System.Serializable]
    public struct CellDef
    {
        public Vector2Int localCoord;
        [Tooltip("Occupa fisicamente la griglia")]
        public bool occupiesSpace;
        [Tooltip("Canali energetici di questa cella (può averne più di uno)")]
        public List<EnergyChannel> energyChannels;
        [Tooltip("Sprite specifico per questa cella")]
        public Sprite overrideSprite;
        [Tooltip("Sprite per celle non-fisiche")]
        public Sprite nonPhysicalSprite;

        [Tooltip("Se true, questa cella è la cella di uscita della pompa — spara acqua nella direzione pumpOutDirection")]
        public bool isPumpCell;

        public PieceData.ConnectionSides conductIn
        {
            get
            {
                var r = ConnectionSides.None;
                if (energyChannels != null)
                    foreach (var ch in energyChannels) r |= ch.conductIn;
                return r;
            }
        }

        public PieceData.ConnectionSides conductOut
        {
            get
            {
                var r = ConnectionSides.None;
                if (energyChannels != null)
                    foreach (var ch in energyChannels) r |= ch.conductOut;
                return r;
            }
        }

        public EnergyChannel? GetChannel(EnergyType type)
        {
            if (energyChannels == null) return null;
            foreach (var ch in energyChannels)
                if (ch.type == type) return ch;
            return null;
        }
    }

    [Tooltip("Sprite condiviso tra tutte le celle fisiche del pezzo")]
    public Sprite pieceSprite;
    [Tooltip("Scala sprite a 0° e 180°")]
    public Vector2 pieceSpriteScale = Vector2.one;
    [Tooltip("Scala sprite a 90° e 270°")]
    public Vector2 pieceSpriteScaleRotated = Vector2.one;
    [Tooltip("Offset sprite")]
    public Vector2 pieceSpriteOffset = Vector2.zero;

    [Header("Feedback piazzamento")]
    [Tooltip("Suono riprodotto quando il pezzo viene piazzato sulla griglia")]
    public AudioClip placeSound;
    [Tooltip("Volume del suono di piazzamento")]
    [Range(0f, 1f)] public float placeSoundVolume = 1f;

    public List<CellDef> cells = new();
    public Color color = Color.white;

    [Tooltip("Colore riga inventario (alpha=0 usa color)")]
    public Color trayRowColor = new Color(0f, 0f, 0f, 0f);

    public bool resizable = false;

    [Tooltip("Se true, il flusso idrico che passa per questo pezzo può salire senza limitazioni")]
    public bool isPump = false;

    [Tooltip("Direzione di sparo della pompa (ruota con il pezzo). Configurare anche isPumpCell sulla cella di uscita.")]
    public PieceData.ConnectionSides pumpOutDirection = ConnectionSides.None;

    [Tooltip("Se true, questo pezzo converte qualsiasi energia in ingresso in energia generica")]
    public bool isConverter = false;

    // ── Convertitore tipizzato ───────────────────────────────────────────
    [Header("Convertitore tipizzato")]
    [Tooltip("Se true, converte uno SPECIFICO tipo di energia in un altro. " +
             "Configura sotto IN e OUT (6 combinazioni possibili con lo stesso schema). " +
             "IMPORTANTE: la cella del pezzo deve avere due canali energia: " +
             "uno di tipo = converterInputType con i lati di INGRESSO in conductIn, " +
             "e uno di tipo = converterOutputType con i lati di USCITA in conductOut.")]
    public bool isTypedConverter = false;

    [Tooltip("Tipo di energia che il convertitore RICEVE")]
    public EnergyType converterInputType = EnergyType.Mechanical;

    [Tooltip("Tipo di energia che il convertitore EMETTE")]
    public EnergyType converterOutputType = EnergyType.Electric;

    [Tooltip("Se true, questo pezzo è un ingranaggio meccanico che ruota on/off")]
    public bool isGear = false;

    [Tooltip("Se true, è un ingranaggio grande — ruota più lentamente")]
    public bool isLarge = false;

    [Tooltip("Se true, è un collettore elettrico che attira energia in linea retta")]
    public bool isCollector = false;

    [Tooltip("Se true, è una cinghia meccanica che collega due ingranaggi (distanza 1, 8 direzioni)")]
    public bool isBelt = false;

    [Tooltip("Direzione OUT della pompa idrica (una sola)")]

    // ── Baule ────────────────────────────────────────────────────────────
    [System.Serializable]
    public class ChestReward
    {
        public PieceData data;
        [Min(1)] public int quantity = 1;
    }

    [Tooltip("Se true, questo pezzo è un baule che sblocca pezzi quando riceve l'energia giusta")]
    public bool isChest = false;

    [Tooltip("Tipo di energia richiesta per aprire il baule")]
    public EnergyType chestRequiredEnergy = EnergyType.Mechanical;

    [Tooltip("Lato da cui il baule deve ricevere l'energia")]
    public ConnectionSides chestInputSide = ConnectionSides.Down;

    [Tooltip("Pezzi sbloccati quando il baule viene aperto")]
    public List<ChestReward> chestRewards = new();

    public Color GetTrayColor() =>
        trayRowColor.a > 0f ? trayRowColor : color;

    public Sprite GetSprite(CellDef cell) =>
        cell.overrideSprite != null ? cell.overrideSprite : pieceSprite;
}