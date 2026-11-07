using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace QuantumGetaway.Core
{
    /// <summary>
    /// Just enough PNG for the tunnel: write an 8-bit greyscale image, and read back the 8-bit images
    /// Atlas returns (grey, grey+alpha, RGB, RGBA or palette; not interlaced). No System.Drawing, so it
    /// runs the same in the game and in the tests.
    /// </summary>
    public static class Png
    {
        static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        static readonly uint[] CrcTable = BuildCrcTable();

        public static byte[] EncodeGray(int width, int height, byte[] pixels)
        {
            if (pixels.Length != width * height)
                throw new ArgumentException("pixel count doesn't match the size");
            var raw = new byte[(width + 1) * height];
            for (var y = 0; y < height; y++)
                Buffer.BlockCopy(pixels, y * width, raw, y * (width + 1) + 1, width); // filter 0 (none) per row

            using (var png = new MemoryStream())
            {
                png.Write(Signature, 0, Signature.Length);
                var header = new byte[13];
                WriteBigEndian(header, 0, (uint)width);
                WriteBigEndian(header, 4, (uint)height);
                header[8] = 8; // bit depth
                header[9] = 0; // greyscale
                WriteChunk(png, "IHDR", header);
                WriteChunk(png, "IDAT", Zlib(raw));
                WriteChunk(png, "IEND", new byte[0]);
                return png.ToArray();
            }
        }

        /// <summary>Brightness per pixel (row-major, row 0 at the top): grey as is, colour as the mean of R, G and B.</summary>
        public static byte[] DecodeLuma(byte[] png, out int width, out int height)
        {
            if (png.Length < 8 || !StartsWith(png, Signature))
                throw new FormatException("not a PNG");
            width = height = 0;
            int colorType = -1;
            byte[] palette = null;
            var idat = new MemoryStream();
            var pos = 8;
            while (pos + 8 <= png.Length)
            {
                var length = (int)ReadBigEndian(png, pos);
                var type = Encoding.ASCII.GetString(png, pos + 4, 4);
                var data = pos + 8;
                if (length < 0 || data + length > png.Length)
                    throw new FormatException("truncated PNG chunk");
                switch (type)
                {
                    case "IHDR":
                        width = (int)ReadBigEndian(png, data);
                        height = (int)ReadBigEndian(png, data + 4);
                        if (png[data + 8] != 8)
                            throw new FormatException($"unsupported PNG bit depth {png[data + 8]}");
                        colorType = png[data + 9];
                        if (png[data + 12] != 0)
                            throw new FormatException("interlaced PNGs aren't supported");
                        break;
                    case "PLTE":
                        palette = new byte[length];
                        Buffer.BlockCopy(png, data, palette, 0, length);
                        break;
                    case "IDAT":
                        idat.Write(png, data, length);
                        break;
                }
                if (type == "IEND")
                    break;
                pos = data + length + 4; // skip the CRC
            }

            int channels;
            switch (colorType)
            {
                case 0: channels = 1; break;
                case 2: channels = 3; break;
                case 3: channels = 1; break;
                case 4: channels = 2; break;
                case 6: channels = 4; break;
                default: throw new FormatException($"unsupported PNG colour type {colorType}");
            }
            if (colorType == 3 && palette == null)
                throw new FormatException("palette PNG without a palette");

            var stride = width * channels;
            var raw = Unzlib(idat.ToArray(), (stride + 1) * height);
            var pixels = Unfilter(raw, stride, height, channels);

            var luma = new byte[width * height];
            for (var i = 0; i < luma.Length; i++)
            {
                var p = i * channels;
                switch (colorType)
                {
                    case 0:
                    case 4:
                        luma[i] = pixels[p];
                        break;
                    case 3:
                        var e = pixels[p] * 3;
                        luma[i] = (byte)((palette[e] + palette[e + 1] + palette[e + 2] + 1) / 3);
                        break;
                    default:
                        luma[i] = (byte)((pixels[p] + pixels[p + 1] + pixels[p + 2] + 1) / 3);
                        break;
                }
            }
            return luma;
        }

        static byte[] Unfilter(byte[] raw, int stride, int height, int bpp)
        {
            var output = new byte[stride * height];
            for (var y = 0; y < height; y++)
            {
                var filter = raw[y * (stride + 1)];
                var src = y * (stride + 1) + 1;
                var row = y * stride;
                var prev = row - stride;
                for (var x = 0; x < stride; x++)
                {
                    int a = x >= bpp ? output[row + x - bpp] : 0;
                    int b = y > 0 ? output[prev + x] : 0;
                    int c = x >= bpp && y > 0 ? output[prev + x - bpp] : 0;
                    int value = raw[src + x];
                    switch (filter)
                    {
                        case 0: break;
                        case 1: value += a; break;
                        case 2: value += b; break;
                        case 3: value += (a + b) / 2; break;
                        case 4: value += Paeth(a, b, c); break;
                        default: throw new FormatException($"bad PNG filter {filter}");
                    }
                    output[row + x] = (byte)value;
                }
            }
            return output;
        }

        static int Paeth(int a, int b, int c)
        {
            var p = a + b - c;
            var pa = Math.Abs(p - a);
            var pb = Math.Abs(p - b);
            var pc = Math.Abs(p - c);
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }

        static byte[] Zlib(byte[] data)
        {
            using (var output = new MemoryStream())
            {
                output.WriteByte(0x78);
                output.WriteByte(0x9C);
                using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, true))
                    deflate.Write(data, 0, data.Length);
                var adler = Adler32(data);
                var tail = new byte[4];
                WriteBigEndian(tail, 0, adler);
                output.Write(tail, 0, 4);
                return output.ToArray();
            }
        }

        static byte[] Unzlib(byte[] data, int expected)
        {
            if (data.Length < 2)
                throw new FormatException("empty PNG image data");
            using (var input = new MemoryStream(data, 2, data.Length - 2)) // skip the 2-byte zlib header
            using (var inflate = new DeflateStream(input, CompressionMode.Decompress))
            {
                var output = new byte[expected];
                var read = 0;
                while (read < expected)
                {
                    var n = inflate.Read(output, read, expected - read);
                    if (n == 0)
                        throw new FormatException("PNG image data is shorter than the image");
                    read += n;
                }
                return output;
            }
        }

        static void WriteChunk(Stream s, string type, byte[] data)
        {
            var head = new byte[8];
            WriteBigEndian(head, 0, (uint)data.Length);
            Encoding.ASCII.GetBytes(type, 0, 4, head, 4);
            s.Write(head, 0, 8);
            s.Write(data, 0, data.Length);
            var crc = 0xFFFFFFFFu;
            for (var i = 4; i < 8; i++)
                crc = CrcTable[(crc ^ head[i]) & 0xFF] ^ (crc >> 8);
            foreach (var b in data)
                crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            var tail = new byte[4];
            WriteBigEndian(tail, 0, crc ^ 0xFFFFFFFFu);
            s.Write(tail, 0, 4);
        }

        static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                var c = n;
                for (var k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }

        static uint Adler32(byte[] data)
        {
            uint a = 1, b = 0;
            foreach (var d in data)
            {
                a = (a + d) % 65521;
                b = (b + a) % 65521;
            }
            return (b << 16) | a;
        }

        static bool StartsWith(byte[] data, byte[] prefix)
        {
            for (var i = 0; i < prefix.Length; i++)
                if (data[i] != prefix[i])
                    return false;
            return true;
        }

        static uint ReadBigEndian(byte[] b, int i) => (uint)(b[i] << 24 | b[i + 1] << 16 | b[i + 2] << 8 | b[i + 3]);

        static void WriteBigEndian(byte[] b, int i, uint v)
        {
            b[i] = (byte)(v >> 24);
            b[i + 1] = (byte)(v >> 16);
            b[i + 2] = (byte)(v >> 8);
            b[i + 3] = (byte)v;
        }
    }
}
