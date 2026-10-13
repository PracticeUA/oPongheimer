namespace Pongheimer.Audio;

/// <summary>Відрізок звуку: нота (частота в герцах) або шум вибуху, тривалістю в мілісекундах.</summary>
public readonly record struct Note(double Frequency, int Milliseconds)
{
    private const double NoiseMarker = -1;

    public bool IsNoise => Frequency == NoiseMarker;

    /// <summary>Гуркіт, що затихає й «осідає» вниз, — вибух.</summary>
    public static Note Boom(int milliseconds) => new(NoiseMarker, milliseconds);
}

/// <summary>
/// Синтезує ретро-звуки просто в пам'яті, тож гра не потребує жодних звукових файлів
/// і лишається одним exe.
/// </summary>
public static class ToneSynth
{
    public const int SampleRate = 22050;

    private const double ToneVolume = 0.18;
    private const double NoiseVolume = 0.45;

    // Плавний початок і кінець ноти, щоб не було клацання.
    private const int FadeSamples = SampleRate * 4 / 1000;

    /// <summary>Повертає звук як моно PCM, 16 біт, <see cref="SampleRate"/> Гц.</summary>
    public static byte[] RenderPcm(params Note[] notes)
    {
        var samples = new List<short>();
        foreach (var note in notes)
        {
            var count = SampleRate * note.Milliseconds / 1000;
            if (note.IsNoise)
            {
                AddNoise(samples, count);
            }
            else
            {
                AddTone(samples, note.Frequency, count);
            }
        }

        var bytes = new byte[samples.Count * sizeof(short)];
        Buffer.BlockCopy(samples.ToArray(), 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static void AddTone(List<short> samples, double frequency, int count)
    {
        for (var i = 0; i < count; i++)
        {
            // Меандр — характерний «піксельний» тембр старих ігор.
            var phase = i * frequency / SampleRate % 1.0;
            var wave = phase < 0.5 ? 1.0 : -1.0;
            var fade = Math.Min(1.0, Math.Min(i, count - 1 - i) / (double)FadeSamples);
            samples.Add((short)(wave * fade * ToneVolume * short.MaxValue));
        }
    }

    private static void AddNoise(List<short> samples, int count)
    {
        // Шум «зі сходинками»: кожне випадкове значення тримається дедалі довше,
        // тому гуркіт стає нижчим, поки затихає. Фіксоване зерно — однаковий звук щоразу.
        var random = new Random(1945);
        var value = 0.0;
        var hold = 0;
        for (var i = 0; i < count; i++)
        {
            var progress = i / (double)count;
            if (hold-- <= 0)
            {
                value = random.NextDouble() * 2 - 1;
                hold = (int)(3 + progress * 40);
            }

            var envelope = Math.Pow(1 - progress, 2) * Math.Min(1.0, i / (double)FadeSamples);
            samples.Add((short)(value * envelope * NoiseVolume * short.MaxValue));
        }
    }
}
