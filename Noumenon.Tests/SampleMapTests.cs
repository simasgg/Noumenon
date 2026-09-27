using System.Buffers.Binary;
using NAudio.Wave;
using Noumenon.Dsp.Sampler;
using Noumenon.Engine.Samples;

namespace Noumenon.Tests;

public class SampleMapTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "noumenon-tests-" + Guid.NewGuid().ToString("N"));

    public SampleMapTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(dir, true);
        }
        catch (IOException)
        {
        }
    }

    private string TempPath(string name) => Path.Combine(dir, name);

    private static float[] Tone(int frames, float hz, int rate, float amplitude) => Enumerable.Range(0, frames).Select(i => amplitude * MathF.Sin(2f * MathF.PI * hz * i / rate)).ToArray();

    private string WriteWav(string name, WaveFormat format, float[] interleaved)
    {
        var path = TempPath(name);
        using var writer = new WaveFileWriter(path, format);
        writer.WriteSamples(interleaved, 0, interleaved.Length);

        return path;
    }

    /// <summary>A minimal big-endian 16-bit AIFF writer (FORM / COMM with an 80-bit rate / SSND) — NAudio has no AIFF writer.</summary>
    private string WriteAiff(string name, int rate, float[] mono)
    {
        var path = TempPath(name);
        var dataBytes = mono.Length * 2;
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        var b4 = new byte[4];
        var b2 = new byte[2];

        void Be32(int v)
        {
            BinaryPrimitives.WriteInt32BigEndian(b4, v);
            bw.Write(b4);
        }

        void Be16(short v)
        {
            BinaryPrimitives.WriteInt16BigEndian(b2, v);
            bw.Write(b2);
        }

        bw.Write("FORM"u8);
        Be32(4 + 8 + 18 + 8 + 8 + dataBytes);
        bw.Write("AIFF"u8);
        bw.Write("COMM"u8);
        Be32(18);
        Be16(1);
        Be32(mono.Length);
        Be16(16);
        var exponent = (int)Math.Floor(Math.Log2(rate));
        var mantissa = (ulong)rate << (63 - exponent);
        Be16((short)(16383 + exponent));
        var b8 = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(b8, mantissa);
        bw.Write(b8);
        bw.Write("SSND"u8);
        Be32(8 + dataBytes);
        Be32(0);
        Be32(0);
        foreach (var v in mono)
            Be16((short)MathF.Round(v * 32767f));

        return path;
    }

    [Fact]
    public void Set_Get_Adjust_And_Clear_Keys()
    {
        var map = new SampleMap();
        Assert.Equal(0, map.Count);
        Assert.Null(map.Get(5));
        Assert.Null(map.Get(-1));
        Assert.Null(map.Get(128));

        var data = new SampleData("tone", 48000, Tone(100, 440f, 48000, 0.5f));
        var slot = map.Set(5, data, gain: 0.5f, rootNote: 57);
        Assert.Same(slot, map.Get(5));
        Assert.Same(data, slot.Data);
        Assert.Equal(0.5f, slot.Gain);
        Assert.Equal(57, slot.RootNote);
        Assert.Equal(1, map.Count);
        Assert.Single(map.Loaded);

        var adjusted = map.Adjust(5, gain: 2f)!;
        Assert.Equal(2f, adjusted.Gain);
        Assert.Equal(57, adjusted.RootNote);
        Assert.Same(data, adjusted.Data);
        Assert.Null(map.Adjust(6, gain: 1f));

        map.Clear(5);
        Assert.Null(map.Get(5));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.Set(128, data));
    }

    [Fact]
    public void Load_Decodes_16Bit_Stereo_Wav()
    {
        const int rate = 44100;
        var left = Tone(2000, 440f, rate, 0.5f);
        var right = Tone(2000, 660f, rate, 0.25f);
        var interleaved = new float[4000];
        for (var i = 0; i < 2000; i++)
        {
            interleaved[2 * i] = left[i];
            interleaved[2 * i + 1] = right[i];
        }

        var path = WriteWav("stereo16.wav", new WaveFormat(rate, 16, 2), interleaved);
        var slot = new SampleMap().Load(3, path);

        Assert.Equal("stereo16", slot.Name);
        Assert.Equal(path, slot.Path);
        Assert.Equal(rate, slot.Data.SampleRate);
        Assert.Equal(2000, slot.Data.Length);
        Assert.True(slot.Data.IsStereo);
        for (var i = 0; i < 2000; i++)
        {
            Assert.Equal(left[i], slot.Data.Left[i], 0.0001f);
            Assert.Equal(right[i], slot.Data.Right[i], 0.0001f);
        }
    }

    [Fact]
    public void Load_Decodes_24Bit_And_Float_Mono_Wav()
    {
        const int rate = 96000;
        var tone = Tone(1500, 1000f, rate, 0.8f);

        var pcm24 = new SampleMap().Load(0, WriteWav("mono24.wav", new WaveFormat(rate, 24, 1), tone));
        Assert.False(pcm24.Data.IsStereo);
        Assert.Same(pcm24.Data.Left, pcm24.Data.Right);
        Assert.Equal(rate, pcm24.Data.SampleRate);
        for (var i = 0; i < 1500; i++)
            Assert.Equal(tone[i], pcm24.Data.Left[i], 0.000002f);

        var ieee = new SampleMap().Load(0, WriteWav("monofloat.wav", WaveFormat.CreateIeeeFloatWaveFormat(rate, 1), tone));
        Assert.Equal(tone, ieee.Data.Left);
    }

    [Fact]
    public void Load_Decodes_Aiff()
    {
        const int rate = 48000;
        var tone = Tone(1200, 220f, rate, 0.6f);
        var slot = new SampleMap().Load(9, WriteAiff("tone.aif", rate, tone));

        Assert.Equal(rate, slot.Data.SampleRate);
        Assert.Equal(1200, slot.Data.Length);
        for (var i = 0; i < 1200; i++)
            Assert.Equal(tone[i], slot.Data.Left[i], 0.0001f);
    }

    [Fact]
    public void Unknown_Formats_And_Missing_Files_Throw()
    {
        var map = new SampleMap();
        Assert.Throws<FileNotFoundException>(() => map.Load(0, TempPath("missing.wav")));

        var odd = TempPath("noise.xyz");
        File.WriteAllBytes(odd, new byte[64]);
        Assert.Throws<NotSupportedException>(() => map.Load(0, odd));
    }

    [Fact]
    public void A_Registered_Decoder_Handles_Its_Own_Extension()
    {
        var fake = TempPath("generated.fakefmt");
        File.WriteAllText(fake, "not audio");
        SampleDecoder.Register(path => path.EndsWith(".fakefmt", StringComparison.OrdinalIgnoreCase) ? new SampleData("fake", 22050, new float[64]) : null);

        var slot = new SampleMap().Load(1, fake);
        Assert.Equal("fake", slot.Name);
        Assert.Equal(22050, slot.Data.SampleRate);
    }
}
