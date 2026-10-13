namespace Pongheimer.Infrastructure;

/// <summary>
/// Лічить плавність гри за щосекундними відрізками. Запізнілі кадри рахуються лише тоді, коли
/// м'яч у русі: поки сцена нерухома (подача, пауза), WPF і сам видає кадри рідше, і оку це не видно.
/// </summary>
public sealed class FrameStats
{
    // Кадр, довший за півтора нормальних для 60 Гц, око вже помічає як ривок.
    private const double LateFrameMilliseconds = 25;

    private double _elapsed;
    private int _frames;
    private int _motionFrames;
    private int _lateFrames;
    private double _worstFrame;
    private double _worstUpdate;
    private int _stalledFrames;

    /// <summary>Враховує кадр; раз на секунду повертає готовий звіт, інакше null.</summary>
    /// <param name="frameSeconds">Час від попереднього кадру.</param>
    /// <param name="updateMilliseconds">Скільки зайняло оновлення логіки цього кадру.</param>
    /// <param name="ballInMotion">М'яч у грі й не на паузі.</param>
    /// <param name="ballStalled">М'яч мав рухатись, але лишився на місці.</param>
    public string? Record(double frameSeconds, double updateMilliseconds, bool ballInMotion, bool ballStalled)
    {
        var frameMilliseconds = frameSeconds * 1000;
        _elapsed += frameSeconds;
        _frames++;
        _worstUpdate = Math.Max(_worstUpdate, updateMilliseconds);
        if (ballInMotion)
        {
            _motionFrames++;
            _lateFrames += frameMilliseconds > LateFrameMilliseconds ? 1 : 0;
            _worstFrame = Math.Max(_worstFrame, frameMilliseconds);
            _stalledFrames += ballStalled ? 1 : 0;
        }

        if (_elapsed < 1)
        {
            return null;
        }

        var report = FormattableString.Invariant(
            $"FPS {_frames / _elapsed:0} | late {_lateFrames}/{_motionFrames} | frame max {_worstFrame:0.0} ms | update max {_worstUpdate:0.00} ms | stalled {_stalledFrames}");
        _elapsed = 0;
        _frames = 0;
        _motionFrames = 0;
        _lateFrames = 0;
        _worstFrame = 0;
        _worstUpdate = 0;
        _stalledFrames = 0;
        return report;
    }
}
