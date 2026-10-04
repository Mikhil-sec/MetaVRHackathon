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
        AudioClip _grab, _twang, _cancel, _swell;
        AudioClip _zip, _sealPop, _shatter, _growl, _emerge;

        /// <summary>The rift's looping hum, for its own spatial source.</summary>
        public AudioClip RiftHum { get; private set; }
        AudioClip[] _pullTicks;
        public const int PullSteps = 6;
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

            // The sling's voice (TECH_GUIDE section 6: sound replaces haptics). A taut band, plucked.
            _grab = ChimeSynth.Pluck("Grab", ChimeSynth.NoteFrequency(RootMidi, 2), 0.35f, 9f, 0.8f, 21);
            _twang = ChimeSynth.Pluck("Twang", ChimeSynth.NoteFrequency(RootMidi - 24, 0), 0.9f, 2.2f, 0.9f, 22);
            _cancel = ChimeSynth.Sweep("Cancel", 520f, 260f, 0.22f, 0.15f, 9f, 23);
            _swell = ChimeSynth.Sweep("Swell", 220f, 880f, 1.8f, 0.2f, 0.6f, 24);
            _zip = ChimeSynth.Zip("Zip");
            _sealPop = ChimeSynth.Bell("SealPop", ChimeSynth.NoteFrequency(RootMidi - 12, 0), 0.9f, 0.7f);
            _shatter = ChimeSynth.Shatter("Shatter");
            _growl = ChimeSynth.Sweep("Growl", 70f, 150f, 0.55f, 0.55f, 1.2f, 52);
            _emerge = ChimeSynth.Sweep("Emerge", 360f, 85f, 0.8f, 0.6f, 2.5f, 53);
            RiftHum = ChimeSynth.Drone("RiftHum");
            _pullTicks = new AudioClip[PullSteps];
            for (int n = 0; n < PullSteps; n++)
                _pullTicks[n] = ChimeSynth.Pluck("PullTick" + n, ChimeSynth.NoteFrequency(RootMidi, n), 0.09f, 45f, 0.6f, 30 + n);

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
        /// <summary>The creature squeals as your light gets through (pitch: its voice, from its size).</summary>
        public void PlayHurt(Vector3 position, float pitch = 1f) => Play(_hurt, position, 0.8f, pitch * Random.Range(0.95f, 1.05f));
        public void PlayBolt(Vector3 position) => Play(_bolt, position, 0.8f, Random.Range(0.95f, 1.05f));
        public void PlayShieldHit(Vector3 position) => Play(_shieldHit, position, 1f, 1f);
        public void PlayGuard(Vector3 position) => Play(_guard, position, 0.8f, 1f);

        /// <summary>A damage tick as light lands in the creature: the combo scale again, an octave up and softer.</summary>
        public void PlayTick(int index, Vector3 position) =>
            Play(_notes[Mathf.Clamp(index + 5, 0, ScaleNotes - 1)], position, 0.45f, 1f);

        /// <summary>A crystal landing as the board spills out of the rift: soft, rising up the scale.</summary>
        public void PlaySeed(int index, Vector3 position) =>
            Play(_notes[Mathf.Min(index, ScaleNotes - 1)], position, 0.18f, 1f);

        void Update()
        {
            float target = _drumLevel * 0.85f;
            float rate = target > _drumroll.volume ? 10f : 3.5f;
            _drumroll.volume = Mathf.Lerp(_drumroll.volume, target, 1f - Mathf.Exp(-rate * RealTime.DeltaTime));
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

        public void PlayGrab(Vector3 position) => Play(_grab, position, 0.7f, Random.Range(0.97f, 1.03f));
        /// <summary>The pull ratchet: one rising pentatonic tick per step of stretch (0..PullSteps-1).</summary>
        public void PlayPullTick(int step, Vector3 position) =>
            Play(_pullTicks[Mathf.Clamp(step, 0, PullSteps - 1)], position, 0.35f + 0.06f * step, 1f);
        /// <summary>The band snapping home on release; harder pulls ring higher and louder.</summary>
        public void PlayTwang(Vector3 position, float charge) =>
            Play(_twang, position, Mathf.Lerp(0.45f, 0.9f, charge), Mathf.Lerp(0.9f, 1.25f, charge));
        public void PlayCancel(Vector3 position) => Play(_cancel, position, 0.4f, 1f);
        /// <summary>The Fever swell: a bright rising sweep under the shattering wave.</summary>
        public void PlaySwell(Vector3 position) => Play(_swell, position, 0.8f, 1f);

        /// <summary>The rift zipping shut, then the pop as it closes.</summary>
        public void PlayZip(Vector3 position) => Play(_zip, position, 0.8f, 1f);
        public void PlaySealPop(Vector3 position) => Play(_sealPop, position, 0.9f, 1f);

        /// <summary>The creature drawing back before its move: a rising rumble, so the telegraph is heard too.</summary>
        public void PlayGrowl(Vector3 position, float pitch = 1f) => Play(_growl, position, 0.7f, pitch * Random.Range(0.96f, 1.04f));
        /// <summary>The creature coming out of the rift: a falling whoosh.</summary>
        public void PlayEmerge(Vector3 position, float pitch = 1f) => Play(_emerge, position, 0.75f, pitch);

        /// <summary>The creature breaking apart along its cracks.</summary>
        public void PlayShatter(Vector3 position) => Play(_shatter, position, 1f, Random.Range(0.96f, 1.04f));

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
