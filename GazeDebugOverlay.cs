using UnityEngine;

/// <summary>
/// Draws the gaze dot, every region's box (green = being looked at) and a status text.
/// Tick/untick "Show" in the Inspector. Uses OnGUI, so no Canvas setup is needed.
/// </summary>
public class GazeDebugOverlay : MonoBehaviour
{
    public bool show = true;
    public bool drawRegions = true;
    public bool showGazeDot = true;
    public bool showStatusPanel = true;
    public float dotSize =  100f;

    [Tooltip("OnGUI units per screen pixel. Leave at 1. If the dot and boxes look offset or too small/large on a Retina screen, try 0.5.")]
    public float guiScale = 1f;

    private GUIStyle labelStyle;

    public void ToggleGazeDot()
    {
        showGazeDot = !showGazeDot;
    }

    public void ToggleGazeRegions()
    {
        drawRegions = !drawRegions;
    }

    void OnGUI()
    {
        float k = Mathf.Max(0.01f, guiScale);
        if (!show || Event.current.type != EventType.Repaint) return;

        GazeInputMapper mapper = GazeInputMapper.Instance;
        GazeDwellManager mgr = GazeDwellManager.Instance;
        BeamGazeProvider beam = BeamGazeProvider.Instance;

        // OnGUI may use different units than the mapper's pixels; guiScale converts if needed
        float h = Screen.height;
        Texture2D tex = Texture2D.whiteTexture;

        if (drawRegions)
        {
            var list = GazeRegion.Active;
            for (int i = 0; i < list.Count; i++)
            {
                if (!list[i].IsInViewport()) continue;
                Rect s = list[i].GetScreenRect();
                Rect g = new Rect(s.xMin * k, (h - s.yMax) * k, s.width * k, s.height * k);
                bool current = mgr != null && mgr.Current == list[i];
                DrawBox(g, current ? Color.green : new Color(1f, 0.9f, 0f, 0.8f), 2f);
                if (current)
                {
                    GUI.color = new Color(0f, 1f, 0f, 0.6f);
                    GUI.DrawTexture(new Rect(g.x, g.yMax - 6f, g.width * list[i].Progress, 6f), tex);
                }
            }
        }

        if (showGazeDot && mapper != null && mapper.IsValid)
        {
            Vector2 p = mapper.ScreenPosition;
            GUI.color = new Color(1f, 0.1f, 0.1f, 0.9f);
            GUI.DrawTexture(new Rect(p.x * k - dotSize * 0.5f, (h - p.y) * k - dotSize * 0.5f, dotSize, dotSize), tex);
        }

        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = Color.white } };
        }

        if (showStatusPanel)
        {
            string text =
                "Beam: " + (beam != null ? beam.Status.ToString() : "no BeamGazeProvider") +
                "   data: " + (beam != null && beam.HasData) + "\n" +
                "Screen: " + Screen.width + "x" + Screen.height +
                "   display: " + (beam != null ? beam.ViewportSize.x + "x" + beam.ViewportSize.y : "-") +
                "   gaze px: " + (mapper != null && mapper.IsValid ? mapper.ScreenPosition.ToString("F0") : "-") + "\n" +
                "Calibrated: " + (mapper != null && mapper.IsCalibrated) +
                "   norm: " + (mapper != null ? mapper.RawNormalized.ToString("F2") : "-") +
                "   beam px: " + (beam != null ? beam.User.UnifiedScreenGaze.PointOfRegard.X + "," + beam.User.UnifiedScreenGaze.PointOfRegard.Y : "-") + "\n" +
                "Region: " + (mgr != null && mgr.Current != null ? mgr.Current.name + "  " + (mgr.Current.Progress * 100f).ToString("F0") + "%" : "-") +
                "   section: " + (StoryScrollManager.Instance != null ? StoryScrollManager.Instance.ActiveIndex.ToString() : "-") +
                "   page: " + (StoryMoodController.Instance != null && StoryMoodController.Instance.CurrentPage > 0 ? StoryMoodController.Instance.CurrentPage.ToString() : "-") +
                "   mood: " + (StoryMoodController.Instance != null && StoryMoodController.Instance.CurrentMood != null ? StoryMoodController.Instance.CurrentMood.label : "-");

            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(new Rect(8f, 8f, 1100f, 100f), tex);
            GUI.color = Color.white;
            GUI.Label(new Rect(16f, 12f, 1080f, 94f), text, labelStyle);
        }
    }

    private static void DrawBox(Rect r, Color c, float t)
    {
        GUI.color = c;
        Texture2D tex = Texture2D.whiteTexture;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), tex);
        GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), tex);
        GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), tex);
        GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), tex);
        GUI.color = Color.white;
    }
}
