using System;
using Newtonsoft.Json.Linq;

namespace QuantumGetaway.Core.Tunnel
{
    /// <summary>Tap the key for a Hop (short tunnel, sometimes bounces), hold it for a Leap (long, fractal).</summary>
    public enum TunnelKind { Hop, Leap }

    /// <summary>
    /// The quantum part: you are one bright pixel on a 32x32 dark grid laid over the street ahead.
    /// Quantum Blur (blur-v1) puts the image into a 10-qubit state and rotates every qubit. That
    /// spreads you into interference echoes 1, 3, 7 and 15 cells away (powers of two, because each
    /// qubit is one bit of the cell's position), which a classical blur can't do. The blurred image
    /// is your landing distribution, and the tunnel measures it.
    /// </summary>
    public static class Tunnels
    {
        public const string Engine = "blur-v1";
        public const int Size = 32;

        /// <summary>Your pixel: the middle of the back row, so the grid reaches ahead and to both sides.</summary>
        public const int OriginX = 15;
        public const int OriginY = 0;

        /// <summary>
        /// Rotation strength and reach (0 = only neighbouring cells, 1 = anywhere). Measured on the
        /// simulator on 7 Nov 2026: Hop stays put 15% of the time, hops under 3 cells 43%, and goes
        /// further 41% (mean 5.9 cells). Leap never stays and lands 16+ cells away 69% of the time (mean 20).
        /// </summary>
        public static double Strength(TunnelKind kind) => kind == TunnelKind.Hop ? 0.4 : 0.8;

        public static double Reach(TunnelKind kind) => kind == TunnelKind.Hop ? 0.5 : 0.7;

        public static string Name(TunnelKind kind) => kind == TunnelKind.Hop ? "HOP" : "LEAP";

        /// <summary>The input image: black everywhere except your cell.</summary>
        public static byte[] InputPng()
        {
            var pixels = new byte[Size * Size];
            pixels[OriginY * Size + OriginX] = 255;
            return Png.EncodeGray(Size, Size, pixels);
        }

        /// <summary>blur-v1 params. mode: sim (exact), fake (noisy snapshot of an IBM chip), qpu (real IBM chip).</summary>
        public static JObject Params(TunnelKind kind, string mode, string backend)
        {
            var p = new JObject
            {
                ["strength"] = Strength(kind),
                ["reach"] = Reach(kind),
                ["style"] = "rx",
                ["size"] = Size,
            };
            var chip = string.IsNullOrWhiteSpace(backend) ? "marrakesh" : backend.Trim().ToLowerInvariant();
            if (mode == "fake")
            {
                p["machine"] = "fake_" + chip;
                p["shots"] = 4096;
            }
            else if (mode == "qpu")
            {
                p["machine"] = string.IsNullOrWhiteSpace(backend) ? "least_busy" : "ibm_" + chip;
                p["shots"] = 4096;
            }
            return p;
        }

        public static ShotSource SourceOf(string mode)
        {
            switch (mode)
            {
                case "fake": return ShotSource.NoisyChip;
                case "qpu": return ShotSource.IbmHardware;
                default: return ShotSource.Simulator;
            }
        }

        /// <summary>The URL of the blurred image in a blur-v1 result.</summary>
        public static string OutputUrl(JObject result)
        {
            if (result["outputs"] is JArray outputs)
                foreach (var o in outputs)
                    if ((string)o["slot"] == "result" && o["url"] != null)
                        return (string)o["url"];
            throw new FormatException("blur-v1 result has no 'result' output");
        }
    }
}
