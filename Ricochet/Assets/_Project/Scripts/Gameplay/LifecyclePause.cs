using System;
using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// The bus-stop test (docs/TECH_GUIDE.md section 9): take the headset off, or open the system menu, and the game
    /// holds still: game time freezes (a Spark in flight hangs in the air, the creature waits) and audio pauses.
    /// Put it back on and time eases back in from exactly where it stopped, slow motion included. The run itself
    /// saves on every turn and on application pause (EncounterDirector), so quitting from here loses nothing.
    /// </summary>
    public sealed class LifecyclePause : MonoBehaviour
    {
        [SerializeField] TimeWarp _warp;

        bool _unmounted, _noFocus, _appPaused, _simulated;

        public static bool Paused { get; private set; }
        public static event Action<bool> Changed;

        void OnEnable()
        {
            OVRManager.HMDUnmounted += OnUnmounted;
            OVRManager.HMDMounted += OnMounted;
            OVRManager.InputFocusLost += OnFocusLost;
            OVRManager.InputFocusAcquired += OnFocusAcquired;
        }

        void OnDisable()
        {
            OVRManager.HMDUnmounted -= OnUnmounted;
            OVRManager.HMDMounted -= OnMounted;
            OVRManager.InputFocusLost -= OnFocusLost;
            OVRManager.InputFocusAcquired -= OnFocusAcquired;
            if (Paused) { _unmounted = _noFocus = _appPaused = _simulated = false; Refresh(); }
        }

        void OnUnmounted() { _unmounted = true; Refresh(); }
        void OnMounted() { _unmounted = false; Refresh(); }
        void OnFocusLost() { _noFocus = true; Refresh(); }
        void OnFocusAcquired() { _noFocus = false; Refresh(); }

        void OnApplicationPause(bool paused)
        {
#if !UNITY_EDITOR
            // In the Editor, Play Mode pausing is a debugging tool, not the player leaving.
            _appPaused = paused;
            Refresh();
#endif
        }

        /// <summary>Automation: stand in for taking the headset off (true) and putting it back on (false).</summary>
        public void Simulate(bool away)
        {
            _simulated = away;
            Refresh();
        }

        void Refresh()
        {
            bool paused = _unmounted || _noFocus || _appPaused || _simulated;
            if (paused == Paused) return;
            Paused = paused;
            AudioListener.pause = paused;
            if (_warp != null)
            {
                if (paused) _warp.Stop();
                else _warp.Unfreeze(); // time eases back to whatever the game wanted (TimeWarp's ease-out)
            }
            Debug.Log(paused
                ? $"[Ricochet] Paused (unmounted={_unmounted} focusLost={_noFocus} appPaused={_appPaused} simulated={_simulated})"
                : "[Ricochet] Resumed");
            Changed?.Invoke(paused);
        }
    }
}
