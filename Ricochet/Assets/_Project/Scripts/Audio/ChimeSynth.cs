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

        /// <summary>
        /// One second of snare roll that loops seamlessly (a whole number of strokes per loop), with alternating
        /// hand accents and slight timing humanization. The crescendo comes from the playback volume.
        /// </summary>
        public static AudioClip Drumroll(string name, int strokesPerSecond = 22)
        {
            int samples = SampleRate;
            var data = new float[samples];
            var rng = new System.Random(77);
            int period = samples / strokesPerSecond;
            for (int k = 0; k < strokesPerSecond; k++)
            {
                int start = k * period + rng.Next(-period / 8, period / 8 + 1);
                float accent = (k % 2 == 0 ? 1f : 0.78f) * (0.9f + 0.2f * (float)rng.NextDouble());
                float bp = 0f, lp = 0f;
                for (int j = 0; j < period * 2; j++)
                {
                    float t = (float)j / SampleRate;
                    float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                    lp += 0.35f * (noise - lp);           // crude band-pass: low-passed noise minus its slower average
                    bp += 0.05f * (lp - bp);
                    float snare = (lp - bp) * Mathf.Exp(-38f * t);
                    float skin = Mathf.Sin(2f * Mathf.PI * 190f * t) * Mathf.Exp(-60f * t) * 0.35f;
                    int i = (start + j + samples) % samples; // wrap so the loop point is seamless
                    data[i] += (snare + skin) * accent * 0.55f;
                }
            }
            var clip = AudioClip.Create(name, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>
        /// A pitch glide from f0 to f1 (exponential), a few harmonics plus filtered noise, with a soft attack and an
        /// exponential tail. One recipe covers rumbles, squeals, whooshes and booms.
        /// </summary>
        public static AudioClip Sweep(string name, float f0, float f1, float seconds, float noise, float decay, int seed = 7)
        {
            int samples = Mathf.CeilToInt(seconds * SampleRate);
            var data = new float[samples];
            var rng = new System.Random(seed);
            float phase = 0f, lp = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;
                float k = t / seconds;
                float f = f0 * Mathf.Pow(f1 / f0, k);
                phase += 2f * Mathf.PI * f / SampleRate;
                float tone = Mathf.Sin(phase) + 0.35f * Mathf.Sin(2f * phase) + 0.15f * Mathf.Sin(3f * phase + 1f);
                float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += Mathf.Clamp01(f * 4f / SampleRate) * (n - lp);   // noise band follows the pitch
                float env = Mathf.Clamp01(t / 0.01f) * Mathf.Exp(-decay * t) * Mathf.Clamp01((seconds - t) / 0.05f);
                data[i] = (tone * (1f - noise) + lp * 2.5f * noise) * env * 0.4f;
            }
            var clip = AudioClip.Create(name, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>A bright, long crash: high-passed noise plus a shimmer of inharmonic partials.</summary>
        public static AudioClip Crash(string name, float seconds = 2.2f)
        {
            int samples = Mathf.CeilToInt(seconds * SampleRate);
            var data = new float[samples];
            var rng = new System.Random(4321);
            float lp = 0f;
            float[] partial = { 3150f, 4420f, 5870f, 7330f };
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += 0.25f * (noise - lp);
                float hiss = (noise - lp) * Mathf.Exp(-2.2f * t);
                float shimmer = 0f;
                for (int p = 0; p < partial.Length; p++)
                    shimmer += Mathf.Sin(2f * Mathf.PI * partial[p] * t + p) * Mathf.Exp(-(3f + p) * t);
                float attack = Mathf.Clamp01(t / 0.002f);
                data[i] = (hiss * 0.5f + shimmer * 0.06f) * attack * 0.7f;
            }
            var clip = AudioClip.Create(name, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
