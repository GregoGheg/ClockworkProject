using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class LastSelectedDisplay : MonoBehaviour
{
    Image img;
    static LastSelectedDisplay instance;

    void Awake()
    {
        img = GetComponent<Image>();
        instance = this;
        img.color = Color.clear;
        Debug.Log($"[LastSelectedDisplay] Awake — instance impostato su {gameObject.name}");
    }

    public static void SetSprite(Sprite sprite)
    {
        Debug.Log($"[LastSelectedDisplay] SetSprite chiamato — instance={instance?.gameObject.name ?? "NULL"} sprite={sprite?.name ?? "NULL"}");
        if (instance == null) return;
        if (sprite == null)
        {
            instance.img.sprite = null;
            instance.img.color = Color.clear;
        }
        else
        {
            instance.img.sprite = sprite;
            instance.img.color = Color.white;
        }
    }
}