using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Story audio on separate channels, optionally routed through an AudioMixer:
///  - Music: equal-power crossfades, per-track trim, resume position, change throttling, ducking.
///  - Ambient: fading loops that can play together.
///  - Voice: one voice at a time (ducks the music).
///  - Sfx: short effects with a minimum gap, a limit on overlap and fade-out at page turns.
///  - Stinger: short musical jingles; the music and ambience duck strongly under them.
/// </summary>
public class StoryAudioManager : MonoBehaviour
{
    public static StoryAudioManager Instance { get; private set; }

    [System.Serializable]
    public class ClipGain
    {
        public AudioClip clip;
        [Tooltip("Loudness trim in decibels (negative = quieter). Use it so every track sounds equally loud.")]
        public float gainDb = 0f;
    }

    [Header("Mixer groups (optional)")]
    [Tooltip("Drag the AudioMixer here: empty group fields below are filled automatically from groups named Music, Ambient, SFX, Stinger and Voice.")]
    public AudioMixer mixer;
    public AudioMixerGroup musicGroup;
    public AudioMixerGroup ambientGroup;
    public AudioMixerGroup sfxGroup;
    public AudioMixerGroup stingerGroup;
    public AudioMixerGroup voiceGroup;

    [Header("Volumes")]
    [Range(0f, 1f)] public float masterVolume = 1f;
    [Range(0f, 1f)] public float musicVolume = 0.6f;
    [Range(0f, 1f)] public float ambientVolume = 0.8f;
    [Range(0f, 1f)] public float voiceVolume = 1f;
    [Range(0f, 1f)] public float sfxVolume = 1f;
    [Range(0f, 1f)] public float stingerVolume = 1f;

    [Header("Music mixing")]
    [Tooltip("Default crossfade length in seconds.")]
    public float musicFadeTime = 2.5f;
    [Tooltip("Minimum seconds between music changes. A change requested sooner is held and applied later (the newest request wins). 0 = off (ReadingSpeedSettings sets it when present).")]
    public float minChangeInterval = 0f;
    [Tooltip("When a track comes back, continue from where it stopped instead of from the start.")]
    public bool resumePosition = true;
    public float resumeRewind = 1.5f;
    public List<ClipGain> clipGains = new List<ClipGain>();

    [Header("Ambient")]
    public float ambientFadeTime = 2f;

    [Header("Page change")]
    [Tooltip("FadeOutPageEffects(): ambience, voice, stingers and sound effects fade out over this time. Music keeps playing.")]
    [Min(0f)] public float pageEffectFadeTime = 0.5f;

    [Header("Ducking")]
    [Tooltip("Duck in code. Untick if you set up Duck Volume (sidechain) in the AudioMixer instead.")]
    public bool useCodeDucking = true;
    public bool duckWhileVoicePlays = true;
    [Range(0.05f, 1f)] public float voiceDuckLevel = 0.35f;
    [Tooltip("Music level for sound effects whose cue has 'Duck Music' ticked.")]
    [Range(0.05f, 1f)] public float sfxDuckLevel = 0.55f;
    [Tooltip("Music level while a stinger plays.")]
    [Range(0.05f, 1f)] public float stingerDuckLevel = 0.15f;
    [Tooltip("Ambient level for cues with 'Duck Ambient' ticked, and during stingers.")]
    [Range(0.05f, 1f)] public float ambientDuckLevel = 0.5f;
    public float duckAttack = 0.15f;
    public float duckRelease = 0.8f;

    [Header("Sound effects")]
    public int sfxPoolSize = 8;
    [Tooltip("Minimum seconds between two sound effects (a later one is dropped, not queued).")]
    public float sfxMinGap = 1.5f;
    public int maxSimultaneousSfx = 3;
    [Tooltip("If on, a sound effect is ignored while the same clip is already playing.")]
    public bool blockSameSfxWhilePlaying = true;
    [Tooltip("No sound effects while a stinger is playing.")]
    public bool blockSfxDuringStinger = true;

    [Header("Voice")]
    [Tooltip("If off, a new voice line is ignored while another voice line is still playing.")]
    public bool voiceInterruptsVoice = true;

    /// <summary>The music track that is currently the target (null if none).</summary>
    public AudioClip CurrentMusic
    {
        get
        {
            for (int i = 0; i < musicVoices.Length; i++)
            {
                if (musicVoices[i].active && musicVoices[i].target > 0f) return musicVoices[i].clip;
            }
            return null;
        }
    }

    public AudioMixerSnapshot CurrentSnapshot { get; private set; }

    private class MusicVoice
    {
        public AudioSource src;
        public AudioClip clip;
        public float level;
        public float target;
        public float rate;
        public float baseVolume;
        public float gain = 1f;
        public bool active;
    }

    private class AmbientVoice
    {
        public AudioSource src;
        public AudioClip clip;
        public float level;
        public float target;
        public float rate;
        public float baseVolume;
        public bool active;
    }

    private class SfxVoice
    {
        public AudioSource src;
        public float baseVolume;
        public float level = 1f;
        public float fadeRate; // > 0 while fading out
    }

    private const int MusicVoiceCount = 3;
    private readonly MusicVoice[] musicVoices = new MusicVoice[MusicVoiceCount];
    private readonly List<AmbientVoice> ambientVoices = new List<AmbientVoice>();
    private readonly List<SfxVoice> sfxVoices = new List<SfxVoice>();
    private readonly Dictionary<AudioClip, float> savedTimes = new Dictionary<AudioClip, float>();

    private AudioSource voiceSource;
    private AudioSource stingerSource;

    private AudioClip pendingClip;
    private float pendingVolume;
    private float pendingFade;
    private float lastChangeTime = -999f;
    private Coroutine silenceRoutine;

    private float musicDuck = 1f;
    private float ambientDuck = 1f;
    private float musicDuckUntil;
    private float ambientDuckUntil;
    private float activeMusicDuckLevel = 1f;
    private float activeAmbientDuckLevel = 1f;

    private float lastSfxTime = -999f;
    private float sfxBlockUntil;

    private readonly Dictionary<AudioSource, Coroutine> oneShotFades = new Dictionary<AudioSource, Coroutine>();

    void Awake()
    {
        Instance = this;
        AutoAssignGroups();
        for (int i = 0; i < MusicVoiceCount; i++)
        {
            musicVoices[i] = new MusicVoice { src = CreateSource("Music " + i, true, musicGroup) };
        }
        voiceSource = CreateSource("Voice", false, voiceGroup);
        stingerSource = CreateSource("Stinger", false, stingerGroup);
        for (int i = 0; i < Mathf.Max(1, sfxPoolSize); i++)
        {
            sfxVoices.Add(new SfxVoice { src = CreateSource("Sfx " + i, false, sfxGroup) });
        }
    }

    private void AutoAssignGroups()
    {
        // Groups dragged in by hand but no mixer: take the mixer from them
        if (mixer == null)
        {
            AudioMixerGroup any = musicGroup != null ? musicGroup
                : ambientGroup != null ? ambientGroup
                : sfxGroup != null ? sfxGroup
                : stingerGroup != null ? stingerGroup
                : voiceGroup;
            if (any != null) mixer = any.audioMixer;
        }
        if (mixer == null) return;
        if (musicGroup == null) musicGroup = FindGroup("Music");
        if (ambientGroup == null) ambientGroup = FindGroup("Ambient");
        if (sfxGroup == null) sfxGroup = FindGroup("SFX");
        if (stingerGroup == null) stingerGroup = FindGroup("Stinger");
        if (voiceGroup == null) voiceGroup = FindGroup("Voice");

        if (musicGroup == null || ambientGroup == null || sfxGroup == null || stingerGroup == null || voiceGroup == null)
        {
            Debug.LogWarning("[Audio] Mixer '" + mixer.name + "' is missing a group named Music, Ambient, SFX, Stinger or Voice. " +
                             "Sounds without a group go straight to the speakers.");
        }
    }

    private AudioMixerGroup FindGroup(string groupName)
    {
        AudioMixerGroup[] found = mixer.FindMatchingGroups(groupName);
        for (int i = 0; i < found.Length; i++)
        {
            if (string.Equals(found[i].name, groupName, System.StringComparison.OrdinalIgnoreCase)) return found[i];
        }
        return null;
    }

    private AudioSource CreateSource(string sourceName, bool loop, AudioMixerGroup group)
    {
        GameObject go = new GameObject(sourceName);
        go.transform.SetParent(transform, false);
        AudioSource s = go.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = loop;
        s.spatialBlend = 0f;
        if (group != null) s.outputAudioMixerGroup = group;
        return s;
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        float now = Time.unscaledTime;

        // Apply a held music change once the minimum interval has passed
        if (pendingClip != null && now - lastChangeTime >= minChangeInterval)
        {
            AudioClip c = pendingClip;
            pendingClip = null;
            StartMusic(c, pendingVolume, pendingFade < 0f ? musicFadeTime : pendingFade);
            lastChangeTime = now;
        }

        // Ducking
        float targetMusic = 1f;
        float targetAmbient = 1f;
        if (useCodeDucking)
        {
            if (duckWhileVoicePlays && voiceSource.isPlaying) targetMusic = voiceDuckLevel;
            if (now < musicDuckUntil) targetMusic = Mathf.Min(targetMusic, activeMusicDuckLevel);
            if (now < ambientDuckUntil) targetAmbient = activeAmbientDuckLevel;
        }
        musicDuck = StepDuck(musicDuck, targetMusic, dt);
        ambientDuck = StepDuck(ambientDuck, targetAmbient, dt);

        // Music fades (equal-power curve)
        for (int i = 0; i < musicVoices.Length; i++)
        {
            MusicVoice v = musicVoices[i];
            if (!v.active) continue;
            v.level = Mathf.MoveTowards(v.level, v.target, v.rate * dt);
            float curve = Mathf.Sin(v.level * Mathf.PI * 0.5f);
            v.src.volume = Mathf.Clamp01(v.baseVolume * v.gain * musicVolume * masterVolume * musicDuck * curve);
            if (v.target <= 0f && v.level <= 0f) ReleaseMusicVoice(v);
        }

        // Ambient fades
        for (int i = 0; i < ambientVoices.Count; i++)
        {
            AmbientVoice v = ambientVoices[i];
            if (!v.active) continue;
            v.level = Mathf.MoveTowards(v.level, v.target, v.rate * dt);
            float curve = Mathf.Sin(v.level * Mathf.PI * 0.5f);
            v.src.volume = Mathf.Clamp01(v.baseVolume * ambientVolume * masterVolume * ambientDuck * curve);
            if (v.target <= 0f && v.level <= 0f) ReleaseAmbientVoice(v);
        }

        // Sound effects fading out after a page turn
        for (int i = 0; i < sfxVoices.Count; i++)
        {
            SfxVoice v = sfxVoices[i];
            if (v.fadeRate <= 0f) continue;
            if (!v.src.isPlaying)
            {
                v.fadeRate = 0f;
                continue;
            }
            v.level -= v.fadeRate * dt;
            if (v.level <= 0f)
            {
                v.src.Stop();
                v.fadeRate = 0f;
            }
            else
            {
                v.src.volume = Mathf.Clamp01(v.baseVolume * v.level);
            }
        }
    }

    private float StepDuck(float current, float target, float dt)
    {
        float rate = target < current ? 1f / Mathf.Max(0.01f, duckAttack) : 1f / Mathf.Max(0.01f, duckRelease);
        return Mathf.MoveTowards(current, target, rate * dt);
    }

    /// <returns>False only when a rule held the sound back (so the caller may try again shortly).</returns>
    public bool Play(StoryAudioCue cue)
    {
        if (cue == null || cue.clip == null) return false;
        switch (cue.channel)
        {
            case StoryAudioChannel.Music: PlayMusic(cue.clip, cue.volume); return true;
            case StoryAudioChannel.Ambient: PlayAmbient(cue.clip, cue.volume); return true;
            case StoryAudioChannel.Voice: PlayVoice(cue.clip, cue.volume); return true;
            case StoryAudioChannel.Stinger: return PlayStinger(cue.clip, cue.volume);
            default:
                if (!PlaySfx(cue.clip, cue.volume)) return false;
                if (cue.duckMusic) DuckMusic(sfxDuckLevel, cue.clip.length);
                if (cue.duckAmbient) DuckAmbient(ambientDuckLevel, cue.clip.length);
                return true;
        }
    }

    // ---------- Mixer mood ----------

    /// <summary>Blend the AudioMixer to a snapshot (e.g. Sad / Happy) over the given seconds.</summary>
    public void ApplySnapshot(AudioMixerSnapshot snapshot, float transitionTime)
    {
        if (snapshot == null || snapshot == CurrentSnapshot) return;
        snapshot.TransitionTo(Mathf.Max(0.01f, transitionTime));
        CurrentSnapshot = snapshot;
    }

    /// <summary>Call when the page changes: running sound effects fade out and none start for a short time.</summary>
    public void NotifyPageTurn(float graceSeconds, float fadeSeconds)
    {
        sfxBlockUntil = Time.unscaledTime + Mathf.Max(0f, graceSeconds);
        float rate = 1f / Mathf.Max(0.01f, fadeSeconds);
        for (int i = 0; i < sfxVoices.Count; i++)
        {
            if (sfxVoices[i].src.isPlaying) sfxVoices[i].fadeRate = rate;
        }
    }

    /// <summary>
    /// Fade page-triggered effects when the reader changes pages (called by AIReadingController).
    /// Music is intentionally left playing so a track can continue over several pages.
    /// </summary>
    public void FadeOutPageEffects()
    {
        float fade = Mathf.Max(0f, pageEffectFadeTime);

        StopAllAmbient(fade);

        float rate = 1f / Mathf.Max(0.01f, fade);
        for (int i = 0; i < sfxVoices.Count; i++)
        {
            if (sfxVoices[i].src.isPlaying) sfxVoices[i].fadeRate = rate;
        }

        FadeOutOneShot(voiceSource, fade);
        FadeOutOneShot(stingerSource, fade);
    }

    private void FadeOutOneShot(AudioSource source, float duration)
    {
        if (source == null || !source.isPlaying || oneShotFades.ContainsKey(source)) return;
        if (duration <= 0f)
        {
            source.Stop();
            return;
        }
        oneShotFades[source] = StartCoroutine(FadeOneShotAndStop(source, duration));
    }

    private IEnumerator FadeOneShotAndStop(AudioSource source, float duration)
    {
        float start = source.volume;
        float t = 0f;
        while (t < duration && source.isPlaying)
        {
            t += Time.unscaledDeltaTime;
            source.volume = Mathf.Lerp(start, 0f, t / duration);
            yield return null;
        }
        source.Stop();
        oneShotFades.Remove(source);
    }

    private void CancelOneShotFade(AudioSource source)
    {
        if (source != null && oneShotFades.TryGetValue(source, out Coroutine routine))
        {
            if (routine != null) StopCoroutine(routine);
            oneShotFades.Remove(source);
        }
    }

    // ---------- Music ----------

    /// <param name="fadeTime">Crossfade seconds; negative = use Music Fade Time.</param>
    /// <param name="immediate">Skip the minimum-interval throttle.</param>
    public void PlayMusic(AudioClip clip, float volume = 1f, float fadeTime = -1f, bool immediate = false)
    {
        if (clip == null) return;
        CancelSilenceRoutine();

        MusicVoice same = FindMusicVoice(clip);
        if (same != null && same.target > 0f)
        {
            same.baseVolume = volume; // already the current track: only follow the volume
            pendingClip = null;
            return;
        }

        bool hasCurrent = CurrentMusic != null;
        if (!immediate && hasCurrent && minChangeInterval > 0f && Time.unscaledTime - lastChangeTime < minChangeInterval)
        {
            pendingClip = clip; // too soon after the last change: hold it, the newest request wins
            pendingVolume = volume;
            pendingFade = fadeTime;
            return;
        }

        pendingClip = null;
        StartMusic(clip, volume, fadeTime < 0f ? musicFadeTime : fadeTime);
        lastChangeTime = Time.unscaledTime;
    }

    /// <summary>Fade the current music out, wait a moment of silence, then fade the new track in.</summary>
    public void PlayMusicAfterSilence(AudioClip clip, float volume, float fadeOut, float silence, float fadeIn)
    {
        if (clip == null) return;
        CancelSilenceRoutine();

        AudioClip current = CurrentMusic;
        if (current == null || current == clip)
        {
            PlayMusic(clip, volume, fadeIn, true);
            return;
        }

        pendingClip = null;
        StopMusic(fadeOut);
        lastChangeTime = Time.unscaledTime;
        silenceRoutine = StartCoroutine(SilenceThenPlay(clip, volume, fadeOut + silence, fadeIn));
    }

    private IEnumerator SilenceThenPlay(AudioClip clip, float volume, float wait, float fadeIn)
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, wait));
        silenceRoutine = null;
        PlayMusic(clip, volume, fadeIn, true);
    }

    private void CancelSilenceRoutine()
    {
        if (silenceRoutine != null)
        {
            StopCoroutine(silenceRoutine);
            silenceRoutine = null;
        }
    }

    public void StopMusic(float fadeTime = -1f)
    {
        CancelSilenceRoutine();
        pendingClip = null;
        float rate = RateFor(fadeTime < 0f ? musicFadeTime : fadeTime);
        for (int i = 0; i < musicVoices.Length; i++)
        {
            if (!musicVoices[i].active) continue;
            musicVoices[i].target = 0f;
            musicVoices[i].rate = rate;
        }
    }

    /// <summary>Temporarily lower the music (level 0..1) for the given number of seconds.</summary>
    public void DuckMusic(float level, float seconds)
    {
        activeMusicDuckLevel = Mathf.Clamp(level, 0.05f, 1f);
        musicDuckUntil = Mathf.Max(musicDuckUntil, Time.unscaledTime + seconds);
    }

    /// <summary>Temporarily lower the ambient loops (level 0..1) for the given number of seconds.</summary>
    public void DuckAmbient(float level, float seconds)
    {
        activeAmbientDuckLevel = Mathf.Clamp(level, 0.05f, 1f);
        ambientDuckUntil = Mathf.Max(ambientDuckUntil, Time.unscaledTime + seconds);
    }

    private static float RateFor(float fadeTime)
    {
        return fadeTime <= 0.001f ? 1000f : 1f / fadeTime;
    }

    private void StartMusic(AudioClip clip, float volume, float fadeTime)
    {
        float rate = RateFor(fadeTime);

        MusicVoice same = FindMusicVoice(clip);
        for (int i = 0; i < musicVoices.Length; i++)
        {
            MusicVoice v = musicVoices[i];
            if (v.active && v != same)
            {
                v.target = 0f;
                v.rate = rate;
            }
        }

        if (same != null) // still fading out: bring it back instead of restarting it
        {
            same.target = 1f;
            same.rate = rate;
            same.baseVolume = volume;
            return;
        }

        MusicVoice voice = GetFreeMusicVoice();
        voice.clip = clip;
        voice.src.clip = clip;
        voice.src.loop = true;
        voice.level = 0f;
        voice.target = 1f;
        voice.rate = rate;
        voice.baseVolume = volume;
        voice.gain = ClipGainLinear(clip);
        voice.src.volume = 0f;

        if (resumePosition && savedTimes.TryGetValue(clip, out float saved))
        {
            float t = Mathf.Max(0f, saved - resumeRewind);
            voice.src.time = t < clip.length ? t : 0f;
        }
        else
        {
            voice.src.time = 0f;
        }

        voice.src.Play();
        voice.active = true;
    }

    private MusicVoice FindMusicVoice(AudioClip clip)
    {
        for (int i = 0; i < musicVoices.Length; i++)
        {
            if (musicVoices[i].active && musicVoices[i].clip == clip) return musicVoices[i];
        }
        return null;
    }

    private MusicVoice GetFreeMusicVoice()
    {
        MusicVoice quietest = null;
        for (int i = 0; i < musicVoices.Length; i++)
        {
            MusicVoice v = musicVoices[i];
            if (!v.active) return v;
            if (quietest == null || v.level < quietest.level) quietest = v;
        }
        ReleaseMusicVoice(quietest); // all busy (very rapid changes): reuse the quietest one
        return quietest;
    }

    private void ReleaseMusicVoice(MusicVoice v)
    {
        if (v.clip != null && v.src.isPlaying) savedTimes[v.clip] = v.src.time;
        v.src.Stop();
        v.src.clip = null;
        v.clip = null;
        v.level = 0f;
        v.active = false;
    }

    private float ClipGainLinear(AudioClip clip)
    {
        for (int i = 0; i < clipGains.Count; i++)
        {
            if (clipGains[i].clip == clip) return Mathf.Pow(10f, clipGains[i].gainDb / 20f);
        }
        return 1f;
    }

    // ---------- Ambient ----------

    /// <param name="fadeTime">Fade-in seconds; negative = use Ambient Fade Time.</param>
    public void PlayAmbient(AudioClip clip, float volume = 1f, float fadeTime = -1f)
    {
        if (clip == null) return;
        float rate = RateFor(fadeTime < 0f ? ambientFadeTime : fadeTime);

        AmbientVoice v = FindAmbientVoice(clip);
        if (v != null) // already running (or fading out): do not restart
        {
            v.target = 1f;
            v.rate = rate;
            v.baseVolume = volume;
            return;
        }

        v = GetFreeAmbientVoice();
        v.clip = clip;
        v.src.clip = clip;
        v.src.loop = true;
        v.level = 0f;
        v.target = 1f;
        v.rate = rate;
        v.baseVolume = volume;
        v.src.volume = 0f;
        v.src.Play();
        v.active = true;
    }

    public void StopAmbient(AudioClip clip, float fadeTime = -1f)
    {
        AmbientVoice v = FindAmbientVoice(clip);
        if (v == null) return;
        v.target = 0f;
        v.rate = RateFor(fadeTime < 0f ? ambientFadeTime : fadeTime);
    }

    public void StopAllAmbient(float fadeTime = -1f)
    {
        StopAllAmbientExcept(null, fadeTime);
    }

    /// <summary>Fade out every ambient loop except the listed clips.</summary>
    public void StopAllAmbientExcept(IList<AudioClip> keep, float fadeTime = -1f)
    {
        float rate = RateFor(fadeTime < 0f ? ambientFadeTime : fadeTime);
        for (int i = 0; i < ambientVoices.Count; i++)
        {
            AmbientVoice v = ambientVoices[i];
            if (!v.active) continue;
            if (keep != null && keep.Contains(v.clip)) continue;
            v.target = 0f;
            v.rate = rate;
        }
    }

    private AmbientVoice FindAmbientVoice(AudioClip clip)
    {
        for (int i = 0; i < ambientVoices.Count; i++)
        {
            if (ambientVoices[i].active && ambientVoices[i].clip == clip) return ambientVoices[i];
        }
        return null;
    }

    private AmbientVoice GetFreeAmbientVoice()
    {
        for (int i = 0; i < ambientVoices.Count; i++)
        {
            if (!ambientVoices[i].active) return ambientVoices[i];
        }
        AmbientVoice v = new AmbientVoice { src = CreateSource("Ambient " + ambientVoices.Count, true, ambientGroup) };
        ambientVoices.Add(v);
        return v;
    }

    private void ReleaseAmbientVoice(AmbientVoice v)
    {
        v.src.Stop();
        v.src.clip = null;
        v.clip = null;
        v.level = 0f;
        v.active = false;
    }

    // ---------- Voice ----------
    public void PlayVoice(AudioClip clip, float volume = 1f)
    {
        if (clip == null) return;
        CancelOneShotFade(voiceSource);
        if (voiceSource.isPlaying)
        {
            if (voiceSource.clip == clip)
            {
                voiceSource.volume = volume * voiceVolume * masterVolume;
                return;
            }
            if (!voiceInterruptsVoice) return;
            voiceSource.Stop();
        }
        voiceSource.clip = clip;
        voiceSource.volume = volume * voiceVolume * masterVolume;
        voiceSource.Play();
    }

    // ---------- Stinger ----------
    /// <summary>Short musical jingle. One at a time; music and ambience duck strongly while it plays.</summary>
    public bool PlayStinger(AudioClip clip, float volume = 1f)
    {
        if (clip == null) return false;
        if (oneShotFades.ContainsKey(stingerSource))
        {
            CancelOneShotFade(stingerSource);
            stingerSource.Stop();
        }
        if (stingerSource.isPlaying) return false;
        stingerSource.clip = clip;
        stingerSource.volume = Mathf.Clamp01(volume * stingerVolume * masterVolume);
        stingerSource.Play();
        DuckMusic(stingerDuckLevel, clip.length + 0.3f);
        DuckAmbient(ambientDuckLevel, clip.length + 0.3f);
        return true;
    }

    // ---------- Sfx ----------
    /// <returns>True if the sound effect started, false if a rule dropped it.</returns>
    public bool PlaySfx(AudioClip clip, float volume = 1f)
    {
        if (clip == null) return false;
        float now = Time.unscaledTime;
        if (now < sfxBlockUntil) return false;                                  // just after a page turn
        if (blockSfxDuringStinger && stingerSource.isPlaying) return false;
        if (sfxMinGap > 0f && now - lastSfxTime < sfxMinGap) return false;     // too soon after the last one

        int playing = 0;
        SfxVoice free = null;
        SfxVoice oldest = null;
        for (int i = 0; i < sfxVoices.Count; i++)
        {
            SfxVoice v = sfxVoices[i];
            if (v.src.isPlaying)
            {
                playing++;
                if (blockSameSfxWhilePlaying && v.src.clip == clip) return false;
                if (oldest == null || v.src.time > oldest.src.time) oldest = v;
            }
            else if (free == null)
            {
                free = v;
            }
        }
        if (playing >= Mathf.Max(1, maxSimultaneousSfx)) return false;

        SfxVoice use = free != null ? free : oldest;
        if (use == null) return false;

        use.src.Stop();
        use.src.clip = clip;
        use.baseVolume = Mathf.Clamp01(volume * sfxVolume * masterVolume);
        use.level = 1f;
        use.fadeRate = 0f;
        use.src.volume = use.baseVolume;
        use.src.Play();
        lastSfxTime = now;
        return true;
    }

    public void StopAll()
    {
        StopMusic();
        StopAllAmbient();
        CancelOneShotFade(voiceSource);
        CancelOneShotFade(stingerSource);
        voiceSource.Stop();
        stingerSource.Stop();
        for (int i = 0; i < sfxVoices.Count; i++)
        {
            sfxVoices[i].fadeRate = 0f;
            sfxVoices[i].src.Stop();
        }
    }
}
