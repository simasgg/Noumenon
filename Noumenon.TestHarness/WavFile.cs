using System.Text;

namespace Noumenon.TestHarness;

/// <summary>
/// Minimal RIFF/WAVE reader-writer for the offline harness. Reads 8/16/24/32-bit PCM and 32-bit
/// IEEE float (mono or stereo); writes 24-bit PCM stereo. Not a general-purpose WAV library — the
/// standalone and the sampler use NAudio.
/// </summary>
internal sealed class WavFile
{
    public int SampleRate { get; set; } = 48000;
    public float[] Left { get; set; } = [];
    public float[] Right { get; set; } = [];
    public int Length => Left.Length;

    public static WavFile Read(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);

        if (ReadId(br) != "RIFF")
            throw new InvalidDataException("Not a RIFF file.");

        br.ReadInt32();
        if (ReadId(br) != "WAVE")
            throw new InvalidDataException("Not a WAVE file.");

        int format = 1, channels = 2, sampleRate = 48000, bits = 16;
        byte[]? data = null;

        while (fs.Position + 8 <= fs.Length)
        {
            var id = ReadId(br);
            var size = br.ReadInt32();
            var next = fs.Position + size + (size & 1);
            if (id == "fmt ")
            {
                format = br.ReadInt16();
                channels = br.ReadInt16();
                sampleRate = br.ReadInt32();
                br.ReadInt32();
                br.ReadInt16();
                bits = br.ReadInt16();
            }
            else if (id == "data")
            {
                data = br.ReadBytes(size);
            }

            fs.Position = next;
        }

        if (data == null)
            throw new InvalidDataException("No data chunk found.");

        var wav = new WavFile { SampleRate = sampleRate };
        Decode(data, format, channels, bits, wav);

        return wav;
    }

    private static void Decode(byte[] data, int format, int channels, int bits, WavFile wav)
    {
        var bytesPerSample = bits / 8;
        var frameSize = bytesPerSample * Math.Max(channels, 1);
        var frames = frameSize > 0 ? data.Length / frameSize : 0;
        var l = new float[frames];
        var r = new float[frames];

        var pos = 0;
        for (var f = 0; f < frames; f++)
        {
            for (var ch = 0; ch < channels; ch++)
            {
                var s = ReadSample(data, pos, format, bits);
                pos += bytesPerSample;
                switch (ch)
                {
                    case 0:
                        l[f] = s;
                        break;
                    case 1:
                        r[f] = s;
                        break;
                }
            }

            if (channels == 1)
                r[f] = l[f];
        }

        wav.Left = l;
        wav.Right = r;
    }

    private static float ReadSample(byte[] d, int pos, int format, int bits)
    {
        if (format == 3 && bits == 32)
            return BitConverter.ToSingle(d, pos);

        return bits switch
        {
            8 => (d[pos] - 128) / 128f,
            16 => BitConverter.ToInt16(d, pos) / 32768f,
            24 => (d[pos] | (d[pos + 1] << 8) | ((sbyte)d[pos + 2] << 16)) / 8388608f,
            32 => BitConverter.ToInt32(d, pos) / 2147483648f,
            _ => throw new NotSupportedException($"Unsupported bit depth: {bits}"),
        };
    }

    public void Write(string path)
    {
        const int channels = 2;
        const int bits = 24;
        const int bytesPerSample = bits / 8;
        var dataSize = Length * channels * bytesPerSample;

        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);

        WriteId(bw, "RIFF");
        bw.Write(36 + dataSize);
        WriteId(bw, "WAVE");
        WriteId(bw, "fmt ");
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)channels);
        bw.Write(SampleRate);
        bw.Write(SampleRate * channels * bytesPerSample);
        bw.Write((short)(channels * bytesPerSample));
        bw.Write((short)bits);
        WriteId(bw, "data");
        bw.Write(dataSize);

        for (var i = 0; i < Length; i++)
        {
            WritePcm24(bw, Left[i]);
            WritePcm24(bw, Right[i]);
        }
    }

    private static void WritePcm24(BinaryWriter bw, float v)
    {
        v = v switch
        {
            > 1f => 1f,
            < -1f => -1f,
            _ => v,
        };

        var s = (int)MathF.Round(v * 8388607f);
        bw.Write((byte)s);
        bw.Write((byte)(s >> 8));
        bw.Write((byte)(s >> 16));
    }

    private static string ReadId(BinaryReader br) => Encoding.ASCII.GetString(br.ReadBytes(4));

    private static void WriteId(BinaryWriter bw, string id) => bw.Write(Encoding.ASCII.GetBytes(id));
}
