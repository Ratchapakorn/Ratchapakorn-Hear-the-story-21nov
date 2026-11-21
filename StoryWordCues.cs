using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[Serializable]
public class PageMusicEntry
{
    [Min(1)] public int page = 1;
    public AudioClip clip;
    [Range(0f, 1f)] public float volume = 1f;
    [Tooltip("Part of the file name. Empty Clip fields are filled from it automatically (Editor only).")]
    public string clipName;
    public string note;
}

[Serializable]
public class WordCueEntry
{
    [Min(1)] public int page = 1;
    [Tooltip("Words exactly as they appear in the story text (case does not matter).")]
    public string phrase;
    public StoryAudioChannel channel = StoryAudioChannel.Sfx;
    public AudioClip clip;
    [Range(0f, 1f)] public float volume = 1f;
    public bool duckMusic;
    public bool duckAmbient;
    [Tooltip("Seconds of looking needed. -1 = the dwell manager's default (reading speed).")]
    public float dwellTime = -1f;
    [Tooltip("Part of the file name. Empty Clip fields are filled from it automatically (Editor only).")]
    public string clipName;
    public string note;
}

/// <summary>
/// For the scrolling Little Star book (whole story in one text):
///  - Word cues: finds each phrase on its page and places a GazeRegion box over it automatically.
///    The boxes are children of the text, so they scroll with it. Entries without a clip still get a box.
///  - Page music: when a page is reached its music starts; pages without music keep the music above.
///    A change of mood (Part) fades through a short silence.
/// Needs StoryMoodController (it finds the page and the text). Put this on the "audio" object.
/// </summary>
public class StoryWordCues : MonoBehaviour
{
    [Header("Boxes")]
    public Vector2 boxPadding = new Vector2(12f, 8f);
    public int boxPriority = 100;
    [Tooltip("Lines per page in the book text (same as StoryMoodController).")]
    [Min(1)] public int linesPerPage = 4;

    [Header("Page music")]
    public bool playPageMusic = true;
    public bool stopMusicWhenLeavingBook = true;
    [Tooltip("Seconds a page must stay at the reading line before its music starts (ReadingSpeedSettings overrides it).")]
    public float holdTime = 0.6f;

    public List<PageMusicEntry> pageMusic = DefaultPageMusic();
    public List<WordCueEntry> wordCues = DefaultWordCues();

    private TMP_Text builtFor;
    private string builtText;
    private Vector2 builtSize;
    private int builtCharCount;
    private RectTransform container;

    private PageMusicEntry currentMusic;
    private string currentMusicMood;
    private PageMusicEntry pendingMusic;
    private float pendingSince;
    private bool wasReading;

    // ---------- Defaults from little_star_that_fell_off_the_sky.pdf ----------

    private static List<PageMusicEntry> DefaultPageMusic()
    {
        return new List<PageMusicEntry>
        {
            new PageMusicEntry { page = 1,  clipName = "ES_A Trace of Light", note = "Epidemic 6f30a607" },
            new PageMusicEntry { page = 3,  clipName = "cemetery-wind-467488", note = "Pixabay cemetery wind" },
            new PageMusicEntry { page = 4,  clipName = "ES_Nothing in Our Way", note = "Epidemic e67edbe7" },
            new PageMusicEntry { page = 5,  clipName = "ES_Little Yellow Petals", note = "Epidemic 7dc1e5f0" },
            new PageMusicEntry { page = 6,  clipName = "", note = "Epidemic 2b6ab4f5 (same track as page 17) - add file" },
            new PageMusicEntry { page = 7,  clipName = "", note = "Epidemic 7e8a44d2 - add file" },
            new PageMusicEntry { page = 8,  clipName = "forest-soundscape-night-time-403609", note = "Pixabay forest night" },
            new PageMusicEntry { page = 11, clipName = "forest-soundscape-night-time-403609", note = "Pixabay forest night" },
            new PageMusicEntry { page = 13, clipName = "", note = "Epidemic be1e7173 - add file" },
            new PageMusicEntry { page = 17, clipName = "", note = "Epidemic 2b6ab4f5 (same track as page 6) - add file" },
            new PageMusicEntry { page = 18, clipName = "", note = "Epidemic 78267352 - add file" },
            new PageMusicEntry { page = 20, clipName = "ES_Nothing in Our Way", note = "Epidemic e67edbe7 (same as page 4)" },
        };
    }

    private static List<WordCueEntry> DefaultWordCues()
    {
        return new List<WordCueEntry>
        {
            new WordCueEntry { page = 1,  phrase = "bright stars gleam", clipName = "shine-5-268908" },
            new WordCueEntry { page = 2,  phrase = "its light stayed so little, so faint and so low.", clipName = "shining-neural-network-26727" },
            new WordCueEntry { page = 3,  phrase = "wind came rushing", clipName = "strong-wind-68472", duckMusic = true, note = "Pixabay strong wind - add file" },
            new WordCueEntry { page = 4,  phrase = "landed in a puddle", clipName = "splash-6213" },
            new WordCueEntry { page = 5,  phrase = "fireflies", clipName = "shine-12-268905" },
            new WordCueEntry { page = 5,  phrase = "felt warm", channel = StoryAudioChannel.Stinger, clipName = "happy-positive-cheerful-music-for-kids-intro-272757" },
            new WordCueEntry { page = 6,  phrase = "it felt glad all the same.", channel = StoryAudioChannel.Stinger, clipName = "happy-outro-8110", note = "Pixabay happy outro - add file" },
            new WordCueEntry { page = 7,  phrase = "stars that glittered", clipName = "shine-5-268908" },
            new WordCueEntry { page = 8,  phrase = "branches swayed", clipName = "branch-drag-329004" },
            new WordCueEntry { page = 8,  phrase = "enormous came creeping around.", channel = StoryAudioChannel.Music, clipName = "", note = "Epidemic 04acd3ba (music change) - add file" },
            new WordCueEntry { page = 10, phrase = "flying left and right.", clipName = "flapping-39306" },
            new WordCueEntry { page = 11, phrase = "Past flowers and mushrooms and rocks in a heap.", clipName = "blooming-16-sec-edit-592342", duckMusic = true, note = "Pixabay blooming - add file" },
            new WordCueEntry { page = 12, phrase = "bright stars shone", clipName = "shine-5-268908" },
            new WordCueEntry { page = 13, phrase = "storm clouds rolled in", clipName = "loud-far-away-thunder-rumble-351537", duckMusic = true, note = "Pixabay thunder - add file" },
            new WordCueEntry { page = 14, phrase = "spread his wings", clipName = "flapping-39306" },
            new WordCueEntry { page = 16, phrase = "firefly saw it", clipName = "shine-5-268908" },
            new WordCueEntry { page = 17, phrase = "stars began shining all over the night", clipName = "shine-1-268902", note = "Pixabay shine 1 - add file" },
            new WordCueEntry { page = 18, phrase = "glow spread", channel = StoryAudioChannel.Stinger, clipName = "peaceful-light-ray-short-450966", note = "Pixabay peaceful light ray - add file" },
            new WordCueEntry { page = 19, phrase = "Up, up it floated", clipName = "deep-long-air-transition-311830", note = "Pixabay air transition - add file" },
        };
    }

    // ---------- Runtime ----------

    void Update()
    {
        StoryMoodController mood = StoryMoodController.Instance;
        if (mood == null) return;

        if (mood.CurrentPage < 1)
        {
            if (wasReading)
            {
                wasReading = false;
                pendingMusic = null;
                currentMusic = null;
                currentMusicMood = null;
                if (playPageMusic && stopMusicWhenLeavingBook && StoryAudioManager.Instance != null)
                    StoryAudioManager.Instance.StopMusic();
            }
            return;
        }

        bool firstFrame = !wasReading;
        wasReading = true;

        EnsureBoxes(mood.storyText);
        if (playPageMusic) UpdatePageMusic(mood.CurrentPage, mood.CurrentMood, firstFrame);
    }

    // ---------- Boxes ----------

    private void EnsureBoxes(TMP_Text text)
    {
        if (text == null || !text.isActiveAndEnabled) return;
        TMP_TextInfo info = text.textInfo;
        if (info == null || info.characterCount == 0) return;

        Vector2 size = text.rectTransform.rect.size;
        if (text == builtFor && string.Equals(text.text, builtText) && size == builtSize &&
            info.characterCount == builtCharCount && container != null)
            return;

        builtFor = text;
        builtText = text.text;
        builtSize = size;
        builtCharCount = info.characterCount;
        BuildBoxes(text);
    }

    private void BuildBoxes(TMP_Text text)
    {
        RectTransform textRect = text.rectTransform;

        Transform existing = textRect.Find("Auto Word Cues");
        if (existing != null)
        {
            container = (RectTransform)existing;
            for (int i = container.childCount - 1; i >= 0; i--) Destroy(container.GetChild(i).gameObject);
        }
        else
        {
            GameObject go = new GameObject("Auto Word Cues", typeof(RectTransform));
            container = (RectTransform)go.transform;
            container.SetParent(textRect, false);
        }
        container.anchorMin = Vector2.zero;
        container.anchorMax = Vector2.one;
        container.offsetMin = Vector2.zero;
        container.offsetMax = Vector2.zero;
        container.pivot = textRect.pivot;
        container.localScale = Vector3.one;

        string s = text.text ?? "";
        List<Vector2Int> lines = NonEmptyLines(s);

        int placed = 0;
        var notFound = new List<string>();
        var empty = new List<string>();

        foreach (WordCueEntry e in wordCues)
        {
            if (e == null || string.IsNullOrWhiteSpace(e.phrase)) continue;

            int first = (e.page - 1) * linesPerPage;
            int last = e.page * linesPerPage - 1;
            if (first >= lines.Count)
            {
                notFound.Add("p" + e.page + " \"" + e.phrase + "\"");
                continue;
            }
            int start = lines[first].x;
            int end = lines[Mathf.Min(last, lines.Count - 1)].y;

            int idx = s.IndexOf(e.phrase.Trim(), start, Mathf.Max(0, end - start), StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                notFound.Add("p" + e.page + " \"" + e.phrase + "\"");
                continue;
            }

            int count = CreateBoxesFor(text, e, idx, idx + e.phrase.Trim().Length);
            if (count > 0) placed++;
            else notFound.Add("p" + e.page + " \"" + e.phrase + "\"");
            if (e.clip == null) empty.Add("p" + e.page + " \"" + e.phrase + "\"");
        }

        Debug.Log("[WordCue] Placed boxes for " + placed + " phrase(s)." +
                  (notFound.Count > 0 ? " Not found: " + string.Join(", ", notFound) + "." : "") +
                  (empty.Count > 0 ? " Boxes without a sound yet: " + string.Join(", ", empty) + "." : ""));
    }

    private int CreateBoxesFor(TMP_Text text, WordCueEntry e, int from, int to)
    {
        TMP_TextInfo info = text.textInfo;
        var segments = new SortedDictionary<int, Rect>();

        for (int i = 0; i < info.characterCount; i++)
        {
            TMP_CharacterInfo ci = info.characterInfo[i];
            if (!ci.isVisible || ci.index < from || ci.index >= to) continue;

            Rect r = Rect.MinMaxRect(ci.bottomLeft.x, ci.descender, ci.topRight.x, ci.ascender);
            if (segments.TryGetValue(ci.lineNumber, out Rect old))
            {
                r = Rect.MinMaxRect(Mathf.Min(old.xMin, r.xMin), Mathf.Min(old.yMin, r.yMin),
                                    Mathf.Max(old.xMax, r.xMax), Mathf.Max(old.yMax, r.yMax));
            }
            segments[ci.lineNumber] = r;
        }

        int n = 0;
        foreach (KeyValuePair<int, Rect> seg in segments)
        {
            string boxName = "p" + e.page + " " + e.phrase + (segments.Count > 1 ? " (" + (n + 1) + ")" : "");
            GameObject go = new GameObject(boxName, typeof(RectTransform));
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(container, false);
            rt.anchorMin = container.pivot;
            rt.anchorMax = container.pivot;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = seg.Value.center;
            rt.sizeDelta = seg.Value.size;

            GazeRegion region = go.AddComponent<GazeRegion>();
            region.textTarget = text;
            region.dwellTime = e.dwellTime;
            region.padding = boxPadding;
            region.priority = boxPriority;
            region.retrigger = RetriggerMode.OnlyAfterLookingAway;
            region.audioCues = new List<StoryAudioCue>
            {
                new StoryAudioCue
                {
                    clip = e.clip,
                    channel = e.channel,
                    volume = e.volume,
                    duckMusic = e.duckMusic,
                    duckAmbient = e.duckAmbient
                }
            };
            n++;
        }
        return n;
    }

    /// <summary>(start, end) string index of every non-empty line.</summary>
    private static List<Vector2Int> NonEmptyLines(string s)
    {
        var list = new List<Vector2Int>();
        int start = 0;
        while (start <= s.Length)
        {
            int end = s.IndexOf('\n', start);
            if (end < 0) end = s.Length;
            if (s.Substring(start, end - start).Trim().Length > 0) list.Add(new Vector2Int(start, end));
            start = end + 1;
        }
        return list;
    }

    // ---------- Page music ----------

    private void UpdatePageMusic(int page, StoryMoodRange mood, bool immediate)
    {
        StoryAudioManager audio = StoryAudioManager.Instance;
        if (audio == null) return;

        PageMusicEntry entry = MusicForPage(page);
        // Compare entries, not clips: page 11 must bring the forest back even if a word cue changed the music on page 8
        if (entry == null || entry == currentMusic)
        {
            pendingMusic = null;
            return;
        }

        if (!immediate)
        {
            if (entry != pendingMusic)
            {
                pendingMusic = entry;
                pendingSince = Time.unscaledTime;
                return;
            }

            float hold = ReadingSpeedSettings.Instance != null ? ReadingSpeedSettings.Instance.Current.sectionHold : holdTime;
            if (Time.unscaledTime - pendingSince < hold) return;
        }

        string moodLabel = mood != null ? mood.label : null;
        // Crossfade length comes from StoryAudioManager.Music Fade Time (set by ReaderTuning / ReadingSpeedSettings)
        float crossfade = Mathf.Max(0f, audio.musicFadeTime);
        float silence = ReadingSpeedSettings.Instance != null ? ReadingSpeedSettings.Instance.Current.partSilence : 0.6f;

        if (immediate || currentMusic == null)
            audio.PlayMusic(entry.clip, entry.volume, crossfade, true);
        else if (moodLabel != currentMusicMood)
            audio.PlayMusicAfterSilence(entry.clip, entry.volume, crossfade, silence, crossfade);
        else
            audio.PlayMusic(entry.clip, entry.volume, crossfade, true);

        Debug.Log("[PageMusic] Page " + page + " -> " + entry.clip.name);
        currentMusic = entry;
        currentMusicMood = moodLabel;
        pendingMusic = null;
    }

    /// <summary>The music of the nearest page at or above this one that has a clip.</summary>
    private PageMusicEntry MusicForPage(int page)
    {
        PageMusicEntry best = null;
        foreach (PageMusicEntry e in pageMusic)
        {
            if (e == null || e.clip == null || e.page > page) continue;
            if (best == null || e.page >= best.page) best = e;
        }
        return best;
    }

    // ---------- Editor: fill clips from file names ----------

#if UNITY_EDITOR
    void Reset()
    {
        pageMusic = DefaultPageMusic();
        wordCues = DefaultWordCues();
        FillClipsByName();
    }

    void OnValidate()
    {
        if (!Application.isPlaying) EditorApplication.delayCall += () => { if (this != null) FillClipsByName(); };
    }

    [ContextMenu("Fill missing clips by name")]
    public void FillClipsByName()
    {
        string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets" });
        var paths = new List<string>();
        foreach (string g in guids) paths.Add(AssetDatabase.GUIDToAssetPath(g));

        bool changed = false;
        foreach (PageMusicEntry e in pageMusic)
        {
            if (e != null && e.clip == null && !string.IsNullOrWhiteSpace(e.clipName))
            {
                e.clip = FindClip(paths, e.clipName);
                changed |= e.clip != null;
            }
        }
        foreach (WordCueEntry e in wordCues)
        {
            if (e != null && e.clip == null && !string.IsNullOrWhiteSpace(e.clipName))
            {
                e.clip = FindClip(paths, e.clipName);
                changed |= e.clip != null;
            }
        }
        if (changed) EditorUtility.SetDirty(this);
    }

    private static AudioClip FindClip(List<string> paths, string namePart)
    {
        string wanted = namePart.Trim();
        foreach (string p in paths)
        {
            if (System.IO.Path.GetFileNameWithoutExtension(p).IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0)
                return AssetDatabase.LoadAssetAtPath<AudioClip>(p);
        }
        return null;
    }
#endif
}
