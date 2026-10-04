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

        /// <summary>
        /// Karplus-Strong plucked string: a noise burst circulating through a damped delay line one period long.
        /// Low damping rings like a taut band (the release twang); high damping gives a muted tick (the pull ratchet).
        /// Brightness 0..1 sets how much the loop filter dulls each pass.
        /// </summary>
        public static AudioClip Pluck(string name, float frequency, float seconds, float damping, float brightness, int seed = 5)
        {
            int samples = Mathf.CeilToInt(seconds * SampleRate);
            var data = new float[samples];
            var rng = new System.Random(seed);
            int period = Mathf.Max(2, Mathf.RoundToInt(SampleRate / frequency));
            var line = new float[period];
            for (int i = 0; i < period; i++) line[i] = (float)(rng.NextDouble() * 2.0 - 1.0);
            float blend = Mathf.Lerp(0.5f, 0.05f, Mathf.Clamp01(brightness)); // weight of the previous sample
            float loss = Mathf.Exp(-damping / frequency);                       // per-period energy loss
            int p = 0;
            float prev = 0f;
            for (int i = 0; i < samples; i++)
            {
                float cur = line[p];
                float next = (cur * (1f - blend) + prev * blend) * loss;
                prev = cur;
                line[p] = next;
                p = (p + 1) % period;
                float t = (float)i / SampleRate;
                data[i] = cur * Mathf.Clamp01((seconds - t) / 0.03f) * 0.45f;
            }
            var clip = AudioClip.Create(name, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>
        /// A zipper of light: a train of tiny high-passed clicks (the teeth meeting) whose rate climbs from ~28 to
        /// ~110 per second, over a whistle rising two octaves. It stops dead at the end (the seal pop follows).
        /// </summary>
        public static AudioClip Zip(string name, float seconds = 0.55f, int seed = 51)
        {
            int samples = Mathf.CeilToInt(seconds * SampleRate);
            var data = new float[samples];
            var rng = new System.Random(seed);
            float clickPhase = 1f, clickEnv = 0f, lp = 0f, phase = 0f;
            float clickDecay = Mathf.Exp(-1f / (0.004f * SampleRate));
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;
                float k = t / seconds;
                clickPhase += Mathf.Lerp(28f, 110f, k * k) / SampleRate;
                if (clickPhase >= 1f) { clickPhase -= 1f; clickEnv = 0.6f + 0.4f * (float)rng.NextDouble(); }
                clickEnv *= clickDecay;
                float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += 0.15f * (n - lp);
                float click = (n - lp) * clickEnv;
                phase += 2f * Mathf.PI * 500f * Mathf.Pow(4f, k) / SampleRate;
                float whistle = Mathf.Sin(phase) * 0.22f * k;
                float env = Mathf.Clamp01(t / 0.02f) * Mathf.Clamp01((seconds - t) / 0.01f);
                data[i] = (click * 0.9f + whistle) * env * 0.5f;
            }
            var clip = AudioClip.Create(name, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>
        /// Something made of light breaking: a low boom falling an octave, a short noise burst, and a spray of glassy
        /// pings (short high partials at scattered onsets, densest at the start).
        /// </summary>
        public static AudioClip Shatter(string name, float seconds = 1.6f, int seed = 41)
        {
            const int Pings = 18;
            int samples = Mathf.CeilToInt(seconds * SampleRate);
            var data = new float[samples];
            var rng = new System.Random(seed);
            var onset = new float[Pings];
            var freq = new float[Pings];
            var amp = new float[Pings];
            var decay = new float[Pings];
            for (int p = 0; p < Pings; p++)
            {
                float r = (float)rng.NextDouble();
                onset[p] = 0.3f * r * r;
                freq[p] = 1800f + 3400f * (float)rng.NextDouble();
                amp[p] = 0.3f + 0.7f * (float)rng.NextDouble();
                decay[p] = 18f + 22f * (float)rng.NextDouble();
            }
            float lp = 0f, boomPhase = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / SampleRate;
                boomPhase += 2f * Mathf.PI * 70f * Mathf.Pow(0.5f, t / 0.6f) / SampleRate;
                float boom = Mathf.Sin(boomPhase) * Mathf.Exp(-3f * t) * 0.9f;
                float n = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += 0.2f * (n - lp);
                float burst = (n - lp) * Mathf.Exp(-14f * t) * 0.5f;
                float pings = 0f;
                for (int p = 0; p < Pings; p++)
                {
                    float u = t - onset[p];
                    if (u > 0f) pings += Mathf.Sin(2f * Mathf.PI * freq[p] * u) * amp[p] * Mathf.Exp(-decay[p] * u);
                }
                float env = Mathf.Clamp01(t / 0.003f) * Mathf.Clamp01((seconds - t) / 0.05f);
                data[i] = (boom + burst + pings * 0.22f) * env * 0.45f;
            }
            var clip = AudioClip.Create(name, samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>
        /// The rift's hum: a low root and fifth with slowly beating detuned partners, a faint airy overtone and a slow
        /// breath. Every frequency completes a whole number of cycles in the clip, so it loops without a seam.
        /// </summary>
        public static AudioClip Drone(string name, float seconds = 4f)
        {
            int samples = Mathf.RoundToInt(seconds * SampleRate);
            var data = new float[samples];
            // Cycles per loop (frequency = cycles / seconds): 55 Hz root, a partner 0.25 Hz off, the fifth, the octave
            // a hair sharp, a high overtone; the breath (amplitude) cycles twice per loop.
            float[] cycles = { 220f, 221f, 330f, 442f, 3522f };
            float[] gains = { 0.5f, 0.35f, 0.3f, 0.18f, 0.025f };
            for (int i = 0; i < samples; i++)
            {
                float k = (float)i / samples;
                float s = 0f;
                for (int p = 0; p < cycles.Length; p++) s += gains[p] * Mathf.Sin(2f * Mathf.PI * cycles[p] * k + p);
                float breath = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 2f * k);
                data[i] = s * breath * 0.35f;
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
