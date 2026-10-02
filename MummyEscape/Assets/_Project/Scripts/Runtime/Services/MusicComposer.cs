using System;
using UnityEngine;

namespace MummyEscape.Services
{
    /// <summary>
    /// Coded music, one theme per act, in the spirit of Strudel / TidalCycles: each part is a pattern string of
    /// steps ("0 ~ 2 _ 4" = scale degree, rest, hold) played by a small synth voice. Everything is rendered offline
    /// into a buffer whose length is a whole number of bars; notes and echoes running past the end wrap around to the
    /// start, so the loop is seamless. Pure C# (no Unity objects): safe to render on a worker thread.
    /// <para>Pattern tokens: an integer is a degree of the act's scale (negative or ≥ 7 change octave), <c>~</c> is a
    /// rest, <c>_</c> holds the previous note one more step, letters trigger drums (see each part).</para>
    /// </summary>
    public static class MusicComposer
    {
        public const int Rate = 22050;

        // Modes as semitone offsets.
        static readonly int[] Hijaz = { 0, 1, 4, 5, 7, 8, 10 };          // phrygian dominant: "Egyptian"
        static readonly int[] Dorian = { 0, 2, 3, 5, 7, 9, 10 };
        static readonly int[] Locrian = { 0, 1, 3, 5, 6, 8, 10 };
        static readonly int[] DoubleHarmonic = { 0, 1, 4, 5, 7, 8, 11 };

        /// <summary>Renders the loop of an act (1..5). Returns mono samples at <see cref="Rate"/>.</summary>
        public static float[] Render(int act)
        {
            switch (act)
            {
                case 2: return Flooded();
                case 3: return Ruins();
                case 4: return Tech();
                case 5: return Inferno();
                default: return Sandstone();
            }
        }

        // ================================================================== act themes

        /// <summary>Act 1 — Antechamber: oud over a drone, darbuka in maqsum rhythm, a ney flute answers.</summary>
        static float[] Sandstone()
        {
            var t = new Track(92f, 16, 50, Hijaz); // D
            t.Drone(new[] { -14, -10 }, 0.16f, tremolo: 0.25f);   // D1 + A1 (degrees -14 = two octaves down)
            // Maqsum: doum tek ~ tek doum ~ tek ~ (eighths), from bar 3.
            t.Drums("D T ~ T D ~ T ~", 8, 2, 16, (s, v) => s == 'D' ? Doum(v) : Tek(v), 0.5f);
            string[] oud =
            {
                "0 ~ 1 2 ~ 1 0 ~ 4 ~ 3 2 1 ~ 0 ~",
                "4 5 4 ~ 2 3 2 ~ 1 2 1 ~ 0 ~ ~ ~",
                "7 ~ 6 5 4 ~ 5 4 2 ~ 1 ~ 0 ~ ~ ~",
                "0 1 2 1 0 ~ -1 ~ 0 ~ ~ ~ ~ ~ ~ ~",
            };
            for (int phrase = 0; phrase < 6; phrase++)
                t.Melody(oud[phrase % 4], 8, 4 + phrase * 2, Oud, 0.32f, octave: 0);
            t.Melody("~ ~ ~ ~ ~ ~ ~ ~ 4 _ _ _ 5 _ 4 _ 2 _ _ _ 1 _ _ _ 0 _ _ _ _ _ _ _", 8, 8, Ney, 0.22f, octave: 1);
            t.Melody("7 _ _ _ 8 _ 7 _ 5 _ 4 _ _ _ _ _ 4 _ 5 _ 4 _ 2 _ 1 _ _ _ 0 _ _ _", 8, 12, Ney, 0.22f, octave: 1);
            t.Echo(0.75f, 0.25f);
            return t.Master();
        }

        /// <summary>Act 2 — Flooded galleries: slow pads, glassy bells echoing, drips and the swell of the water.</summary>
        static float[] Flooded()
        {
            var t = new Track(68f, 8, 57, Dorian); // A
            int[][] chords = { new[] { 0, 2, 4 }, new[] { -1, 1, 3 }, new[] { 3, 5, 7 }, new[] { 0, 2, 4 } };
            for (int c = 0; c < 4; c++)
                foreach (int d in chords[c]) t.Note(c * 2 * t.Bar, 2 * t.Bar + 1.5f, t.Hz(d - 7), (f, x, dur) => Pad(f, x, dur, 1.6f), 0.12f);
            t.Melody("4 ~ 2 ~ 7 ~ ~ 5 ~ ~ 4 ~ 2 ~ ~ ~", 4, 0, Bell, 0.2f, octave: 1, repeat: 2);
            t.Melody("~ ~ 9 ~ ~ 7 ~ ~ 8 ~ ~ ~ 4 ~ ~ ~", 4, 4, Bell, 0.14f, octave: 1);
            t.Drums("S ~ ~ ~", 4, 0, 8, (s, v) => Sub(v, t.Hz(-14)), 0.35f);
            var rng = new System.Random(2);
            for (int i = 0; i < 14; i++) t.Note((float)rng.NextDouble() * t.Length, 0.25f, 1400f + (float)rng.NextDouble() * 900f, (f, x, d) => Drip(f, x), 0.12f);
            t.Noise(0.11f, lowpass: 0.02f, swells: 4);
            t.Echo(t.Beat * 0.75f, 0.4f);
            t.Echo(t.Beat * 1.5f, 0.25f);
            return t.Master();
        }

        /// <summary>Act 3 — Collapsed ruins: a grinding low drone with a tritone, distant drums, lonely notes, falling stones.</summary>
        static float[] Ruins()
        {
            var t = new Track(60f, 8, 48, Locrian); // C
            t.Drone(new[] { -14, -7 }, 0.15f, tremolo: 0.1f, saw: true);
            for (int bar = 3; bar < 8; bar += 4) t.Note(bar * t.Bar, 4 * t.Bar, t.Hz(-10), (f, x, d) => Pad(f, x, d, 3f), 0.12f); // tritone swell
            t.Drums("B ~ ~ ~ ~ ~ ~ ~ | B ~ ~ ~ ~ ~ ~ ~ | B ~ ~ ~ ~ ~ ~ ~ | B ~ ~ ~ b ~ b ~", 8, 0, 8, (s, v) => Taiko(v, s == 'B' ? 1f : 0.6f), 0.4f);
            t.Melody("0 ~ ~ 1 ~ ~ 3 ~ 2 ~ ~ ~ 1 ~ 0 ~ ~ ~ 5 ~ 4 ~ ~ 3 1 ~ ~ ~ ~ ~ ~ ~", 4, 0, Piano, 0.28f, octave: 0);
            var rng = new System.Random(3);
            for (int i = 0; i < 5; i++) t.Note((float)rng.NextDouble() * t.Length, 1.2f, 0f, (f, x, d) => Rubble(x), 0.18f);
            t.Noise(0.13f, lowpass: 0.012f, swells: 2);
            t.Echo(t.Beat * 1.5f, 0.3f);
            return t.Master();
        }

        /// <summary>Act 4 — City of Anubis: Egyptian scale on synths — four on the floor, sub bass, 16th arpeggio, square lead.</summary>
        static float[] Tech()
        {
            var t = new Track(112f, 16, 52, Hijaz); // E
            t.Drums("K ~ ~ ~ K ~ ~ ~ K ~ ~ ~ K ~ ~ ~", 16, 2, 16, (s, v) => Kick(v), 0.55f);
            t.Drums("~ ~ H ~ ~ ~ H ~ ~ ~ H ~ ~ ~ H h", 16, 4, 16, (s, v) => Hat(v, s == 'H' ? 1f : 0.5f), 0.22f);
            t.Drums("~ ~ ~ ~ C ~ ~ ~ ~ ~ ~ ~ C ~ ~ ~", 16, 8, 16, (s, v) => Clap(v), 0.25f);
            int[] roots = { 0, 0, 5, 5, 1, 1, 0, 0 };
            for (int bar = 0; bar < 16; bar++)
            {
                int r = roots[bar % 8];
                t.Melody($"{r} {r} ~ {r} {r + 1} ~ {r} ~", 8, bar, (f, x, d) => SquareBass(f, x, d), 0.24f, octave: -2);
                t.Melody($"{r} {r + 2} {r + 4} {r + 7} {r + 4} {r + 2} {r} {r + 2} {r + 4} {r + 7} {r + 9} {r + 7} {r + 4} {r + 2} {r + 4} {r + 7}",
                         16, bar, (f, x, d) => SawPluck(f, x, d, bar), 0.1f, octave: 0);
            }
            t.Melody("7 _ 8 7 4 _ 5 4 2 _ 1 _ 0 _ _ _ 4 _ 5 _ 7 _ 8 _ 7 _ _ _ ~ ~ ~ ~", 8, 8, Lead, 0.16f, octave: 1, repeat: 2);
            t.Echo(t.Beat * 0.75f, 0.3f);
            return t.Master();
        }

        /// <summary>Act 5 — Burning sanctuary: taiko, a saturated drone, oud tremolo, a choir and the crackling fire.</summary>
        static float[] Inferno()
        {
            var t = new Track(126f, 16, 50, DoubleHarmonic); // D
            t.Drone(new[] { -14, -10 }, 0.18f, tremolo: 0.5f, saw: true, drive: 2.5f);
            t.Drums("B ~ b B ~ b B b", 8, 0, 16, (s, v) => Taiko(v, s == 'B' ? 1f : 0.55f), 0.5f);
            t.Drums("~ ~ ~ ~ T ~ ~ ~ ~ ~ ~ ~ T ~ T ~", 16, 4, 16, (s, v) => Tek(v), 0.3f);
            string[] riff = { "0 1 4 5 4 1 0 ~", "7 8 7 5 4 _ _ ~", "4 5 7 8 7 5 4 1", "0 _ 1 _ 0 _ _ ~" };
            for (int bar = 2; bar < 16; bar++)
                t.Tremolo(riff[bar % 4], 8, bar, 0.2f);
            t.Melody("0 _ _ _ _ _ _ _ 1 _ _ _ _ _ _ _ 4 _ _ _ _ _ _ _ 0 _ _ _ _ _ _ _", 4, 8, Choir, 0.17f, octave: 0);
            var rng = new System.Random(5);
            for (int i = 0; i < 70; i++) t.Note((float)rng.NextDouble() * t.Length, 0.02f, 0f, (f, x, d) => Crackle(x), 0.1f + 0.1f * (float)rng.NextDouble());
            t.Noise(0.05f, lowpass: 0.05f, swells: 8);
            return t.Master();
        }

        // ================================================================== voices
        // A voice maps (frequency, seconds since note start, note duration) to a sample.

        delegate float Voice(float freq, float t, float dur);

        static float Sin(float f, float t) => Mathf.Sin(2f * Mathf.PI * f * t);

        static float Attack(float t, float a) => t < a ? t / a : 1f;

        /// <summary>Fade at the end of a held note (avoids clicks).</summary>
        static float Release(float t, float dur, float r) => t > dur - r ? Mathf.Max(0f, (dur - t) / r) : 1f;

        static float Oud(float f, float t, float dur)
        {
            float vib = 1f + 0.004f * Mathf.Sin(t * 30f) * Mathf.Min(1f, t * 3f);
            float s = Sin(f * vib, t) + 0.6f * Sin(2f * f * vib, t) * Mathf.Exp(-t * 6f) + 0.35f * Sin(3f * f, t) * Mathf.Exp(-t * 10f)
                      + 0.2f * Sin(4.02f * f, t) * Mathf.Exp(-t * 14f);
            return s * Mathf.Exp(-t * 4.5f) * Attack(t, 0.003f) * 0.6f;
        }

        static float Ney(float f, float t, float dur)
        {
            float vib = 1f + 0.006f * Mathf.Sin(t * 33f) * Mathf.Min(1f, t * 1.5f);
            float breath = 0.15f * Noise((int)(t * Rate) + (int)f) * Attack(t, 0.05f) * Mathf.Exp(-t * 3f);
            return (Sin(f * vib, t) + 0.15f * Sin(2f * f * vib, t) + breath) * Attack(t, 0.12f) * Release(t, dur, 0.2f);
        }

        static float Bell(float f, float t, float dur)
            => (Sin(f, t) + 0.45f * Sin(2.76f * f, t) * Mathf.Exp(-t * 3f) + 0.2f * Sin(5.4f * f, t) * Mathf.Exp(-t * 6f))
               * Mathf.Exp(-t * 1.6f) * Attack(t, 0.004f) * 0.6f;

        static float Pad(float f, float t, float dur, float attack)
            => (Sin(f, t) + Sin(f * 1.004f, t) + 0.5f * Sin(f * 2.003f, t) + 0.25f * Sin(f * 3.001f, t))
               * Attack(t, attack) * Release(t, dur, Mathf.Min(attack, dur * 0.4f)) * 0.3f;

        static float Piano(float f, float t, float dur)
            => (Sin(f, t) + 0.5f * Sin(2f * f, t) * Mathf.Exp(-t * 2f) + 0.25f * Sin(3.01f * f, t) * Mathf.Exp(-t * 4f))
               * Mathf.Exp(-t * 1.2f) * Attack(t, 0.005f) * 0.6f;

        static float Saw(float f, float t, int harmonics)
        {
            float s = 0f;
            for (int k = 1; k <= harmonics; k++) s += Sin(k * f, t) / k;
            return s * 0.6f;
        }

        static float SawPluck(float f, float t, float dur, int bar)
        {
            // The "filter" opens over 8 bars: more harmonics, longer notes.
            int h = 3 + (bar % 8);
            return Saw(f, t, h) * Mathf.Exp(-t * (14f - bar % 8)) * Attack(t, 0.002f);
        }

        static float SquareBass(float f, float t, float dur)
        {
            float s = 0f;
            for (int k = 1; k <= 7; k += 2) s += Sin(k * f, t) / k;
            return s * Mathf.Exp(-t * 5f) * Attack(t, 0.003f) * Release(t, dur, 0.02f);
        }

        static float Lead(float f, float t, float dur)
        {
            float vib = 1f + 0.008f * Mathf.Sin(t * 36f) * Mathf.Min(1f, t * 2f);
            float s = 0f;
            for (int k = 1; k <= 5; k += 2) s += Sin(k * f * vib, t) / k;
            return s * Attack(t, 0.01f) * Release(t, dur, 0.06f) * 0.8f;
        }

        static float Choir(float f, float t, float dur)
        {
            // Several detuned voices with an "ah" formant (strong 2nd and 3rd partials).
            float s = 0f;
            float[] det = { 1f, 1.006f, 0.994f, 2.003f };
            foreach (var d in det) s += Sin(f * d, t) + 0.6f * Sin(2f * f * d, t) + 0.4f * Sin(3f * f * d, t);
            return s * Attack(t, 0.8f) * Release(t, dur, 0.8f) * 0.12f;
        }

        // Drums: no pitch argument (v = seconds since hit).
        static float Kick(float t) => Mathf.Sin(2f * Mathf.PI * (48f * t + 110f / 28f * (1f - Mathf.Exp(-28f * t)))) * Mathf.Exp(-t * 7f);

        static float Doum(float t) => Mathf.Sin(2f * Mathf.PI * (75f * t + 60f / 20f * (1f - Mathf.Exp(-20f * t)))) * Mathf.Exp(-t * 9f)
                                      + Noise((int)(t * Rate)) * Mathf.Exp(-t * 60f) * 0.2f;

        static float Tek(float t) => (Noise((int)(t * Rate)) * 0.7f + Sin(820f, t) * 0.4f) * Mathf.Exp(-t * 45f);

        static float Hat(float t, float v)
        {
            int i = (int)(t * Rate);
            return (Noise(i) - Noise(i + 1)) * 0.5f * Mathf.Exp(-t * (v > 0.7f ? 70f : 120f)) * v;
        }

        static float Clap(float t)
        {
            float env = Mathf.Exp(-t * 25f) + (t > 0.012f ? Mathf.Exp(-(t - 0.012f) * 30f) * 0.6f : 0f);
            return Noise((int)(t * Rate) * 3) * env * 0.6f;
        }

        static float Taiko(float t, float v)
            => (Mathf.Sin(2f * Mathf.PI * (62f * t + 45f / 10f * (1f - Mathf.Exp(-10f * t)))) * Mathf.Exp(-t * 4.5f)
                + Noise((int)(t * Rate)) * Mathf.Exp(-t * 30f) * 0.35f) * v;

        static float Sub(float t, float f) => Sin(f, t) * Mathf.Exp(-t * 2.5f) * Attack(t, 0.01f);

        static float Drip(float f, float t) => Mathf.Sin(2f * Mathf.PI * f * t * (1f + t * 6f)) * Mathf.Exp(-t * 28f) * Attack(t, 0.001f);

        static float Rubble(float t)
        {
            int i = (int)(t * Rate);
            // Low rumble with a few grains.
            float rumble = (Noise(i / 9) + Noise(i / 9 + 1)) * 0.5f;
            float grains = Noise(i) * (Noise(i / 600) > 0.6f ? 1f : 0f) * 0.4f;
            return (rumble + grains) * Mathf.Exp(-t * 2.5f) * Attack(t, 0.05f);
        }

        static float Crackle(float t) => Noise((int)(t * Rate) * 7) * Mathf.Exp(-t * 300f);

        static float Noise(int i)
        {
            unchecked
            {
                uint x = (uint)i * 747796405u + 2891336453u;
                x = ((x >> (int)((x >> 28) + 4u)) ^ x) * 277803737u;
                return ((x >> 22) ^ x) / (float)uint.MaxValue * 2f - 1f;
            }
        }

        // ================================================================== sequencer

        sealed class Track
        {
            readonly float[] _buf;
            readonly int _root;
            readonly int[] _scale;
            public readonly int N;
            public readonly float Beat, Bar, Length;

            /// <param name="root">MIDI note of degree 0.</param>
            public Track(float bpm, int bars, int root, int[] scale)
            {
                Beat = 60f / bpm;
                Bar = Beat * 4f;
                N = Mathf.RoundToInt(bars * Bar * Rate);
                Length = N / (float)Rate;
                _buf = new float[N];
                _root = root;
                _scale = scale;
            }

            public float Hz(int degree)
            {
                int oct = Mathf.FloorToInt(degree / 7f);
                int idx = degree - oct * 7;
                int midi = _root + oct * 12 + _scale[idx];
                return 440f * Mathf.Pow(2f, (midi - 69) / 12f);
            }

            /// <summary>Adds one note; whatever runs past the end of the loop wraps to its start.</summary>
            public void Note(float start, float dur, float freq, Voice voice, float gain)
            {
                int s0 = Mathf.RoundToInt(start * Rate);
                int len = Mathf.RoundToInt(dur * Rate);
                for (int i = 0; i < len; i++)
                {
                    int idx = (s0 + i) % N;
                    if (idx < 0) idx += N;
                    _buf[idx] += voice(freq, i / (float)Rate, dur) * gain;
                }
            }

            static string[] Steps(string pattern) => pattern.Replace("|", " ").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            /// <summary>
            /// Plays a degree pattern starting at <paramref name="startBar"/>, <paramref name="stepsPerBar"/> steps per bar.
            /// Each note lasts until the next token that is not "_", plus a short tail for ringing voices.
            /// </summary>
            public void Melody(string pattern, int stepsPerBar, int startBar, Voice voice, float gain, int octave = 0, int repeat = 1)
            {
                var steps = Steps(pattern);
                float step = Bar / stepsPerBar;
                for (int r = 0; r < repeat; r++)
                {
                    float origin = startBar * Bar + r * steps.Length * step;
                    for (int i = 0; i < steps.Length; i++)
                    {
                        if (!int.TryParse(steps[i], out int degree)) continue;
                        int hold = 1;
                        while (i + hold < steps.Length && steps[i + hold] == "_") hold++;
                        Note(origin + i * step, hold * step + 0.6f, Hz(degree + octave * 7), voice, gain);
                    }
                }
            }

            /// <summary>Oud tremolo: every note of the pattern is picked as fast repeated 16ths... of 16ths.</summary>
            public void Tremolo(string pattern, int stepsPerBar, int bar, float gain)
            {
                var steps = Steps(pattern);
                float step = Bar / stepsPerBar;
                int last = 0;
                for (int i = 0; i < steps.Length; i++)
                {
                    if (steps[i] == "~") continue;
                    if (int.TryParse(steps[i], out int d)) last = d;
                    float sub = step / 4f;
                    for (int k = 0; k < 4; k++)
                        Note(bar * Bar + i * step + k * sub, sub + 0.08f, Hz(last), Oud, gain * (k == 0 ? 1f : 0.65f));
                }
            }

            /// <summary>Drum pattern repeated every bar from <paramref name="fromBar"/> to <paramref name="toBar"/> (exclusive).
            /// A pattern longer than one bar ("|" separated) spans several bars.</summary>
            public void Drums(string pattern, int stepsPerBar, int fromBar, int toBar, Func<char, float, float> hit, float gain)
            {
                var steps = Steps(pattern);
                float step = Bar / stepsPerBar;
                for (float t0 = fromBar * Bar; t0 < toBar * Bar - 1e-3f; t0 += steps.Length * step)
                    for (int i = 0; i < steps.Length; i++)
                    {
                        char c = steps[i][0];
                        if (c == '~' || c == '_') continue;
                        Note(t0 + i * step, 1.2f, 0f, (f, x, d) => hit(c, x), gain);
                    }
            }

            /// <summary>Sustained low notes over the whole loop (slow tremolo, optional saw and saturation).</summary>
            public void Drone(int[] degrees, float gain, float tremolo = 0f, bool saw = false, float drive = 0f)
            {
                foreach (int d in degrees)
                {
                    float f = Hz(d);
                    // Whole number of cycles over the loop so the drone joins up with itself.
                    f = Mathf.Max(1f, Mathf.Round(f * Length)) / Length;
                    float lfo = Mathf.Max(1f, Mathf.Round(Length / 4f)) / Length; // ~4 s tremolo period, loop-aligned
                    for (int i = 0; i < N; i++)
                    {
                        float t = i / (float)Rate;
                        float s = saw ? Saw(f, t, 8) : Sin(f, t) + 0.3f * Sin(2f * f, t);
                        if (drive > 0f) s = (float)Math.Tanh(s * drive) / (float)Math.Tanh(drive);
                        _buf[i] += s * gain * (1f - tremolo * 0.5f * (1f + Mathf.Sin(2f * Mathf.PI * lfo * t)));
                    }
                }
            }

            /// <summary>Filtered noise (wind, water) swelling <paramref name="swells"/> times per loop.</summary>
            public void Noise(float gain, float lowpass, int swells)
            {
                float y = 0f;
                for (int pass = 0; pass < 2; pass++) // first pass warms the filter so the seam has no jump
                    for (int i = 0; i < N; i++)
                    {
                        y += lowpass * (MusicComposer.Noise(i * 13 + 7) - y);
                        if (pass == 0) continue;
                        float t = i / (float)N;
                        float env = 0.35f + 0.65f * Mathf.Pow(0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * swells * t), 2f);
                        _buf[i] += y * gain * env * (1f / Mathf.Sqrt(lowpass)) * 0.5f;
                    }
            }

            /// <summary>Feedback echo, circular so the tail of the loop rings into its start.</summary>
            public void Echo(float delaySeconds, float feedback)
            {
                int d = Mathf.Max(1, Mathf.RoundToInt(delaySeconds * Rate));
                var wet = new float[N];
                for (int pass = 0; pass < 3; pass++)
                    for (int i = 0; i < N; i++)
                    {
                        int j = (i - d) % N;
                        if (j < 0) j += N;
                        wet[i] = (_buf[j] + wet[j]) * feedback;
                    }
                for (int i = 0; i < N; i++) _buf[i] += wet[i];
            }

            /// <summary>Soft-clips and normalises the mix.</summary>
            public float[] Master()
            {
                float peak = 0f;
                for (int i = 0; i < N; i++) peak = Mathf.Max(peak, Mathf.Abs(_buf[i]));
                float pre = peak > 0f ? 1.4f / peak : 1f;
                for (int i = 0; i < N; i++) _buf[i] = (float)Math.Tanh(_buf[i] * pre) * 0.7f;
                return _buf;
            }
        }
    }
}
