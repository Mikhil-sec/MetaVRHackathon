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
        }

        /// <summary>The n-th note of the rising combo scale (clamped at the top).</summary>
        public void PlayComboNote(int comboIndex, Vector3 position) =>
            Play(_notes[Mathf.Clamp(comboIndex, 0, ScaleNotes - 1)], position, 1f, 1f);

        public void PlayBounce(Vector3 position, float intensity) =>
            Play(_thud, position, Mathf.Clamp01(intensity) * 0.7f, Random.Range(0.92f, 1.08f));

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
