using UnityEngine;

namespace Ricochet.Audio
{
    /// <summary>Pooled, spatialized one-shots. Clips are synthesized at startup (see ChimeSynth).</summary>
    public sealed class SfxPlayer : MonoBehaviour
    {
        const int Voices = 24;
        const int ScaleNotes = 15;
        const int RootMidi = 72; // C5

        AudioSource[] _voices;
        int _next;
        AudioClip[] _notes;
        AudioClip _thud;
        AudioClip _launch;
        AudioClip _crash;
        AudioClip _riftOpen, _hurt, _bolt, _shieldHit, _guard;
        AudioSource _drumroll;
        float _drumLevel;

        void Awake()
        {
            _voices = new AudioSource[Voices];
            for (int i = 0; i < Voices; i++)
            {
                var go = new GameObject("Voice" + i);
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 1f;
                src.spatialize = true;
                src.minDistance = 0.4f;
                src.maxDistance = 12f;
                src.rolloffMode = AudioRolloffMode.Logarithmic;
                src.dopplerLevel = 0f;
                _voices[i] = src;
            }

            _notes = new AudioClip[ScaleNotes];
            for (int n = 0; n < ScaleNotes; n++)
                _notes[n] = ChimeSynth.Bell("Note" + n, ChimeSynth.NoteFrequency(RootMidi, n));
            _thud = ChimeSynth.Thud("Thud");
            _launch = ChimeSynth.Bell("Launch", ChimeSynth.NoteFrequency(RootMidi - 12, 0), 0.5f, 0.3f);
            _crash = ChimeSynth.Crash("Crash");
            _riftOpen = ChimeSynth.Sweep("RiftOpen", 55f, 140f, 1.4f, 0.55f, 1.2f, 11);
            _hurt = ChimeSynth.Sweep("Hurt", 620f, 280f, 0.28f, 0.25f, 7f, 12);
            _bolt = ChimeSynth.Sweep("Bolt", 180f, 720f, 0.4f, 0.7f, 3f, 13);
            _shieldHit = ChimeSynth.Sweep("ShieldHit", 110f, 48f, 0.5f, 0.35f, 6f, 14);
            _guard = ChimeSynth.Bell("Guard", ChimeSynth.NoteFrequency(RootMidi - 24, 3), 0.8f, 0.6f);

            // Tension bed for the last-crystal moment: non-diegetic, so it plays in the head rather than the room.
            var drum = new GameObject("Drumroll");
            drum.transform.SetParent(transform, false);
            _drumroll = drum.AddComponent<AudioSource>();
            _drumroll.playOnAwake = false;
            _drumroll.loop = true;
            _drumroll.spatialBlend = 0f;
            _drumroll.volume = 0f;
            _drumroll.clip = ChimeSynth.Drumroll("Drumroll");
        }

        /// <summary>
        /// Drumroll level 0..1, eased in real time (it keeps swelling while game time is slowed).
        /// Louder is also slightly faster and higher, so it builds as the Spark closes in.
        /// </summary>
        public void SetDrumroll(float level) => _drumLevel = Mathf.Clamp01(level);

        public void PlayCrash(Vector3 position) => Play(_crash, position, 1f, 1f);

        public void PlayRiftOpen(Vector3 position) => Play(_riftOpen, position, 1f, 1f);
        public void PlayHurt(Vector3 position) => Play(_hurt, position, 0.8f, Random.Range(0.93f, 1.07f));
        public void PlayBolt(Vector3 position) => Play(_bolt, position, 0.8f, Random.Range(0.95f, 1.05f));
        public void PlayShieldHit(Vector3 position) => Play(_shieldHit, position, 1f, 1f);
        public void PlayGuard(Vector3 position) => Play(_guard, position, 0.8f, 1f);

        /// <summary>A damage tick as light lands in the creature: the combo scale again, an octave up and softer.</summary>
        public void PlayTick(int index, Vector3 position) =>
            Play(_notes[Mathf.Clamp(index + 5, 0, ScaleNotes - 1)], position, 0.45f, 1f);

        void Update()
        {
            float target = _drumLevel * 0.85f;
            float rate = target > _drumroll.volume ? 10f : 3.5f;
            _drumroll.volume = Mathf.Lerp(_drumroll.volume, target, 1f - Mathf.Exp(-rate * Time.unscaledDeltaTime));
            _drumroll.pitch = 1f + 0.15f * _drumLevel;
            if (_drumLevel > 0f && !_drumroll.isPlaying) _drumroll.Play();
            else if (_drumLevel <= 0f && _drumroll.isPlaying && _drumroll.volume < 0.01f) _drumroll.Stop();
        }

        /// <summary>The n-th note of the rising combo scale (clamped at the top).</summary>
        public void PlayComboNote(int comboIndex, Vector3 position) =>
            Play(_notes[Mathf.Clamp(comboIndex, 0, ScaleNotes - 1)], position, 1f, 1f);

        // Bounces sag in pitch under slow motion, the one sound that follows game time.
        public void PlayBounce(Vector3 position, float intensity) =>
            Play(_thud, position, Mathf.Clamp01(intensity) * 0.7f, Random.Range(0.92f, 1.08f) * Mathf.Lerp(0.6f, 1f, Time.timeScale));

        public void PlayLaunch(Vector3 position) => Play(_launch, position, 0.6f, 1f);

        public void PlayChord(Vector3 position)
        {
            Play(_notes[5], position, 0.9f, 1f);
            Play(_notes[7], position, 0.8f, 1f);
            Play(_notes[9], position, 0.8f, 1f);
            Play(_notes[10], position, 0.9f, 1f);
        }

        void Play(AudioClip clip, Vector3 position, float volume, float pitch)
        {
            var src = _voices[_next];
            _next = (_next + 1) % Voices;
            src.transform.position = position;
            src.pitch = pitch;
            src.volume = volume;
            src.clip = clip;
            src.Play();
        }
    }
}
