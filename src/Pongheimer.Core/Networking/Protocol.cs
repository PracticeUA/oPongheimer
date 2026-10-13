using System.Buffers.Binary;
using Pongheimer.Core.Game;

namespace Pongheimer.Core.Networking;

/// <summary>Стан гри від хоста до клієнта.</summary>
/// <param name="State">Знімок гри на момент надсилання.</param>
/// <param name="BallVelocityX">Швидкість м'яча, пікселів на секунду: з нею клієнт рухає м'яч між пакетами.</param>
/// <param name="BallVelocityY">Те саме по вертикалі.</param>
public sealed record StateMessage(GameState State, double BallVelocityX = 0, double BallVelocityY = 0);

/// <summary>Ввід клієнта до хоста.</summary>
/// <param name="PaddleY">Позиція ракетки клієнта.</param>
/// <param name="RematchRequested">Клієнт просить реванш.</param>
/// <param name="PauseToggles">Скільки разів клієнт натиснув паузу від початку партії (за модулем 256).</param>
public sealed record InputMessage(double PaddleY, bool RematchRequested, byte PauseToggles = 0);

public enum HelloResult
{
    Ok,
    NotPongheimer,
    VersionMismatch,
}

/// <summary>
/// Бінарний протокол: кожен кадр — байт типу та тіло фіксованого для цього типу розміру.
/// Усе, що приходить із мережі, перевіряється: співрозмовнику не довіряємо.
/// </summary>
public static class Protocol
{
    public const int DefaultPort = 7777;
    public const byte Version = 3;

    public const byte HelloType = 1;
    public const byte StateType = 2;
    public const byte InputType = 3;
    public const byte PingType = 4;

    public const int MaxPayloadSize = StatePayloadSize;

    private const uint Magic = 0x474E4F50; // "PONG"
    private const int HelloPayloadSize = 5;
    private const int StatePayloadSize = 28;
    private const int InputPayloadSize = 6;

    // Запас на те, що м'яч на мить вилітає за межі поля перед зарахуванням гола.
    private const double BallBoundsSlack = 100;
    private const double MaxBallVelocity = GameRules.MaxBallSpeed * 1.5;

    public static byte[] PingFrame { get; } = [PingType];

    /// <summary>Розмір тіла кадру або -1 для невідомого типу.</summary>
    public static int PayloadSize(byte type) => type switch
    {
        HelloType => HelloPayloadSize,
        StateType => StatePayloadSize,
        InputType => InputPayloadSize,
        PingType => 0,
        _ => -1,
    };

    public static byte[] EncodeHello(byte version = Version)
    {
        var frame = new byte[1 + HelloPayloadSize];
        frame[0] = HelloType;
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(1), Magic);
        frame[5] = version;
        return frame;
    }

    public static HelloResult ValidateHello(ReadOnlySpan<byte> frame)
    {
        if (frame.Length != 1 + HelloPayloadSize
            || frame[0] != HelloType
            || BinaryPrimitives.ReadUInt32LittleEndian(frame[1..]) != Magic)
        {
            return HelloResult.NotPongheimer;
        }

        return frame[5] == Version ? HelloResult.Ok : HelloResult.VersionMismatch;
    }

    public static byte[] Encode(StateMessage message)
    {
        var state = message.State;
        var frame = new byte[1 + StatePayloadSize];
        frame[0] = StateType;
        var payload = frame.AsSpan(1);
        BinaryPrimitives.WriteSingleLittleEndian(payload, (float)state.BallX);
        BinaryPrimitives.WriteSingleLittleEndian(payload[4..], (float)state.BallY);
        BinaryPrimitives.WriteSingleLittleEndian(payload[8..], (float)state.LeftPaddleY);
        BinaryPrimitives.WriteSingleLittleEndian(payload[12..], (float)state.RightPaddleY);
        payload[16] = (byte)state.ScoreLeft;
        payload[17] = (byte)state.ScoreRight;
        payload[18] = (byte)state.Phase;
        payload[19] = (byte)(state.IsPaused ? 1 : 0);
        BinaryPrimitives.WriteSingleLittleEndian(payload[20..], (float)message.BallVelocityX);
        BinaryPrimitives.WriteSingleLittleEndian(payload[24..], (float)message.BallVelocityY);
        return frame;
    }

    public static bool TryDecodeState(ReadOnlySpan<byte> payload, out StateMessage? message)
    {
        message = null;
        if (payload.Length != StatePayloadSize)
        {
            return false;
        }

        double ballX = BinaryPrimitives.ReadSingleLittleEndian(payload);
        double ballY = BinaryPrimitives.ReadSingleLittleEndian(payload[4..]);
        double leftY = BinaryPrimitives.ReadSingleLittleEndian(payload[8..]);
        double rightY = BinaryPrimitives.ReadSingleLittleEndian(payload[12..]);
        int scoreLeft = payload[16];
        int scoreRight = payload[17];
        var phase = payload[18];
        var paused = payload[19];
        double velocityX = BinaryPrimitives.ReadSingleLittleEndian(payload[20..]);
        double velocityY = BinaryPrimitives.ReadSingleLittleEndian(payload[24..]);

        var valid = IsWithin(velocityX, -MaxBallVelocity, MaxBallVelocity)
            && IsWithin(velocityY, -MaxBallVelocity, MaxBallVelocity)
            && IsWithin(ballX, -BallBoundsSlack, GameRules.FieldWidth + BallBoundsSlack)
            && IsWithin(ballY, -BallBoundsSlack, GameRules.FieldHeight + BallBoundsSlack)
            && double.IsFinite(leftY)
            && double.IsFinite(rightY)
            && scoreLeft <= GameRules.WinScore
            && scoreRight <= GameRules.WinScore
            && phase <= (byte)GamePhase.GameOver
            && paused <= 1;
        if (!valid)
        {
            return false;
        }

        message = new StateMessage(
            new GameState(
                ballX,
                ballY,
                GameRules.ClampPaddle(leftY),
                GameRules.ClampPaddle(rightY),
                scoreLeft,
                scoreRight,
                (GamePhase)phase,
                paused == 1),
            velocityX,
            velocityY);
        return true;
    }

    public static byte[] Encode(InputMessage message)
    {
        var frame = new byte[1 + InputPayloadSize];
        frame[0] = InputType;
        BinaryPrimitives.WriteSingleLittleEndian(frame.AsSpan(1), (float)message.PaddleY);
        frame[5] = (byte)(message.RematchRequested ? 1 : 0);
        frame[6] = message.PauseToggles;
        return frame;
    }

    public static bool TryDecodeInput(ReadOnlySpan<byte> payload, out InputMessage? message)
    {
        message = null;
        if (payload.Length != InputPayloadSize)
        {
            return false;
        }

        double paddleY = BinaryPrimitives.ReadSingleLittleEndian(payload);
        if (!double.IsFinite(paddleY) || payload[4] > 1)
        {
            return false;
        }

        message = new InputMessage(GameRules.ClampPaddle(paddleY), payload[4] == 1, payload[5]);
        return true;
    }

    private static bool IsWithin(double value, double min, double max) =>
        double.IsFinite(value) && value >= min && value <= max;
}
