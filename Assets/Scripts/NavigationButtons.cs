using UnityEngine;
using UnityEngine.UI;

public class NavigationButtons : MonoBehaviour
{
    public WorldNavigator navigator;

    public Button btnLeft;
    public Button btnRight;
    public Button btnUp;
    public Button btnDown;

    void Start()
    {
        if (btnLeft) btnLeft.onClick.AddListener(() => navigator.TryNavigate(Vector2Int.left));
        if (btnRight) btnRight.onClick.AddListener(() => navigator.TryNavigate(Vector2Int.right));
        if (btnUp) btnUp.onClick.AddListener(() => navigator.TryNavigate(Vector2Int.up));
        if (btnDown) btnDown.onClick.AddListener(() => navigator.TryNavigate(Vector2Int.down));
    }

    void Update()
    {
        if (navigator == null || navigator.config == null) return;

        int cur = navigator.CurrentIndex;
        bool solved = navigator.IsSolved(cur);
        var curPos = navigator.config.levels[cur].mapPosition;

        // Una freccia è visibile se:
        // - Esiste un livello adiacente in quella direzione, E
        // - Il livello corrente è risolto OPPURE il livello adiacente è già stato visitato/risolto
        if (btnLeft) btnLeft.gameObject.SetActive(CanNavigate(curPos, Vector2Int.left, solved));
        if (btnRight) btnRight.gameObject.SetActive(CanNavigate(curPos, Vector2Int.right, solved));
        if (btnUp) btnUp.gameObject.SetActive(CanNavigate(curPos, Vector2Int.up, solved));
        if (btnDown) btnDown.gameObject.SetActive(CanNavigate(curPos, Vector2Int.down, solved));
    }

    bool CanNavigate(Vector2Int curPos, Vector2Int dir, bool currentSolved)
    {
        var targetPos = curPos + dir;
        for (int i = 0; i < navigator.config.levels.Length; i++)
        {
            if (navigator.config.levels[i].mapPosition != targetPos) continue;
            // Livello adiacente trovato —
            // visibile se il corrente è risolto OPPURE il target è già stato visitato
            return currentSolved || navigator.IsSolved(i) || navigator.HasBeenVisited(i);
        }
        return false;
    }
}