using UnityEngine;

/// <summary>
/// Two reader settings, adjustable in the Inspector or in the game:
///  - Look time: seconds of looking at a word before its sound plays (GazeDwellManager default dwell;
///    used by every gaze box whose own Dwell Time is -1, e.g. all StoryWordCues boxes).
///  - Crossfade: seconds the music takes to fade from one track into the next (StoryAudioManager Music Fade Time).
/// In the game press T (or the "Settings" button bottom-left) to open the panel. Values are remembered.
/// Creates itself if it is not in the scene.
/// </summary>
[DefaultExecutionOrder(100)] // after ReadingSpeedSettings, so these values win
public class ReaderTuning : MonoBehaviour
{
    public static ReaderTuning Instance { get; private set; }

    [Header("Values")]
    [Tooltip("Seconds of looking at a word before its sound plays.")]
    [Range(MinDwell, MaxDwell)] public float lookTime = 0.6f;
    [Tooltip("Seconds the music takes to crossfade into the next track.")]
    [Range(MinFade, MaxFade)] public float crossfade = 2.5f;

    [Header("In-game panel")]
    public bool showButton = true;
    public KeyCode toggleKey = KeyCode.T;
    public bool rememberValues = true;

    public const float MinDwell = 0.2f, MaxDwell = 2.5f;
    public const float MinFade = 0f, MaxFade = 8f;

    private const string DwellKey = "ReaderTuning.LookTime";
    private const string FadeKey = "ReaderTuning.Crossfade";

    private bool panelOpen;
    private GUIStyle label, title, button;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (FindFirstObjectByType<ReaderTuning>() != null) return;
        GameObject go = new GameObject("[ReaderTuning]");
        go.AddComponent<ReaderTuning>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        if (rememberValues)
        {
            if (PlayerPrefs.HasKey(DwellKey)) lookTime = PlayerPrefs.GetFloat(DwellKey);
            if (PlayerPrefs.HasKey(FadeKey)) crossfade = PlayerPrefs.GetFloat(FadeKey);
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void LateUpdate()
    {
        Apply();
        if (ReaderInput.GetKeyDownSafe(toggleKey)) panelOpen = !panelOpen;
    }

    public bool PanelOpen => panelOpen;

    public void SetPanelOpen(bool open)
    {
        panelOpen = open;
    }

    public void SetLookTime(float seconds)
    {
        lookTime = Mathf.Clamp(seconds, MinDwell, MaxDwell);
        Save();
    }

    public void SetCrossfade(float seconds)
    {
        crossfade = Mathf.Clamp(seconds, MinFade, MaxFade);
        Save();
    }

    private void Apply()
    {
        if (GazeDwellManager.Instance != null) GazeDwellManager.Instance.defaultDwellTime = lookTime;
        if (StoryAudioManager.Instance != null) StoryAudioManager.Instance.musicFadeTime = crossfade;
    }

    private void Save()
    {
        if (!rememberValues) return;
        PlayerPrefs.SetFloat(DwellKey, lookTime);
        PlayerPrefs.SetFloat(FadeKey, crossfade);
        PlayerPrefs.Save();
    }

    // ---------- In-game panel ----------

    void OnGUI()
    {
        float k = Screen.height / 1080f;
        Matrix4x4 old = GUI.matrix;
        GUI.matrix = Matrix4x4.Scale(new Vector3(k, k, 1f));
        float w = Screen.width / k;
        float h = 1080f;

        if (label == null)
        {
            label = new GUIStyle(GUI.skin.label) { fontSize = 22, normal = { textColor = Color.white } };
            title = new GUIStyle(label) { fontSize = 26, fontStyle = FontStyle.Bold };
            button = new GUIStyle(GUI.skin.button) { fontSize = 20 };
        }

        if (showButton && !panelOpen &&
            GUI.Button(new Rect(20f, h - 70f, 150f, 48f), "Settings (T)", button))
        {
            panelOpen = true;
        }

        if (panelOpen)
        {
            Rect r = new Rect(20f, h - 400f, 560f, 380f);
            GUI.color = new Color(0f, 0f, 0f, 0.8f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;

            float x = r.x + 24f, y = r.y + 18f, sw = r.width - 48f;
            GUI.Label(new Rect(x, y, sw, 36f), "Reading settings", title);
            y += 52f;

            GUI.Label(new Rect(x, y, sw, 30f), "Look time before a sound plays: " + lookTime.ToString("0.00") + " s", label);
            y += 36f;
            float d = GUI.HorizontalSlider(new Rect(x, y, sw, 24f), lookTime, MinDwell, MaxDwell);
            if (!Mathf.Approximately(d, lookTime)) SetLookTime(Mathf.Round(d * 20f) / 20f);
            y += 40f;

            GUI.Label(new Rect(x, y, sw, 30f), "Music crossfade: " + crossfade.ToString("0.0") + " s", label);
            y += 36f;
            float f = GUI.HorizontalSlider(new Rect(x, y, sw, 24f), crossfade, MinFade, MaxFade);
            if (!Mathf.Approximately(f, crossfade)) SetCrossfade(Mathf.Round(f * 10f) / 10f);
            y += 46f;

            float bw = (sw - 20f) / 3f;
            if (GUI.Button(new Rect(x, y, bw, 44f), "Slow", button)) { SetLookTime(0.9f); SetCrossfade(4f); }
            if (GUI.Button(new Rect(x + bw + 10f, y, bw, 44f), "Medium", button)) { SetLookTime(0.6f); SetCrossfade(2.5f); }
            if (GUI.Button(new Rect(x + 2f * (bw + 10f), y, bw, 44f), "Fast", button)) { SetLookTime(0.4f); SetCrossfade(1.5f); }
            y += 58f;

            if (GUI.Button(new Rect(x, y, sw, 44f), "Close (T)", button)) panelOpen = false;
        }

        GUI.matrix = old;
    }
}

/// <summary>Key check that works with either input system.</summary>
internal static class ReaderInput
{
    public static bool GetKeyDownSafe(KeyCode key)
    {
#if ENABLE_INPUT_SYSTEM
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return false;
        switch (key)
        {
            case KeyCode.T: return kb.tKey.wasPressedThisFrame;
            case KeyCode.F1: return kb.f1Key.wasPressedThisFrame;
            case KeyCode.Tab: return kb.tabKey.wasPressedThisFrame;
            default: return false;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        return UnityEngine.Input.GetKeyDown(key);
#else
        return false;
#endif
    }
}
