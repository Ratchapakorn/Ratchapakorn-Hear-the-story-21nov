using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public enum PageTransitionStyle
{
    Crossfade,          // new music fades in while the old one fades out (same mood)
    FadeThroughSilence, // old music and ambience fade out, a short silence, then the new music (mood change)
    Cut                 // immediate
}

[System.Serializable]
public class PageAmbient
{
    public AudioClip clip;
    [Range(0f, 1f)] public float volume = 0.8f;
    [Tooltip("Fade-in seconds. -1 = use the reading-speed crossfade time.")]
    public float fadeIn = -1f;
}

/// <summary>
/// Put on the root object of a page / scroll section. Describes what that page does with the music,
/// ambience and mixer mood while it is the page being read.
/// </summary>
public class PageAudioProfile : MonoBehaviour
{
    [Header("Mood")]
    [Tooltip("AudioMixer snapshot for this page's mood (e.g. Sad / Happy). Empty = same mood as the page above.")]
    public AudioMixerSnapshot snapshot;
    [Tooltip("Seconds for the snapshot to blend in. -1 = automatic.")]
    public float snapshotTime = -1f;

    [Header("Music")]
    [Tooltip("Music of this page. Empty = the music of the page above keeps playing.")]
    public AudioClip music;
    [Range(0f, 1f)] public float musicVolume = 1f;
    public PageTransitionStyle transition = PageTransitionStyle.Crossfade;
    [Tooltip("Whenever the mood (snapshot) changes, in either scroll direction, fade through a short silence instead of using the style above.")]
    public bool silenceOnMoodChange = true;

    [Header("Ambient loops")]
    public List<PageAmbient> ambient = new List<PageAmbient>();
    [Tooltip("Fade out ambient loops that are not listed here.")]
    public bool stopOtherAmbient = true;

    public List<AudioClip> AmbientClips()
    {
        var list = new List<AudioClip>();
        for (int i = 0; i < ambient.Count; i++)
        {
            if (ambient[i].clip != null) list.Add(ambient[i].clip);
        }
        return list;
    }

    /// <summary>
    /// Make the audio match a page. The caller resolves the music and mood (a page without its own
    /// music or mood inherits the one above it). profile may be null.
    /// </summary>
    /// <param name="changed">False for the very first page: start right away instead of fading.</param>
    public static void Apply(
        PageAudioProfile profile,
        bool changed,
        AudioClip music,
        float musicVolume,
        AudioMixerSnapshot snapshot,
        bool stopAmbientWithoutProfile)
    {
        StoryAudioManager audio = StoryAudioManager.Instance;
        if (audio == null) return;

        SpeedValues speed = ReadingSpeedSettings.Instance != null
            ? ReadingSpeedSettings.Instance.Current
            : SpeedValues.Medium();

        PageTransitionStyle style = profile != null ? profile.transition : PageTransitionStyle.Crossfade;
        bool moodChange = snapshot != null && audio.CurrentSnapshot != null && audio.CurrentSnapshot != snapshot;
        if (changed && moodChange && (profile == null || profile.silenceOnMoodChange))
        {
            style = PageTransitionStyle.FadeThroughSilence;
        }

        // Sound effects still playing from the previous page fade out; none start right after the change
        if (changed) audio.NotifyPageTurn(speed.sfxGraceAfterPageTurn, speed.sfxFadeOnPageTurn);

        float fade = style == PageTransitionStyle.FadeThroughSilence ? speed.partFadeOut : speed.musicCrossfade;

        // Mixer mood
        if (snapshot != null)
        {
            float t = style == PageTransitionStyle.FadeThroughSilence ? speed.partFadeOut + speed.partSilence : speed.musicCrossfade;
            if (profile != null && profile.snapshotTime >= 0f) t = profile.snapshotTime;
            audio.ApplySnapshot(snapshot, changed ? t : 0.01f);
        }

        // Ambient loops
        if (profile != null)
        {
            if (profile.stopOtherAmbient) audio.StopAllAmbientExcept(profile.AmbientClips(), fade);
            for (int i = 0; i < profile.ambient.Count; i++)
            {
                PageAmbient a = profile.ambient[i];
                if (a.clip != null) audio.PlayAmbient(a.clip, a.volume, a.fadeIn >= 0f ? a.fadeIn : fade);
            }
        }
        else if (changed && stopAmbientWithoutProfile)
        {
            audio.StopAllAmbient(fade);
        }

        // Music
        if (music == null) return;

        if (!changed)
        {
            audio.PlayMusic(music, musicVolume, speed.musicCrossfade, true);
            return;
        }

        switch (style)
        {
            case PageTransitionStyle.Cut:
                audio.PlayMusic(music, musicVolume, 0.05f, true);
                break;
            case PageTransitionStyle.FadeThroughSilence:
                audio.PlayMusicAfterSilence(music, musicVolume, speed.partFadeOut, speed.partSilence, speed.musicCrossfade);
                break;
            default:
                audio.PlayMusic(music, musicVolume, speed.musicCrossfade, false);
                break;
        }
    }
}
