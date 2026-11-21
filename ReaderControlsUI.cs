using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Adds two buttons under every "GazeDotToggleButton" (Dot On/Off) in the reader panels:
///  - "Recalibrate": starts GazeCalibration,
///  - "Settings": opens the ReaderTuning panel (look time before a sound + music crossfade).
/// The buttons are copies of the Dot button, so they look the same. Creates itself; nothing to set up.
/// </summary>
public class ReaderControlsUI : MonoBehaviour
{
    [Tooltip("Hide ReaderTuning's own bottom-left Settings button, since this one is next to Dot On/Off.")]
    public bool hideBottomSettingsButton = true;

    private bool built;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (FindFirstObjectByType<ReaderControlsUI>() != null) return;
        new GameObject("[ReaderControlsUI]").AddComponent<ReaderControlsUI>();
    }

    void Start()
    {
        Build();
    }

    void Update()
    {
        if (hideBottomSettingsButton && ReaderTuning.Instance != null) ReaderTuning.Instance.showButton = false;
    }

    public void Recalibrate()
    {
        GazeCalibration cal = GazeCalibration.Instance != null ? GazeCalibration.Instance : FindFirstObjectByType<GazeCalibration>();
        if (cal == null)
        {
            Debug.LogWarning("[ReaderControls] No GazeCalibration in the scene.");
            return;
        }
        if (ReaderTuning.Instance != null) ReaderTuning.Instance.SetPanelOpen(false);
        cal.StartCalibration();
    }

    public void ToggleSettings()
    {
        if (ReaderTuning.Instance != null) ReaderTuning.Instance.SetPanelOpen(!ReaderTuning.Instance.PanelOpen);
        else Debug.LogWarning("[ReaderControls] No ReaderTuning in the scene.");
    }

    private void Build()
    {
        if (built) return;
        built = true;

        foreach (Button dot in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (dot == null || dot.name != "GazeDotToggleButton" || dot.transform.parent == null) continue;
            if (dot.transform.parent.Find("RecalibrateButton") != null) continue;

            RectTransform dotRect = (RectTransform)dot.transform;
            float rowStep = dotRect.sizeDelta.y + 12f;

            // Recalibrate: directly under Dot On/Off
            Button recal = CloneButton(dot, "RecalibrateButton", "Recalibrate", new Vector2(0f, -rowStep));
            recal.onClick.AddListener(Recalibrate);

            // Settings: under Trigger On/Off if there is one, otherwise to the left of Recalibrate
            Transform trigger = dot.transform.parent.Find("TriggerBoxesToggleButton");
            Vector2 offset = trigger != null
                ? ((RectTransform)trigger).anchoredPosition - dotRect.anchoredPosition + new Vector2(0f, -rowStep)
                : new Vector2(-(dotRect.sizeDelta.x + 18f), -rowStep);
            Button settings = CloneButton(dot, "SettingsButton", "Settings", offset);
            settings.onClick.AddListener(ToggleSettings);
        }
    }

    private static Button CloneButton(Button source, string newName, string label, Vector2 offset)
    {
        GameObject go = Instantiate(source.gameObject, source.transform.parent);
        go.name = newName;
        go.SetActive(true);

        RectTransform rt = (RectTransform)go.transform;
        rt.anchoredPosition = ((RectTransform)source.transform).anchoredPosition + offset;

        Button b = go.GetComponent<Button>();
        b.onClick = new Button.ButtonClickedEvent(); // drop the copied "Toggle Dot" call

        TMP_Text tmp = go.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null) tmp.text = label;
        Text legacy = go.GetComponentInChildren<Text>(true);
        if (legacy != null) legacy.text = label;
        return b;
    }
}
