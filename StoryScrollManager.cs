using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

/// <summary>
/// For a story that scrolls up and down inside a ScrollRect (replaces StoryPageManager).
/// Each page is a section inside the Content. The section at the "reading line" of the viewport is the
/// active page; when it stays there for a moment, its PageAudioProfile takes over the music, mood and ambience.
/// A section without its own music or mood inherits the one above it, so scrolling back up restores them.
/// </summary>
public class StoryScrollManager : MonoBehaviour
{
    public static StoryScrollManager Instance { get; private set; }

    [Serializable]
    public class Section
    {
        public string label;
        [Tooltip("The page block inside the ScrollRect's Content (illustration + text + GazeRegions). Put the PageAudioProfile on it.")]
        public RectTransform root;
    }

    public ScrollRect scrollRect;
    public List<Section> sections = new List<Section>();

    [Tooltip("Where the reading line sits in the viewport: 0 = top edge, 0.5 = middle, 1 = bottom edge.")]
    [Range(0f, 1f)] public float focusLine = 0.5f;

    [Tooltip("Seconds a section must stay at the reading line before it takes over (used when there is no ReadingSpeedSettings).")]
    public float holdTime = 0.6f;

    [Tooltip("Fade out ambient sounds when the section has no PageAudioProfile.")]
    public bool stopAmbientWithoutProfile = true;

    public int ActiveIndex { get; private set; } = -1;
    public event Action<int> SectionChanged;

    private int pendingIndex = -1;
    private float pendingSince;
    private readonly Vector3[] corners = new Vector3[4];

    void Awake()
    {
        Instance = this;
    }

    void Update()
    {
        if (scrollRect == null || sections.Count == 0) return;

        int candidate = FindSectionAtReadingLine();
        if (candidate < 0) return;

        if (candidate == ActiveIndex)
        {
            pendingIndex = -1;
            return;
        }

        if (ActiveIndex < 0) // first section: start right away
        {
            Activate(candidate);
            return;
        }

        if (candidate != pendingIndex)
        {
            pendingIndex = candidate;
            pendingSince = Time.unscaledTime;
            return;
        }

        float hold = ReadingSpeedSettings.Instance != null
            ? ReadingSpeedSettings.Instance.Current.sectionHold
            : holdTime;

        if (Time.unscaledTime - pendingSince >= hold) Activate(candidate);
    }

    private int FindSectionAtReadingLine()
    {
        RectTransform viewport = scrollRect.viewport != null ? scrollRect.viewport : (RectTransform)scrollRect.transform;

        Camera cam = null;
        Canvas canvas = scrollRect.GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            canvas = canvas.rootCanvas;
            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) cam = canvas.worldCamera;
        }

        Rect vp = ScreenRect(viewport, cam);
        if (vp.height <= 0f) return -1; // layout not ready yet
        float y = vp.yMax - focusLine * vp.height;

        int best = -1;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < sections.Count; i++)
        {
            if (sections[i].root == null) continue;
            Rect r = ScreenRect(sections[i].root, cam);
            float d = 0f;
            if (y < r.yMin) d = r.yMin - y;
            else if (y > r.yMax) d = y - r.yMax;

            if (d < bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }
        return best;
    }

    private Rect ScreenRect(RectTransform rt, Camera cam)
    {
        rt.GetWorldCorners(corners);
        if (cam != null)
        {
            for (int i = 0; i < 4; i++) corners[i] = cam.WorldToScreenPoint(corners[i]);
        }
        return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
    }

    private PageAudioProfile ProfileOf(int index)
    {
        return sections[index].root != null ? sections[index].root.GetComponent<PageAudioProfile>() : null;
    }

    private void Activate(int index)
    {
        bool changed = ActiveIndex >= 0;
        ActiveIndex = index;
        pendingIndex = -1;

        // Looking at this page again: its words can fire again
        if (sections[index].root != null)
        {
            foreach (GazeRegion r in sections[index].root.GetComponentsInChildren<GazeRegion>(true)) r.ResetState();
        }

        // Music and mood: the nearest page at or above this one that defines them
        AudioClip music = null;
        float volume = 1f;
        AudioMixerSnapshot snapshot = null;
        bool haveMusic = false;
        bool haveSnapshot = false;
        for (int i = index; i >= 0 && !(haveMusic && haveSnapshot); i--)
        {
            PageAudioProfile p = ProfileOf(i);
            if (p == null) continue;
            if (!haveMusic && p.music != null)
            {
                music = p.music;
                volume = p.musicVolume;
                haveMusic = true;
            }
            if (!haveSnapshot && p.snapshot != null)
            {
                snapshot = p.snapshot;
                haveSnapshot = true;
            }
        }

        PageAudioProfile.Apply(ProfileOf(index), changed, music, volume, snapshot, stopAmbientWithoutProfile);

        if (SectionChanged != null) SectionChanged(index);
    }
}
