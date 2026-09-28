using UnityEngine;

namespace Ricochet.Audio
{
    /// <summary>
    /// Synthesizes bell/chime clips at startup so every crystal hit can step up a pentatonic scale
    /// (Peggle's rising-note trick; pentatonic never sounds wrong). Placeholder until final sound design,
    /// but already mono and spatializable.
    /// </summary>
    public static class ChimeSynth
    {
        const int SampleRate = 48000;

        // Major pentatonic degrees in semitones.
        static readonly int[] Pentatonic = { 0, 2, 4, 7, 9 };

        /// <summary>Note n of an ascending major pentatonic scale starting at rootMidi.</summary>
        public static float NoteFrequency(int rootMidi, int n)
        {
            int octave = n / Pentatonic.Length;
            int semis = Pentatonic[n % Pentatonic.Length] + 12 * octave;
            return 440f * Mathf.Pow(2f, (rootMidi + semis - 69) / 12f);
        }

        /// <summary>Inharmonic-partial bell with a soft attack and exponential decay.</summary>
        public static AudioClip Bell(string name, float frequency, float seconds = 1.2f, float brightness = 1f)
        {
            int samples = Mathf.CeilToInt(seconds * SampleRate);
            var data = new float[samples];
            // Partial ratios / gains / decay rates roughly modelled on a glass bell.
            float[] ratio = { 1f, 2.0f, 2.76f, 5.4f, 8.9f };
            float[] gain = { 1f, 0.45f, 0.3f * brightness, 0.16f * brightness, 0.07f * brightness };
            float[] decay = { 3.2f, 4.5f, 6f, 9f, 13f };

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;
                float attack = Mathf.Clamp01(t / 0.004f);
                float s = 0f;
                for (int p = 0; p < ratio.Length; p++)
                    s += gain[p] * Mathf.Exp(-decay[p] * t) * Mathf.Sin(2f * Mathf.PI * frequency * ratio[p] * t);
                data[i] = s * attack * 0.32f;
            }

            var clip = AudioClip.Create(name, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Short filtered-noise tick for spark bounces off room surfaces.</summary>
        public static AudioClip Thud(string name, float seconds = 0.12f)
        {
            int samples = Mathf.CeilToInt(seconds * SampleRate);
            var data = new float[samples];
            var rng = new System.Random(1234);
            float lp = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += 0.08f * (noise - lp);
                float body = Mathf.Sin(2f * Mathf.PI * 140f * t) * Mathf.Exp(-40f * t);
                data[i] = (lp * 0.9f + body * 0.6f) * Mathf.Exp(-28f * t) * 0.6f;
            }
            var clip = AudioClip.Create(name, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
