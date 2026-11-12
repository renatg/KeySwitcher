using System.Media;

namespace KeySwitcher.UI;

/// <summary>A short, quiet "tick" generated in memory and played after an automatic correction.</summary>
internal sealed class ClickSound : IDisposable
{
    private readonly SoundPlayer _player;
    private readonly MemoryStream _stream;

    public ClickSound()
    {
        _stream = new MemoryStream(BuildWav());
        _player = new SoundPlayer(_stream);
        _player.Load();
    }

    public void Play()
    {
        try { _player.Play(); }
        catch (Exception ex) { Log.Error("Sound failed", ex); }
    }

    private static byte[] BuildWav()
    {
        const int sampleRate = 44100;
        const double seconds = 0.035;
        const double frequency = 1400;
        int samples = (int)(sampleRate * seconds);

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8);
        w.Write(36 + samples * 2);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);          // PCM
        w.Write((short)1);          // mono
        w.Write(sampleRate);
        w.Write(sampleRate * 2);    // byte rate
        w.Write((short)2);          // block align
        w.Write((short)16);         // bits per sample
        w.Write("data"u8);
        w.Write(samples * 2);
        for (int i = 0; i < samples; i++)
        {
            double t = (double)i / sampleRate;
            double envelope = Math.Exp(-t * 120);
            double value = Math.Sin(2 * Math.PI * frequency * t) * envelope * 0.18;
            w.Write((short)(value * short.MaxValue));
        }
        w.Flush();
        return ms.ToArray();
    }

    public void Dispose()
    {
        _player.Dispose();
        _stream.Dispose();
    }
}
