using Ricochet.Gameplay;
using UnityEngine;

namespace Ricochet.Audio
{
    /// <summary>
    /// The music (Audio/Music/Source~/music_bed.py): two looping stems started on the same DSP time so they stay
    /// beat-locked. Music is not in the room, so both are 2D. The bed plays throughout, quietly; the pulse stem fades
    /// in for the boss and under Fever. The whole mix ducks under the lethal drama (the drumroll owns that moment) and
    /// lifts for a win. Fades run on the real-time clock; the headset-off pause stops it with everything else.
    /// </summary>
    public sealed class MusicBed : MonoBehaviour
    {
        [SerializeField] AudioClip _bed;
        [SerializeField] AudioClip _pulse;
        [SerializeField] ShotDrama _drama;
        [SerializeField] float _bedLevel = 0.3f;
        [SerializeField] float _pulseLevel = 0.36f;
        [SerializeField] float _fadeInSeconds = 6f;   // rises under the cold open
        [SerializeField] float _duck = 0.15f;         // level under the lethal drama

        AudioSource _bedSource, _pulseSource;
        float _fadeIn, _level = 1f, _intensity, _intensityTarget, _liftUntil;

        /// <summary>0 = bed only, 1 = the pulse stem fully in (boss, Fever).</summary>
        public void SetIntensity(float amount) => _intensityTarget = Mathf.Clamp01(amount);

        /// <summary>A brief lift of the whole mix (a win).</summary>
        public void Lift(float seconds) => _liftUntil = RealTime.Now + seconds;

        void Awake()
        {
            _bedSource = Source("Bed", _bed);
            _pulseSource = Source("Pulse", _pulse);
        }

        AudioSource Source(string name, AudioClip clip)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            src.priority = 0;
            src.bypassReverbZones = true;
            src.volume = 0f;
            return src;
        }

        void Start()
        {
            if (_bed == null || _pulse == null) { enabled = false; return; }
            double at = AudioSettings.dspTime + 0.25;
            _bedSource.PlayScheduled(at);
            _pulseSource.PlayScheduled(at);
        }

        void Update()
        {
            float dt = RealTime.DeltaTime;
            _fadeIn = Mathf.MoveTowards(_fadeIn, 1f, dt / _fadeInSeconds);
            float want = (_drama != null && _drama.Active ? _duck : 1f) * (RealTime.Now < _liftUntil ? 1.4f : 1f);
            // Duck fast (the drumroll swells in), come back slowly.
            _level = Mathf.MoveTowards(_level, want, dt * (want < _level ? 3f : 0.5f));
            _intensity = Mathf.MoveTowards(_intensity, _intensityTarget, dt * 0.4f);
            float fade = _fadeIn * _fadeIn;
            _bedSource.volume = _bedLevel * _level * fade;
            _pulseSource.volume = _pulseLevel * _intensity * _level * fade;
        }
    }
}
