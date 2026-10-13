using Microsoft.Xna.Framework.Audio;
using Pongheimer.Core.Diagnostics;

namespace Pongheimer.Audio;

public enum GameSound
{
    PaddleHit,
    WallBounce,
    PointWon,
    PointLost,
    Victory,
    Defeat,
}

public interface ISoundPlayer
{
    bool IsEnabled { get; set; }

    /// <summary>Програє звук, не блокуючи виклик. Ніколи не кидає винятків.</summary>
    void Play(GameSound sound);
}

/// <summary>
/// Програє синтезовані звуки через аудіорушій MonoGame: виклик повертається миттєво,
/// а звуки змішуються самі. Звук — прикраса, а не функція: якщо аудіопристрою немає
/// чи він збоїть, гра мовчки триває без звуку.
/// </summary>
public sealed class SynthSoundPlayer : ISoundPlayer, IDisposable
{
    private readonly Dictionary<GameSound, SoundEffect> _effects = [];
    private readonly ILog _log;
    private bool _broken;

    public SynthSoundPlayer(ILog log)
    {
        _log = log;
        try
        {
            Add(GameSound.PaddleHit, new Note(480, 45));
            Add(GameSound.WallBounce, new Note(240, 35));
            Add(GameSound.PointWon, new Note(660, 80), new Note(880, 120));
            Add(GameSound.PointLost, new Note(220, 100), new Note(165, 160));

            // Фінал гри — ядерний вибух, а вже після гуркоту мелодія перемоги чи поразки.
            var boom = Note.Boom(1100);
            Add(GameSound.Victory, boom, new Note(523, 110), new Note(659, 110), new Note(784, 110), new Note(1047, 260));
            Add(GameSound.Defeat, boom, new Note(392, 140), new Note(330, 140), new Note(262, 140), new Note(196, 320));
        }
        catch (Exception ex)
        {
            Disable("Cannot prepare sounds", ex);
        }
    }

    public bool IsEnabled { get; set; } = true;

    public void Play(GameSound sound)
    {
        if (!IsEnabled || _broken || !_effects.TryGetValue(sound, out var effect))
        {
            return;
        }

        try
        {
            effect.Play();
        }
        catch (Exception ex)
        {
            Disable("Cannot play sound", ex);
        }
    }

    public void Dispose()
    {
        foreach (var effect in _effects.Values)
        {
            effect.Dispose();
        }

        _effects.Clear();
    }

    private void Add(GameSound sound, params Note[] notes) =>
        _effects[sound] = new SoundEffect(ToneSynth.RenderPcm(notes), ToneSynth.SampleRate, AudioChannels.Mono);

    private void Disable(string message, Exception exception)
    {
        _broken = true;
        _log.Error(message + "; sound is off for this run", exception);
    }
}
