
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using TMPro;
using UnityEngine.UI;

public enum StoryAudioChannel
{
    Sfx,
    Voice,
    Ambient,
    Music,
    Stinger   // short musical jingle; music and ambience duck under it
}

public enum RetriggerMode
{
    OnlyAfterLookingAway,
    WhileLooking,
    Once
}

[Serializable]
public class StoryAudioCue
{
    public AudioClip clip;
    public StoryAudioChannel channel = StoryAudioChannel.Sfx;

    [Range(0f, 1f)]
    public float volume = 1f;

    [Tooltip("Lower the music while this sound plays (Voice and Stinger cues always lower it).")]
    public bool duckMusic = false;

    [Tooltip("Lower the ambient loops while this sound effect plays (e.g. a wind effect over a wind loop).")]
    public bool duckAmbient = false;
}

[Serializable]
public class GazeWordEvent : UnityEvent<string>
{
}

[RequireComponent(typeof(RectTransform))]
public class GazeRegion : MonoBehaviour
{
    public static readonly List<GazeRegion> Active =
        new List<GazeRegion>();

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Active.Clear();
    }

    [Header("Paragraph Dwell")]
    [Tooltip("Seconds of looking needed. -1 uses the manager default.")]
    public float dwellTime = -1f;

    public RetriggerMode retrigger =
        RetriggerMode.OnlyAfterLookingAway;

    [Tooltip("Extra seconds before repeating while looking.")]
    public float repeatInterval = 2f;

    [Header("Word Detection")]
    [Tooltip("Time needed to trigger a word event.")]
    public float wordDwellTime = 0.35f;

    [Tooltip("Optional TMP text. If empty, this object is checked automatically.")]
    public TMP_Text textTarget;

    [Header("Hit Area")]
    [Tooltip("Extra margin around the RectTransform in UI units.")]
    public Vector2 padding = new Vector2(20f, 20f);

    [Tooltip("Higher priority wins when regions overlap.")]
    public int priority = 0;

    [Header("Audio")]
    public List<StoryAudioCue> audioCues =
        new List<StoryAudioCue>();

    [Header("Paragraph Events")]
    public UnityEvent onGazeEnter = new UnityEvent();
    public UnityEvent onDwellComplete = new UnityEvent();
    public UnityEvent onGazeExit = new UnityEvent();

    [Header("Word Events")]
    public GazeWordEvent onWordEnter = new GazeWordEvent();
    public GazeWordEvent onWordExit = new GazeWordEvent();
    public GazeWordEvent onWordDwellComplete = new GazeWordEvent();

    public float Progress { get; internal set; }

    public string CurrentWordText { get; private set; }

    public int CurrentWordIndex { get; private set; } = -1;

    public float WordProgress { get; private set; }

    internal float timer;
    internal bool triggeredThisLook;
    internal bool usedOnce;

    private RectTransform rect;
    private Canvas canvas;

    // Set when the region is inside a ScrollRect: only the visible part counts.
    private RectTransform viewport;
    private readonly Vector3[] viewportCorners = new Vector3[4];

    private readonly Vector3[] corners = new Vector3[4];

    private float wordTimer;
    private bool wordTriggered;

    private Camera EventCamera
    {
        get
        {
            if (canvas != null &&
                canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                return canvas.worldCamera;
            }

            return null;
        }
    }

    public Camera GetEventCamera()
    {
        return EventCamera;
    }

    public float Area
    {
        get
        {
            if (rect == null)
                return float.MaxValue;

            return Mathf.Abs(
                rect.rect.width *
                rect.rect.height *
                transform.lossyScale.x *
                transform.lossyScale.y
            );
        }
    }

    private void OnEnable()
    {
        rect = GetComponent<RectTransform>();

        Canvas c = GetComponentInParent<Canvas>();

        canvas = c != null ? c.rootCanvas : null;

        ScrollRect scroll = GetComponentInParent<ScrollRect>();
        viewport = scroll != null
            ? (scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform)
            : null;

        if (textTarget == null)
            textTarget = GetComponent<TMP_Text>();

        ResetState();

        if (!Active.Contains(this))
            Active.Add(this);
    }

    private void OnDisable()
    {
        ResetWord(-1, "");

        Active.Remove(this);
    }

    public bool Contains(Vector2 screenPoint)
    {
        if (rect == null)
            return false;

        // Inside a ScrollRect, words scrolled out of view cannot be hit.
        if (viewport != null &&
            !RectTransformUtility.RectangleContainsScreenPoint(
                viewport,
                screenPoint,
                EventCamera))
        {
            return false;
        }

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rect,
                screenPoint,
                EventCamera,
                out Vector2 local))
        {
            return false;
        }

        Rect rawRect = rect.rect;
        // RectTransform can report a negative width or height when a gaze
        // box uses stretched anchors with offsets larger than its parent.
        // Rect.Contains rejects those inverted bounds, making later page
        // trigger groups appear active but impossible to hit.
        Rect r = Rect.MinMaxRect(
            Mathf.Min(rawRect.xMin, rawRect.xMax),
            Mathf.Min(rawRect.yMin, rawRect.yMax),
            Mathf.Max(rawRect.xMin, rawRect.xMax),
            Mathf.Max(rawRect.yMin, rawRect.yMax)
        );

        r.xMin -= padding.x;
        r.xMax += padding.x;
        r.yMin -= padding.y;
        r.yMax += padding.y;

        return r.Contains(local);
    }

    public Rect GetScreenRect()
    {
        if (rect == null)
            return new Rect();

        rect.GetWorldCorners(corners);

        Camera cam = EventCamera;

        if (cam != null)
        {
            for (int i = 0; i < 4; i++)
            {
                corners[i] =
                    cam.WorldToScreenPoint(corners[i]);
            }
        }

        float minX = corners[0].x;
        float maxX = corners[0].x;
        float minY = corners[0].y;
        float maxY = corners[0].y;

        for (int i = 1; i < corners.Length; i++)
        {
            minX = Mathf.Min(minX, corners[i].x);
            maxX = Mathf.Max(maxX, corners[i].x);
            minY = Mathf.Min(minY, corners[i].y);
            maxY = Mathf.Max(maxY, corners[i].y);
        }

        return Rect.MinMaxRect(minX, minY, maxX, maxY);
    }

    /// <summary>False when the region is scrolled completely out of the visible area.</summary>
    public bool IsInViewport()
    {
        if (viewport == null || rect == null)
            return true;

        Rect region = GetScreenRect();

        viewport.GetWorldCorners(viewportCorners);

        Camera cam = EventCamera;

        if (cam != null)
        {
            for (int i = 0; i < 4; i++)
            {
                viewportCorners[i] =
                    cam.WorldToScreenPoint(viewportCorners[i]);
            }
        }

        Rect view = Rect.MinMaxRect(
            Mathf.Min(viewportCorners[0].x, viewportCorners[2].x),
            Mathf.Min(viewportCorners[0].y, viewportCorners[2].y),
            Mathf.Max(viewportCorners[0].x, viewportCorners[2].x),
            Mathf.Max(viewportCorners[0].y, viewportCorners[2].y)
        );

        return region.Overlaps(view);
    }

    public void ResetState()
    {
        ResetLookState();
        usedOnce = false;
    }

    internal void ResetLookState()
    {
        timer = 0f;
        Progress = 0f;
        triggeredThisLook = false;

        ResetWord(-1, "");
    }

    internal void Enter()
    {
        ResetLookState();
        onGazeEnter.Invoke();
    }

    internal void Exit()
    {
        ResetWord(-1, "");
        ResetLookState();

        onGazeExit.Invoke();
    }

    internal void UpdateWordDetection(
        int wordIndex,
        string wordText,
        float deltaTime)
    {
        if (wordIndex != CurrentWordIndex)
        {
            ResetWord(wordIndex, wordText);
        }

        if (wordIndex < 0)
            return;

        if (wordTriggered)
            return;

        wordTimer += deltaTime;

        WordProgress = wordDwellTime > 0f
            ? Mathf.Clamp01(wordTimer / wordDwellTime)
            : 1f;

        if (wordTimer >= wordDwellTime)
        {
            wordTriggered = true;

            Debug.Log(
                "[Gaze Word] Dwell complete: " +
                CurrentWordText
            );

            onWordDwellComplete.Invoke(CurrentWordText);
        }
    }

    private void ResetWord(int wordIndex, string wordText)
    {
        if (CurrentWordIndex >= 0 &&
            CurrentWordIndex != wordIndex)
        {
            onWordExit.Invoke(CurrentWordText);
        }

        bool changed = CurrentWordIndex != wordIndex;

        CurrentWordIndex = wordIndex;
        CurrentWordText = wordText;

        wordTimer = 0f;
        WordProgress = 0f;
        wordTriggered = false;

        if (wordIndex >= 0 && changed)
        {
            Debug.Log(
                "[Gaze Word] Looking at: " + wordText
            );

            onWordEnter.Invoke(wordText);
        }
    }

    /// <returns>
    /// False if every sound was held back by the audio rules (too soon after
    /// another effect, a stinger playing...). The manager then tries again shortly.
    /// </returns>
    internal bool Fire()
    {
        StoryAudioManager audio = StoryAudioManager.Instance;

        bool hasSound = false;
        bool played = false;

        if (audio != null)
        {
            for (int i = 0; i < audioCues.Count; i++)
            {
                if (audioCues[i] == null || audioCues[i].clip == null)
                    continue;

                hasSound = true;

                if (audio.Play(audioCues[i]))
                    played = true;
            }

            if (hasSound && !played)
                return false;
        }
        else if (audioCues.Count > 0)
        {
            Debug.LogWarning(
                "[Gaze] '" + name +
                "' has audio cues but there is no StoryAudioManager."
            );
        }

        Debug.Log("[Gaze Paragraph] Dwell complete: " + name);

        onDwellComplete.Invoke();

        return true;
    }
}
