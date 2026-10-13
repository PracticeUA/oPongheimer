using Pongheimer.Core.Game;
using Pongheimer.Core.Networking;

namespace Pongheimer.Core.Tests;

public class ProtocolTests
{
    // Позиції байтів у кадрі стану (разом із байтом типу на початку).
    private const int PhaseIndex = 19;
    private const int PauseFlagIndex = 20;

    [Fact]
    public void State_RoundTrips()
    {
        var state = new GameState(123.5, 77.25, 10, 300, 3, 6, GamePhase.Playing);

        var frame = Protocol.Encode(new StateMessage(state));

        Assert.Equal(Protocol.StateType, frame[0]);
        Assert.Equal(Protocol.PayloadSize(Protocol.StateType), frame.Length - 1);
        Assert.True(Protocol.TryDecodeState(frame.AsSpan(1), out var decoded));
        Assert.Equal(state, decoded!.State);
    }

    [Fact]
    public void Input_RoundTrips()
    {
        var frame = Protocol.Encode(new InputMessage(42.5, RematchRequested: true));

        Assert.Equal(Protocol.PayloadSize(Protocol.InputType), frame.Length - 1);
        Assert.True(Protocol.TryDecodeInput(frame.AsSpan(1), out var decoded));
        Assert.Equal(new InputMessage(42.5, true), decoded);
    }

    [Fact]
    public void Pause_RoundTripsInBothDirections()
    {
        var state = Protocol.Encode(new StateMessage(GameState.Initial with { IsPaused = true }));
        var input = Protocol.Encode(new InputMessage(10, false, PauseToggles: 255));

        Assert.True(Protocol.TryDecodeState(state.AsSpan(1), out var decodedState));
        Assert.True(decodedState!.State.IsPaused);
        Assert.True(Protocol.TryDecodeInput(input.AsSpan(1), out var decodedInput));
        Assert.Equal(255, decodedInput!.PauseToggles);
    }

    [Fact]
    public void State_WithGarbagePauseFlag_IsRejected()
    {
        var frame = Protocol.Encode(new StateMessage(GameState.Initial));
        frame[PauseFlagIndex] = 7;

        Assert.False(Protocol.TryDecodeState(frame.AsSpan(1), out _));
    }

    [Fact]
    public void BallVelocity_RoundTrips()
    {
        var frame = Protocol.Encode(new StateMessage(GameState.Initial, BallVelocityX: -412.5, BallVelocityY: 96.25));

        Assert.True(Protocol.TryDecodeState(frame.AsSpan(1), out var decoded));
        Assert.Equal(-412.5, decoded!.BallVelocityX);
        Assert.Equal(96.25, decoded.BallVelocityY);
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.PositiveInfinity)]
    [InlineData(1e6, 0)]
    public void State_WithImpossibleBallVelocity_IsRejected(double velocityX, double velocityY)
    {
        var frame = Protocol.Encode(new StateMessage(GameState.Initial, velocityX, velocityY));

        Assert.False(Protocol.TryDecodeState(frame.AsSpan(1), out _));
    }

    [Fact]
    public void State_WithNonFiniteBall_IsRejected()
    {
        var frame = Protocol.Encode(new StateMessage(GameState.Initial with { BallX = double.NaN }));

        Assert.False(Protocol.TryDecodeState(frame.AsSpan(1), out _));
    }

    [Fact]
    public void State_WithBallFarOutsideField_IsRejected()
    {
        var frame = Protocol.Encode(new StateMessage(GameState.Initial with { BallY = 1e9 }));

        Assert.False(Protocol.TryDecodeState(frame.AsSpan(1), out _));
    }

    [Fact]
    public void State_WithUnknownPhaseOrImpossibleScore_IsRejected()
    {
        var badPhase = Protocol.Encode(new StateMessage(GameState.Initial));
        badPhase[PhaseIndex] = 200;
        var badScore = Protocol.Encode(new StateMessage(GameState.Initial with { ScoreLeft = 99 }));

        Assert.False(Protocol.TryDecodeState(badPhase.AsSpan(1), out _));
        Assert.False(Protocol.TryDecodeState(badScore.AsSpan(1), out _));
    }

    [Fact]
    public void Input_OutOfRangePaddle_IsClamped()
    {
        var frame = Protocol.Encode(new InputMessage(99999, false));

        Assert.True(Protocol.TryDecodeInput(frame.AsSpan(1), out var decoded));
        Assert.Equal(GameRules.FieldHeight, decoded!.PaddleY);
    }

    [Fact]
    public void Input_NonFinitePaddle_IsRejected()
    {
        var frame = Protocol.Encode(new InputMessage(double.NegativeInfinity, false));

        Assert.False(Protocol.TryDecodeInput(frame.AsSpan(1), out _));
    }

    [Fact]
    public void Payload_OfWrongLength_IsRejected()
    {
        Assert.False(Protocol.TryDecodeState(new byte[3], out _));
        Assert.False(Protocol.TryDecodeInput(new byte[30], out _));
    }

    [Fact]
    public void PayloadSize_OfUnknownType_IsNegative()
    {
        Assert.Equal(-1, Protocol.PayloadSize(0));
        Assert.Equal(-1, Protocol.PayloadSize(250));
    }

    [Fact]
    public void Hello_IsValidatedByMagicAndVersion()
    {
        Assert.Equal(HelloResult.Ok, Protocol.ValidateHello(Protocol.EncodeHello()));
        Assert.Equal(
            HelloResult.VersionMismatch,
            Protocol.ValidateHello(Protocol.EncodeHello((byte)(Protocol.Version + 1))));
        Assert.Equal(HelloResult.NotPongheimer, Protocol.ValidateHello("GET / "u8));
        Assert.Equal(HelloResult.NotPongheimer, Protocol.ValidateHello([]));
    }
}
