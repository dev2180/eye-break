using System;
using System.IO;
using System.Media;
using System.Threading.Tasks;

namespace EyeBreakSetup
{
    /// <summary>
    /// Ambient setup music, generated at startup: a slow pad drifting between D minor and B-flat, a low drone and
    /// sparse bells. Every frequency and swell repeats a whole number of times in the loop, so the loop has no seam.
    /// ponytail: synthesized instead of a bundled track, so there's no file to license and nothing added to the size.
    /// </summary>
    static class Music
    {
        const int Rate = 22050;
        const double L = 24;   // loop length, seconds
        static SoundPlayer _player;
        public static bool On { get; private set; }

        public static void Start() => Task.Run(() =>
        {
            try
            {
                var p = new SoundPlayer(Build());
                p.Load();
                _player = p;
                p.PlayLooping();
                On = true;
            }
            catch { }   // no sound device: setup works silently
        });

        public static void Toggle()
        {
            if (_player == null) return;
            if (On) _player.Stop(); else _player.PlayLooping();
            On = !On;
        }

        public static void Stop() { _player?.Stop(); On = false; }

        static MemoryStream Build()
        {
            int n = (int)(Rate * L);
            var buf = new double[n];

            void Tone(double hz, double amp, Func<double, double> env)
            {
                double f = Math.Round(hz * L) / L;   // whole cycles per loop
                for (int i = 0; i < n; i++)
                {
                    double t = (double)i / Rate;
                    buf[i] += amp * env(t) * Math.Sin(2 * Math.PI * f * t);
                }
            }
            // Pad note: two slightly detuned voices for a soft chorus, breathing slowly.
            void Pad(double hz, double amp, Func<double, double> chord, int breath)
            {
                Func<double, double> env = t => chord(t) * (0.75 + 0.25 * Math.Sin(2 * Math.PI * breath * t / L + hz));
                Tone(hz, amp, env);
                Tone(hz + 0.25, amp * 0.8, env);
            }

            Func<double, double> dm = t => 0.5 + 0.5 * Math.Cos(2 * Math.PI * t / L);   // swells in the first half
            Func<double, double> bb = t => 1 - dm(t);                                   // and this in the second

            Tone(73.42, 0.16, t => 1);   // D drone under everything
            double[] dMinor = { 146.83, 220.00, 293.66, 349.23, 440.00 };
            double[] bFlat = { 116.54, 174.61, 293.66, 349.23, 440.00 };
            for (int k = 0; k < 5; k++)
            {
                double amp = 0.07 - k * 0.008;
                Pad(dMinor[k], amp, dm, 2 + k % 3);
                Pad(bFlat[k], amp, bb, 3 - k % 2);
            }
            Pad(659.25, 0.02, dm, 4);
            Pad(587.33, 0.02, bb, 4);

            // Sparse bells, D pentatonic, each fading out well before the loop ends.
            double[] times = { 1.5, 4.9, 7.4, 10.2, 13.6, 16.3, 18.8, 20.6 };
            double[] notes = { 587.33, 880.00, 698.46, 1174.66, 783.99, 880.00, 587.33, 1046.50 };
            for (int b = 0; b < times.Length; b++)
            {
                double t0 = times[b], f = notes[b];
                int start = (int)(t0 * Rate);
                for (int i = start; i < n; i++)
                {
                    double dt = (double)(i - start) / Rate;
                    double a = 0.045 * Math.Exp(-dt * 1.3) * Math.Min(1, dt * 200);
                    buf[i] += a * (Math.Sin(2 * Math.PI * f * dt) + 0.3 * Math.Sin(2 * Math.PI * f * 2.01 * dt));
                }
            }

            double peak = 1e-9;
            foreach (double v in buf) peak = Math.Max(peak, Math.Abs(v));
            double gain = 0.45 * short.MaxValue / peak;

            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + n * 2);
            w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' }); w.Write(16);
            w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(n * 2);
            foreach (double v in buf) w.Write((short)(v * gain));
            ms.Position = 0;
            return ms;
        }
    }
}
