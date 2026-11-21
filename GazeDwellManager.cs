
using UnityEngine;
using TMPro;

public class GazeDwellManager : MonoBehaviour
{
    public static GazeDwellManager Instance { get; private set; }

    [Header("Gaze Settings")]
    public float defaultDwellTime = 1.0f;
    public float lookAwayGrace = 0.2f;

    [Header("Gaze Activation")]
    public bool gazeEnabled = false;

    [Header("Debug")]
    public bool logWordChanges = true;

    public GazeRegion Current { get; private set; }

    public string CurrentWord { get; private set; }

    public int CurrentWordIndex { get; private set; } = -1;

    public float CurrentWordProgress { get; private set; }

    private float lookAwayTimer;

    private int lastLoggedWordIndex = -1;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        gazeEnabled = false;
    }

    private void OnDisable()
    {
        DisableGaze();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void EnableGaze()
    {
        gazeEnabled = true;
        lookAwayTimer = 0f;

        Debug.Log("[Gaze] Gaze interaction enabled.");
    }

    public void DisableGaze()
    {
        gazeEnabled = false;
        lookAwayTimer = 0f;

        SwitchTo(null, true);

        Debug.Log("[Gaze] Gaze interaction disabled.");
    }

    private void Update()
    {
        GazeInputMapper mapper = GazeInputMapper.Instance;

        if (mapper == null)
            return;

        float dt = Time.unscaledDeltaTime;

        // Always sample Beam gaze data.
        mapper.Sample(dt);

        // Do not trigger interactions during calibration.
        if (GazeCalibration.Instance != null &&
            GazeCalibration.Instance.IsRunning)
        {
            if (Current != null)
                SwitchTo(null, true);

            return;
        }

        if (!gazeEnabled)
        {
            if (Current != null)
                SwitchTo(null, true);

            return;
        }

        if (Current != null && !Current.isActiveAndEnabled)
            SwitchTo(null, false);

        GazeRegion hit = mapper.IsValid
            ? FindRegionAt(mapper.ScreenPosition)
            : null;

        if (hit != Current)
        {
            if (Current != null &&
                lookAwayTimer < lookAwayGrace)
            {
                lookAwayTimer += dt;
                return;
            }

            SwitchTo(hit, true);
        }
        else
        {
            lookAwayTimer = 0f;
        }

        if (Current != null)
        {
            Advance(Current, dt);

            UpdateWordDetection(
                Current,
                mapper.ScreenPosition,
                dt
            );
        }
        else
        {
            ClearCurrentWord();
        }
    }

    private void Advance(GazeRegion region, float dt)
    {
        float need = region.dwellTime >= 0f
            ? region.dwellTime
            : defaultDwellTime;

        region.timer += dt;

        region.Progress = need > 0f
            ? Mathf.Clamp01(region.timer / need)
            : 1f;

        if (region.timer < need ||
            region.triggeredThisLook ||
            region.usedOnce)
        {
            return;
        }

        if (!region.Fire())
        {
            // A sound rule held it back: try again a moment later.
            region.timer = Mathf.Max(0f, need - 0.25f);
            return;
        }

        switch (region.retrigger)
        {
            case RetriggerMode.WhileLooking:

                region.timer = -region.repeatInterval;
                region.Progress = 0f;

                break;

            case RetriggerMode.Once:

                region.usedOnce = true;
                region.triggeredThisLook = true;

                break;

            default:

                region.triggeredThisLook = true;

                break;
        }
    }

    private void UpdateWordDetection(
        GazeRegion region,
        Vector2 screenPosition,
        float dt)
    {
        if (region == null || region.textTarget == null)
        {
            ClearCurrentWord();
            return;
        }

        TMP_Text target = region.textTarget;

        if (!target.isActiveAndEnabled)
        {
            ClearCurrentWord();
            return;
        }

        int wordIndex =
            TMP_TextUtilities.FindIntersectingWord(
                target,
                screenPosition,
                region.GetEventCamera()
            );

        string word = "";

        if (wordIndex >= 0 &&
            wordIndex < target.textInfo.wordCount)
        {
            word = target.textInfo.wordInfo[wordIndex].GetWord();
        }
        else
        {
            wordIndex = -1;
        }

        region.UpdateWordDetection(
            wordIndex,
            word,
            dt
        );

        CurrentWordIndex = wordIndex;
        CurrentWord = word;

        CurrentWordProgress = region.WordProgress;

        if (logWordChanges &&
            wordIndex != lastLoggedWordIndex)
        {
            lastLoggedWordIndex = wordIndex;

            if (wordIndex >= 0)
            {
                Debug.Log(
                    "[Gaze Manager] Current word: " + word
                );
            }
        }
    }

    private void ClearCurrentWord()
    {
        CurrentWord = "";
        CurrentWordIndex = -1;
        CurrentWordProgress = 0f;

        lastLoggedWordIndex = -1;
    }

    private void SwitchTo(GazeRegion next, bool callExit)
    {
        if (Current != null)
        {
            if (callExit)
                Current.Exit();
            else
                Current.ResetLookState();
        }

        Current = next;

        lookAwayTimer = 0f;

        ClearCurrentWord();

        if (Current != null)
            Current.Enter();
    }

    private static GazeRegion FindRegionAt(Vector2 screenPoint)
    {
        GazeRegion best = null;

        float bestArea = float.MaxValue;

        var list = GazeRegion.Active;

        for (int i = 0; i < list.Count; i++)
        {
            GazeRegion region = list[i];

            if (region == null ||
                !region.isActiveAndEnabled)
            {
                continue;
            }

            if (!region.Contains(screenPoint))
                continue;

            float area = region.Area;

            if (best == null ||
                region.priority > best.priority ||
                (region.priority == best.priority &&
                 area < bestArea))
            {
                best = region;
                bestArea = area;
            }
        }

        return best;
    }
}
