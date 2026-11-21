using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

[Serializable]
public class StoryMoodRange
{
    public string label;
    [Min(1)] public int firstPage = 1;
    [Min(1)] public int lastPage = 1;

    [Tooltip("Snapshot for this part. Empty = look up 'Snapshot Name' in the mixer.")]
    public AudioMixerSnapshot snapshot;

    [Tooltip("Name of the snapshot in the AudioMixer (used when Snapshot is empty).")]
    public string snapshotName;

    public bool Contains(int page)
    {
        return page >= firstPage && page <= lastPage;
    }
}

/// <summary>
/// Blends the AudioMixer to the mood of the part being read (Sadness, Happiness, Suspense...).
///
/// Interactive (scrolling) reader: the whole book is one text in a ScrollRect. The page is found from the
/// line at the reading line of the viewport, counting non-empty text lines (Lines Per Page each).
/// Page-by-page reader: the page comes from AIReadingController.CurrentPageIndex.
///
/// Put it on any object (e.g. "audio"). Everything is found automatically; only the mixer is needed
/// (here or on StoryAudioManager).
/// </summary>
public class StoryMoodController : MonoBehaviour
{
    public static StoryMoodController Instance { get; private set; }

    [Header("Book")]
    [Tooltip("Only this book changes the mood. Empty = every book.")]
    public string bookResourcePath = "Books/book_emotion_results_unity";
    [Tooltip("Found automatically.")]
    public AIReadingController reader;

    [Header("Scrolling reader (found automatically)")]
    public ScrollRect scrollRect;
    public TMP_Text storyText;
    [Tooltip("Where the reading line sits in the viewport: 0 = top, 0.5 = middle, 1 = bottom.")]
    [Range(0f, 1f)] public float focusLine = 0.5f;
    [Tooltip("Non-empty text lines per page in the scrolling book text.")]
    [Min(1)] public int linesPerPage = 4;

    [Header("Mixer")]
    [Tooltip("Empty = use the mixer set on StoryAudioManager.")]
    public AudioMixer mixer;
    [Tooltip("Seconds a new part must stay at the reading line before the mood changes (ReadingSpeedSettings overrides it).")]
    public float holdTime = 0.6f;
    [Tooltip("Seconds the mixer takes to blend to the new mood (ReadingSpeedSettings overrides it).")]
    public float transitionTime = 2.5f;
    [Tooltip("Snapshot used for other books or when the reader is closed. Leave empty to keep the last mood.")]
    public string defaultSnapshotName = "";

    [Header("Parts")]
    public List<StoryMoodRange> moods = DefaultMoods();

    [Header("Debug")]
    public bool logChanges = true;

    public int CurrentPage { get; private set; } = -1;
    public StoryMoodRange CurrentMood { get; private set; }
    public event Action<int, StoryMoodRange> MoodChanged;

    private int pendingMoodIndex = -1;
    private float pendingSince;
    private int currentMoodIndex = -1;
    private bool appliedDefault;

    private string cachedText;
    private readonly List<int> lineStarts = new List<int>();
    private readonly Vector3[] corners = new Vector3[4];

    private static List<StoryMoodRange> DefaultMoods()
    {
        return new List<StoryMoodRange>
        {
            new StoryMoodRange { label = "Part 1 - Sadness",        firstPage = 1,  lastPage = 3,  snapshotName = "Sad" },
            new StoryMoodRange { label = "Part 2 - Happiness",      firstPage = 4,  lastPage = 6,  snapshotName = "Happy" },
            new StoryMoodRange { label = "Part 3 - Suspense",       firstPage = 7,  lastPage = 9,  snapshotName = "Suspense" },
            new StoryMoodRange { label = "Part 4 - Sadness",        firstPage = 10, lastPage = 12, snapshotName = "Sad" },
            new StoryMoodRange { label = "Part 5 - Hope",           firstPage = 13, lastPage = 16, snapshotName = "Hope" },
            new StoryMoodRange { label = "Part 6 - Joy",            firstPage = 17, lastPage = 19, snapshotName = "Joy" },
            new StoryMoodRange { label = "Part 7 - A Happy Ending", firstPage = 20, lastPage = 24, snapshotName = "Happy Ending" },
        };
    }

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (reader == null) reader = FindFirstObjectByType<AIReadingController>();

        if (!IsReadingThisBook())
        {
            LeaveBook();
            return;
        }

        int page = DetectPage();
        if (page < 1) return;
        CurrentPage = page;

        int moodIndex = MoodIndexForPage(page);
        if (moodIndex < 0 || moodIndex == currentMoodIndex)
        {
            pendingMoodIndex = -1;
            return;
        }

        if (currentMoodIndex < 0) // first mood when the book opens: no waiting
        {
            ApplyMood(moodIndex, true);
            return;
        }

        if (moodIndex != pendingMoodIndex)
        {
            pendingMoodIndex = moodIndex;
            pendingSince = Time.unscaledTime;
            return;
        }

        float hold = ReadingSpeedSettings.Instance != null
            ? ReadingSpeedSettings.Instance.Current.sectionHold
            : holdTime;

        if (Time.unscaledTime - pendingSince >= hold) ApplyMood(moodIndex, false);
    }

    // ---------- Which book / panel ----------

    private bool IsReadingThisBook()
    {
        if (reader == null) return true;

        GameObject panel = reader.ActiveReaderPanel;
        if (panel == null || !panel.activeInHierarchy) return false;

        return string.IsNullOrWhiteSpace(bookResourcePath) ||
               string.Equals(reader.jsonResourcePath, bookResourcePath.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private void LeaveBook()
    {
        currentMoodIndex = -1;
        pendingMoodIndex = -1;
        CurrentPage = -1;
        CurrentMood = null;

        if (appliedDefault || string.IsNullOrWhiteSpace(defaultSnapshotName)) return;

        AudioMixerSnapshot snap = FindSnapshot(defaultSnapshotName);
        if (snap != null)
        {
            Transition(snap, TransitionSeconds());
            appliedDefault = true;
        }
    }

    // ---------- Page detection ----------

    private int DetectPage()
    {
        bool interactive = reader == null ||
                           (reader.interactiveBookPagePanel != null &&
                            reader.ActiveReaderPanel == reader.interactiveBookPagePanel);

        if (interactive)
        {
            if (!FindScrollingText()) return -1;
            return PageAtReadingLine();
        }

        return reader.CurrentPageIndex + 1;
    }

    private bool FindScrollingText()
    {
        if (scrollRect != null && storyText != null && storyText.isActiveAndEnabled) return true;

        GameObject panel = reader != null ? reader.interactiveBookPagePanel : null;
        if (panel == null) return false;

        if (scrollRect == null) scrollRect = panel.GetComponentInChildren<ScrollRect>(true);
        if (scrollRect == null) return false;

        // The book text: the longest StoryText under the scroll view
        TMP_Text best = null;
        foreach (TMP_Text t in scrollRect.GetComponentsInChildren<TMP_Text>(true))
        {
            if (!t.name.StartsWith("StoryText", StringComparison.Ordinal)) continue;
            if (best == null || (t.text != null && best.text != null && t.text.Length > best.text.Length)) best = t;
        }
        storyText = best;
        return storyText != null && storyText.isActiveAndEnabled;
    }

    private int PageAtReadingLine()
    {
        TMP_TextInfo info = storyText.textInfo;
        if (info == null || info.lineCount == 0) return -1;

        Camera cam = null;
        Canvas canvas = storyText.canvas;
        if (canvas != null)
        {
            canvas = canvas.rootCanvas;
            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay) cam = canvas.worldCamera;
        }

        RectTransform viewport = scrollRect.viewport != null ? scrollRect.viewport : (RectTransform)scrollRect.transform;
        viewport.GetWorldCorners(corners);
        Vector3 bottomLeft = cam != null ? cam.WorldToScreenPoint(corners[0]) : corners[0];
        Vector3 topRight = cam != null ? cam.WorldToScreenPoint(corners[2]) : corners[2];
        Vector2 point = new Vector2(
            (bottomLeft.x + topRight.x) * 0.5f,
            Mathf.Lerp(topRight.y, bottomLeft.y, focusLine));

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(storyText.rectTransform, point, cam, out Vector2 local))
            return -1;

        // Line nearest to the reading line (in the text's local space)
        int bestLine = -1;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < info.lineCount; i++)
        {
            TMP_LineInfo line = info.lineInfo[i];
            if (line.characterCount == 0) continue;
            float top = line.ascender;
            float bottom = line.descender;
            float d = local.y > top ? local.y - top : (local.y < bottom ? bottom - local.y : 0f);
            if (d < bestDistance)
            {
                bestDistance = d;
                bestLine = i;
            }
        }
        if (bestLine < 0) return -1;

        int charIndex = info.lineInfo[bestLine].firstCharacterIndex;
        if (charIndex < 0 || charIndex >= info.characterCount) return -1;

        return PageOfStringIndex(info.characterInfo[charIndex].index);
    }

    private int PageOfStringIndex(int stringIndex)
    {
        string text = storyText.text ?? "";
        if (!ReferenceEquals(text, cachedText))
        {
            cachedText = text;
            lineStarts.Clear();
            int start = 0;
            while (start <= text.Length)
            {
                int end = text.IndexOf('\n', start);
                if (end < 0) end = text.Length;
                if (text.Substring(start, end - start).Trim().Length > 0) lineStarts.Add(start);
                start = end + 1;
            }
        }

        int lineNumber = 0; // non-empty lines that start at or before this character
        for (int i = 0; i < lineStarts.Count; i++)
        {
            if (lineStarts[i] <= stringIndex) lineNumber = i + 1;
            else break;
        }
        if (lineNumber == 0) lineNumber = 1;

        return (lineNumber - 1) / Mathf.Max(1, linesPerPage) + 1;
    }

    // ---------- Mood ----------

    private int MoodIndexForPage(int page)
    {
        for (int i = 0; i < moods.Count; i++)
        {
            if (moods[i] != null && moods[i].Contains(page)) return i;
        }
        return -1; // e.g. "The End." line after the last page: keep the current mood
    }

    private void ApplyMood(int index, bool immediate)
    {
        StoryMoodRange mood = moods[index];
        currentMoodIndex = index;
        pendingMoodIndex = -1;
        appliedDefault = false;
        CurrentMood = mood;

        AudioMixerSnapshot snap = mood.snapshot != null ? mood.snapshot : FindSnapshot(mood.snapshotName);
        if (snap != null)
        {
            Transition(snap, immediate ? 0.01f : TransitionSeconds());
        }
        else
        {
            Debug.LogWarning("[Mood] No snapshot '" + mood.snapshotName + "' for " + mood.label +
                             ". Create it in the AudioMixer or drag one into the Snapshot field.");
        }

        if (logChanges) Debug.Log("[Mood] Page " + CurrentPage + " -> " + mood.label);
        if (MoodChanged != null) MoodChanged(CurrentPage, mood);
    }

    private float TransitionSeconds()
    {
        if (ReadingSpeedSettings.Instance != null)
        {
            SpeedValues s = ReadingSpeedSettings.Instance.Current;
            return s.partFadeOut + s.partSilence;
        }
        return transitionTime;
    }

    private void Transition(AudioMixerSnapshot snap, float seconds)
    {
        if (StoryAudioManager.Instance != null) StoryAudioManager.Instance.ApplySnapshot(snap, seconds);
        else snap.TransitionTo(Mathf.Max(0.01f, seconds));
    }

    private AudioMixerSnapshot FindSnapshot(string snapshotName)
    {
        if (string.IsNullOrWhiteSpace(snapshotName)) return null;
        AudioMixer m = mixer;
        StoryAudioManager audio = StoryAudioManager.Instance;
        if (m == null && audio != null)
        {
            m = audio.mixer;
            if (m == null && audio.musicGroup != null) m = audio.musicGroup.audioMixer;
        }
        if (m == null) return null;

        string wanted = snapshotName.Trim();
        AudioMixerSnapshot snap = m.FindSnapshot(wanted);
        if (snap != null && string.Equals(snap.name, wanted, StringComparison.OrdinalIgnoreCase)) return snap;

        // Not found exactly: accept a snapshot named with one of the words, e.g. "Ending" for "Happy Ending"
        string[] words = wanted.Split(' ');
        for (int i = words.Length - 1; i >= 0; i--)
        {
            if (words.Length == 1 || words[i].Length == 0) break;
            AudioMixerSnapshot alt = m.FindSnapshot(words[i]);
            if (alt != null && string.Equals(alt.name, words[i], StringComparison.OrdinalIgnoreCase)) return alt;
        }
        return snap;
    }
}
