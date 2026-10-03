using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// Slow motion for drama (docs/TECH_GUIDE.md section 5: slow the game, never the head). Only game time slows:
    /// head and hand tracking, rendering and audio run at full rate. The physics step shrinks with the time scale,
    /// so the Spark keeps a real-time tick rate and stays smooth instead of stepping visibly.
    /// </summary>
    public sealed class TimeWarp : MonoBehaviour
    {
        [SerializeField] float _easeIn = 14f;   // exponential rate per real second
        [SerializeField] float _easeOut = 5f;

        static float s_baseStep;
        float _target = 1f;
        float _holdUntil;

        /// <summary>The unwarped physics step. Use for scripted simulation (prediction, sweeps).</summary>
        public static float PhysicsStep => s_baseStep > 0f ? s_baseStep : Time.fixedDeltaTime;

        public bool IsWarped => Time.timeScale < 0.999f || _target < 1f;

        /// <summary>Dev capture lock: pins game time (e.g. near-still for a screenshot) and ignores gameplay requests.</summary>
        public bool Frozen { get; private set; }

        public void Freeze(float scale)
        {
            Frozen = true;
            Apply(Mathf.Clamp(scale, 0.001f, 1f));
        }

        /// <summary>A true stop (the player left): game time is exactly 0 until Unfreeze, which eases back in.</summary>
        public void Stop()
        {
            Frozen = true;
            Time.timeScale = 0f; // no physics steps run at 0, so the physics step is left as it was
        }

        public void Unfreeze() => Frozen = false;

        void Awake()
        {
            s_baseStep = Time.fixedDeltaTime;
            // The game always steps physics itself; a sweep or prediction interrupted in the Editor can leave Script
            // mode saved in ProjectSettings, which silently freezes every shot.
            if (Physics.simulationMode != SimulationMode.FixedUpdate)
            {
                Debug.LogWarning($"[Ricochet] Physics simulation mode was {Physics.simulationMode}; restoring FixedUpdate");
                Physics.simulationMode = SimulationMode.FixedUpdate;
            }
        }

        /// <summary>Ease game time toward this scale (0..1) until Release.</summary>
        public void SlowTo(float scale)
        {
            _target = Mathf.Clamp(scale, 0.001f, 1f);
            _holdUntil = 0f;
        }

        /// <summary>Return to full speed, optionally holding the current slow-mo for a moment first (real seconds).</summary>
        public void Release(float holdSeconds = 0f)
        {
            _holdUntil = RealTime.Now + holdSeconds;
            _target = 1f;
        }

        /// <summary>Drop to this scale at once (a hit-stop), hold it for real seconds, then ease back to full speed.</summary>
        public void Hold(float scale, float seconds)
        {
            if (Frozen) return;
            Apply(Mathf.Clamp(scale, 0.001f, 1f));
            Release(seconds);
        }

        void Update()
        {
            if (Frozen || RealTime.Now < _holdUntil) return;
            float scale = Time.timeScale;
            if (Mathf.Approximately(scale, _target)) return;

            float rate = _target < scale ? _easeIn : _easeOut;
            scale = Mathf.Lerp(scale, _target, 1f - Mathf.Exp(-rate * RealTime.DeltaTime));
            if (Mathf.Abs(scale - _target) < 0.01f) scale = _target;
            Apply(scale);
        }

        void OnDisable()
        {
            _target = 1f;
            Apply(1f);
        }

        static void Apply(float scale)
        {
            Time.timeScale = scale;
            Time.fixedDeltaTime = PhysicsStep * scale;
        }
    }
}
