using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attacca su un GameObject con Image (es. "UltimoPezzo").
/// Viene aggiornato da PieceDragger.Select() ogni volta che si seleziona un pezzo.
/// </summary>
[RequireComponent(typeof(Image))]
public class LastSelectedDisplay : MonoBehaviour
{
    Image img;

    static LastSelectedDisplay instance;

    void Awake()
    {
        img = GetComponent<Image>();
        instance = this;
        img.color = Color.clear; // nascosto finché nessun pezzo è selezionato
    }

    /// <summary>Chiamato da PieceDragger.Select() con lo sprite del pezzo.</summary>
    public static void SetSprite(Sprite sprite)
    {
        if (instance == null) return;
        if (sprite == null)
        {
            instance.img.sprite = null;
            instance.img.color  = Color.clear;
        }
        else
        {
            instance.img.sprite = sprite;
            instance.img.color  = Color.white;
        }
    }
}
