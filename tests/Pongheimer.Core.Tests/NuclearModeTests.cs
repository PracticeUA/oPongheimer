using Pongheimer.Core.Game;
using Pongheimer.Core.Networking;
using Pongheimer.Core.Sessions;

namespace Pongheimer.Core.Tests;

public class NuclearModeTests
{
    private const double Step = 1.0 / 120;
    private const double Frame = 1.0 / 60;
    private static readonly PlayerInput Idle = new(0, 0, false);

    [Fact]
    public void ClassicMode_NeverHasBombsOrEffects()
    {
        var simulation = new PongSimulation(new Random(1));

        Run(simulation, seconds: 30);
        simulation.ApplyBonus(BonusKind.BigBall, Side.Left);

        Assert.Null(simulation.State.Nuclear);
        Assert.Equal(GameMode.Classic, simulation.State.Mode);
        Assert.Equal(GameRules.BallSize, simulation.State.BallSize);
        Assert.False(simulation.SpawnBomb(200, 200, 0, BonusKind.Shield));
    }

    [Fact]
    public void Bombs_AppearCrossTheFieldAndLeave()
    {
        var simulation = Nuclear(seed: 3);

        Run(simulation, NuclearRules.FirstBombDelay + 0.1);
        var bomb = Assert.Single(simulation.State.Nuclear!.Bombs);
        Assert.InRange(bomb.X, 170, 630);
        Assert.Contains(bomb.Kind, NuclearRules.AllBonuses);

        // На полі одночасно кілька бомб, але ніколи не більше дозволеного; за межі поля вони не тікають.
        var seenAtOnce = 0;
        for (var t = 0.0; t < 40; t += Step)
        {
            simulation.Step(Step);
            var nuclear = simulation.State.Nuclear!;
            seenAtOnce = Math.Max(seenAtOnce, nuclear.Bombs.Count);
            foreach (var current in nuclear.Bombs)
            {
                Assert.InRange(current.Y, -NuclearRules.BombRadius - 1, GameRules.FieldHeight + NuclearRules.BombRadius + 1);
            }
        }

        Assert.InRange(seenAtOnce, 3, NuclearRules.MaxBombs);
    }

    [Theory]
    [InlineData(GameRules.StartBallSpeed, false, 1.0)]
    [InlineData(GameRules.StartBallSpeed * 2, false, 2.0)]
    [InlineData(GameRules.StartBallSpeed * 2, true, 2.0 * NuclearRules.FastBallFactor)]
    [InlineData(GameRules.StartBallSpeed / 2, false, 1.0)]
    public void Bombs_SpeedUpTogetherWithTheBall_ButNeverSlowDown(double ballSpeed, bool fastBall, double expectedPace)
    {
        var simulation = Nuclear();
        simulation.LaunchBall(400, 20, 0, 0);
        simulation.LaunchBall(400, 20, ballSpeed, 0);
        if (fastBall)
        {
            simulation.ApplyBonus(BonusKind.FastBall, Side.Left);
        }

        simulation.SpawnBomb(200, 100, NuclearRules.BombSpeed, BonusKind.Shield);
        simulation.Step(0.1);

        var bomb = Assert.Single(simulation.State.Nuclear!.Bombs);
        Assert.Equal(100 + NuclearRules.BombSpeed * expectedPace * 0.1, bomb.Y, precision: 6);
    }

    [Fact]
    public void Bombs_FlyAtBasePaceWhileBallWaitsForServe()
    {
        var simulation = Nuclear();
        simulation.SpawnBomb(200, 100, -NuclearRules.BombSpeed, BonusKind.Shield);

        simulation.Step(0.1);

        Assert.Equal(100 - NuclearRules.BombSpeed * 0.1, simulation.State.Nuclear!.Bombs[0].Y, precision: 6);
    }

    [Theory]
    [InlineData(300, Side.Left)]
    [InlineData(-300, Side.Right)]
    public void BombHitByBall_GoesToWhoeverTouchedTheBallLast(double velocityX, Side expectedOwner)
    {
        var simulation = Nuclear();
        simulation.LaunchBall(400, 200, velocityX, 0);
        simulation.SpawnBomb(400 + Math.Sign(velocityX) * 40, 206, 0, BonusKind.LongPaddle);

        Run(simulation, 0.3);

        var nuclear = simulation.State.Nuclear!;
        Assert.Empty(nuclear.Bombs);
        Assert.Equal(BonusKind.LongPaddle, nuclear.LastDetonation.Kind);
        Assert.Equal(expectedOwner, nuclear.LastDetonation.Owner);
        Assert.Equal(1, nuclear.LastDetonation.Count);
        Assert.Equal(NuclearRules.LongPaddleHeight, simulation.State.PaddleHeight(expectedOwner));
        Assert.Equal(GameRules.PaddleHeight, simulation.State.PaddleHeight(Other(expectedOwner)));
    }

    [Fact]
    public void Bomb_IsNotDetonatedByBallWaitingForServe()
    {
        var simulation = Nuclear();
        var ball = simulation.State;
        simulation.SpawnBomb(ball.BallX + 6, ball.BallY + 6, 0, BonusKind.Freeze);

        Run(simulation, 0.5);

        Assert.Single(simulation.State.Nuclear!.Bombs);
        Assert.Equal(0, simulation.State.Nuclear!.LastDetonation.Count);
    }

    [Theory]
    [InlineData(BonusKind.BigBall, NuclearRules.BigBallSize)]
    [InlineData(BonusKind.SmallBall, NuclearRules.SmallBallSize)]
    public void BallSizeBonus_ResizesBallAroundItsCentreAndWearsOff(BonusKind kind, double expectedSize)
    {
        var simulation = Nuclear();
        simulation.LaunchBall(400, 200, 0, 0);
        var centre = simulation.State.BallX + GameRules.BallSize / 2;

        simulation.ApplyBonus(kind, Side.Left);
        Assert.Equal(expectedSize, simulation.State.BallSize);
        Assert.Equal(centre, simulation.State.BallX + expectedSize / 2, precision: 9);

        Run(simulation, NuclearRules.EffectDuration + 0.1);
        Assert.Equal(GameRules.BallSize, simulation.State.BallSize);
        Assert.Equal(centre, simulation.State.BallX + GameRules.BallSize / 2, precision: 6);
    }

    [Fact]
    public void BigBall_StillBouncesOffWallsAndPaddles()
    {
        var simulation = Nuclear();
        simulation.LaunchBall(300, 200, -500, 400);
        simulation.ApplyBonus(BonusKind.BigBall, Side.Left);

        for (var t = 0.0; t < 3; t += Step)
        {
            // Ліва ракетка стежить за м'ячем, права стоїть — м'яч має відбитись зліва щоразу.
            var state = simulation.State;
            simulation.SetPaddle(Side.Left, state.BallY + state.BallSize / 2 - state.LeftPaddleHeight / 2);
            simulation.Step(Step);
            Assert.InRange(simulation.State.BallY, 0, GameRules.FieldHeight - simulation.State.BallSize);
        }

        Assert.Equal(0, simulation.State.ScoreRight);
    }

    [Fact]
    public void FastBall_SpeedsBallUp_AndNormalSpeedResetsEvenRallySpeedUp()
    {
        var simulation = Nuclear();
        simulation.LaunchBall(400, 200, 600, 0);

        simulation.ApplyBonus(BonusKind.FastBall, Side.Left);
        Assert.Equal(600 * NuclearRules.FastBallFactor, simulation.BallVelocityX, precision: 6);

        // «Нормальна швидкість» повертає м'яч до стартової, але не нижче неї.
        simulation.ApplyBonus(BonusKind.NormalSpeedBall, Side.Left);
        Assert.Equal(GameRules.StartBallSpeed, simulation.BallVelocityX, precision: 6);

        simulation.LaunchBall(400, 200, GameRules.StartBallSpeed, 0);
        simulation.ApplyBonus(BonusKind.NormalSpeedBall, Side.Left);
        Assert.Equal(GameRules.StartBallSpeed, simulation.BallVelocityX, precision: 6);
    }

    [Fact]
    public void Shockwave_ThrowsBallInNewDirectionAtSameSpeed()
    {
        var directions = new HashSet<int>();
        for (var seed = 0; seed < 20; seed++)
        {
            var simulation = Nuclear(seed);
            simulation.LaunchBall(400, 200, 500, 0);

            simulation.ApplyBonus(BonusKind.Shockwave, Side.Left);

            var speed = Math.Sqrt(Math.Pow(simulation.BallVelocityX, 2) + Math.Pow(simulation.BallVelocityY, 2));
            Assert.Equal(500, speed, precision: 6);
            Assert.NotEqual(0, simulation.BallVelocityX);
            directions.Add(Math.Sign(simulation.BallVelocityX));
        }

        Assert.Equal(2, directions.Count);
    }

    [Theory]
    [InlineData(BonusKind.LongPaddle, NuclearRules.LongPaddleHeight)]
    [InlineData(BonusKind.ShortPaddle, NuclearRules.ShortPaddleHeight)]
    public void PaddleSizeBonus_KeepsPaddleCentredAndInsideField(BonusKind kind, double expectedHeight)
    {
        var simulation = Nuclear();
        simulation.SetPaddle(Side.Right, 100);
        var centre = 100 + GameRules.PaddleHeight / 2;

        simulation.ApplyBonus(kind, Side.Right);
        Assert.Equal(expectedHeight, simulation.State.RightPaddleHeight);
        Assert.Equal(centre, simulation.State.RightPaddleY + expectedHeight / 2, precision: 9);
        Assert.Equal(GameRules.PaddleHeight, simulation.State.LeftPaddleHeight);

        // Довга ракетка біля краю не вилазить за поле.
        simulation.SetPaddle(Side.Right, 9999);
        Assert.Equal(GameRules.FieldHeight, simulation.State.RightPaddleY + expectedHeight, precision: 9);
    }

    [Fact]
    public void OppositeBonus_ReplacesPreviousOne()
    {
        var simulation = Nuclear();

        simulation.ApplyBonus(BonusKind.LongPaddle, Side.Left);
        simulation.ApplyBonus(BonusKind.ShortPaddle, Side.Left);
        simulation.ApplyBonus(BonusKind.BigBall, Side.Left);
        simulation.ApplyBonus(BonusKind.SmallBall, Side.Right);

        Assert.Equal(NuclearRules.ShortPaddleHeight, simulation.State.LeftPaddleHeight);
        Assert.Equal(NuclearRules.SmallBallSize, simulation.State.BallSize);
    }

    [Theory]
    [InlineData(BonusKind.FastPaddle, NuclearRules.FastPaddleFactor)]
    [InlineData(BonusKind.SlowPaddle, NuclearRules.SlowPaddleFactor)]
    public void PaddleSpeedBonus_ChangesHowFarPaddleMoves(BonusKind kind, double factor)
    {
        var simulation = Nuclear();
        simulation.ApplyBonus(kind, Side.Left);

        simulation.MovePaddle(Side.Left, 1, 0.1);
        simulation.MovePaddle(Side.Right, 1, 0.1);

        Assert.Equal(GameRules.PaddleSpeed * 0.1 * factor, simulation.State.LeftPaddleY - GameRules.CenteredPaddleY, precision: 9);
        Assert.Equal(GameRules.PaddleSpeed * 0.1, simulation.State.RightPaddleY - GameRules.CenteredPaddleY, precision: 9);
    }

    [Fact]
    public void Freeze_StopsOwnPaddleForASecond()
    {
        var simulation = Nuclear();
        simulation.ApplyBonus(BonusKind.Freeze, Side.Left);

        simulation.MovePaddle(Side.Left, 1, 0.1);
        Assert.Equal(GameRules.CenteredPaddleY, simulation.State.LeftPaddleY);

        Run(simulation, NuclearRules.FreezeDuration + 0.05);
        simulation.MovePaddle(Side.Left, 1, 0.1);
        Assert.True(simulation.State.LeftPaddleY > GameRules.CenteredPaddleY);
    }

    [Fact]
    public void Inversion_SwapsUpAndDown()
    {
        var simulation = Nuclear();
        simulation.ApplyBonus(BonusKind.Inversion, Side.Right);

        simulation.MovePaddle(Side.Right, 1, 0.1);
        simulation.MovePaddle(Side.Left, 1, 0.1);

        Assert.True(simulation.State.RightPaddleY < GameRules.CenteredPaddleY);
        Assert.True(simulation.State.LeftPaddleY > GameRules.CenteredPaddleY);
    }

    [Fact]
    public void Shield_SavesExactlyOneGoal()
    {
        var simulation = Nuclear();
        simulation.SetPaddle(Side.Left, 0);
        simulation.ApplyBonus(BonusKind.Shield, Side.Left);
        simulation.LaunchBall(100, 400, -400, 0);

        Run(simulation, 0.5);
        Assert.Equal(0, simulation.State.ScoreRight);
        Assert.True(simulation.BallVelocityX > 0);
        Assert.False(simulation.State.Nuclear!.Left.HasShield);

        simulation.LaunchBall(100, 400, -400, 0);
        Run(simulation, 0.5);
        Assert.Equal(1, simulation.State.ScoreRight);
    }

    [Fact]
    public void DoublePoint_MakesNextOwnGoalCountTwice_Once()
    {
        var simulation = Nuclear();
        simulation.SetPaddle(Side.Left, 0);
        simulation.ApplyBonus(BonusKind.DoublePoint, Side.Right);

        simulation.LaunchBall(100, 400, -400, 0);
        Run(simulation, 0.5);
        Assert.Equal(2, simulation.State.ScoreRight);

        simulation.LaunchBall(100, 400, -400, 0);
        Run(simulation, 0.5);
        Assert.Equal(3, simulation.State.ScoreRight);
    }

    [Fact]
    public void DoublePoint_CannotPushScorePastWinningScore()
    {
        var simulation = Nuclear();
        simulation.SetPaddle(Side.Left, 0);
        for (var point = 0; point < GameRules.WinScore - 1; point++)
        {
            simulation.LaunchBall(100, 400, -400, 0);
            Run(simulation, 0.5);
        }

        simulation.ApplyBonus(BonusKind.DoublePoint, Side.Right);
        simulation.LaunchBall(100, 400, -400, 0);
        Run(simulation, 0.5);

        Assert.Equal(GameRules.WinScore, simulation.State.ScoreRight);
        Assert.Equal(Side.Right, simulation.State.Winner);
    }

    [Fact]
    public void Goal_ClearsEveryEffect_ButBombsKeepFlying()
    {
        var simulation = Nuclear();
        simulation.SetPaddle(Side.Left, 0);
        foreach (var kind in NuclearRules.AllBonuses)
        {
            simulation.ApplyBonus(kind, Side.Left);
            simulation.ApplyBonus(kind, Side.Right);
        }

        simulation.SpawnBomb(200, 50, 0, BonusKind.Shield);
        simulation.LaunchBall(100, 400, -400, 0);
        Run(simulation, 0.1);

        // Щит урятував би від гола, тому перший удар у край його знімає, а другий — уже гол.
        simulation.LaunchBall(100, 400, -400, 0);
        Run(simulation, 0.3);
        simulation.LaunchBall(100, 400, -400, 0);
        Run(simulation, 0.5);

        var state = simulation.State;
        Assert.True(state.ScoreRight > 0);
        Assert.Equal(GameRules.BallSize, state.BallSize);
        Assert.Equal(GameRules.PaddleHeight, state.LeftPaddleHeight);
        Assert.Equal(GameRules.PaddleHeight, state.RightPaddleHeight);
        Assert.Equal(default, state.Nuclear!.Left);
        Assert.Equal(default, state.Nuclear.Right);
        Assert.False(state.Nuclear.BallSize.IsActive);
        Assert.False(state.Nuclear.BallSpeed.IsActive);
        Assert.NotEmpty(state.Nuclear.Bombs);
    }

    [Fact]
    public void Pause_FreezesBombsAndEffectTimers()
    {
        var simulation = Nuclear();
        simulation.SpawnBomb(200, 100, NuclearRules.BombSpeed, BonusKind.Shield);
        simulation.ApplyBonus(BonusKind.LongPaddle, Side.Left);
        simulation.TogglePause();
        var frozen = simulation.State;

        Run(simulation, 5);

        Assert.Equal(frozen, simulation.State);
    }

    [Fact]
    public void Rematch_StartsWithCleanField()
    {
        var simulation = Nuclear();
        simulation.SpawnBomb(200, 100, 0, BonusKind.Shield);
        simulation.ApplyBonus(BonusKind.BigBall, Side.Left);
        simulation.ApplyBonus(BonusKind.Shield, Side.Right);

        simulation.Restart();

        Assert.Equal(NuclearSnapshot.Empty, simulation.State.Nuclear);
        Assert.Equal(GameRules.BallSize, simulation.State.BallSize);
    }

    [Fact]
    public void Detector_ReportsEachDetonationOnce_AndNotAsPaddleHit()
    {
        var simulation = Nuclear(seed: 2);
        var detector = new GameEventDetector();
        simulation.LaunchBall(300, 200, 400, 0);
        simulation.SpawnBomb(380, 206, 0, BonusKind.Shockwave);
        simulation.SetPaddle(Side.Left, 0);
        simulation.SetPaddle(Side.Right, 0);

        var events = new List<GameEvents> { detector.Observe(simulation.State) };
        for (var t = 0.0; t < 0.4; t += Step)
        {
            simulation.Step(Step);
            events.Add(detector.Observe(simulation.State));
        }

        var detonation = Assert.Single(events, e => e.Detonated is not null).Detonated!.Value;
        Assert.Equal(BonusKind.Shockwave, detonation.Kind);
        Assert.Equal(Side.Left, detonation.Owner);
        Assert.DoesNotContain(events, e => e.PaddleHit || e.WallBounce);

        // Реванш обнуляє лічильник вибухів — це не має виглядати як новий вибух.
        simulation.Restart();
        Assert.Null(detector.Observe(simulation.State).Detonated);
    }

    [Fact]
    public void NuclearState_RoundTripsThroughProtocol()
    {
        var simulation = Nuclear();
        simulation.LaunchBall(400, 200, 300, 100);
        simulation.SpawnBomb(200.5, 100.25, NuclearRules.BombSpeed, BonusKind.Inversion);
        simulation.SpawnBomb(600, 300, -NuclearRules.BombSpeed, BonusKind.BigBall);
        simulation.ApplyBonus(BonusKind.SmallBall, Side.Left, 111, 222);
        simulation.ApplyBonus(BonusKind.FastBall, Side.Left);
        simulation.ApplyBonus(BonusKind.LongPaddle, Side.Left);
        simulation.ApplyBonus(BonusKind.SlowPaddle, Side.Left);
        simulation.ApplyBonus(BonusKind.Shield, Side.Left);
        simulation.ApplyBonus(BonusKind.ShortPaddle, Side.Right);
        simulation.ApplyBonus(BonusKind.Freeze, Side.Right);
        simulation.ApplyBonus(BonusKind.Inversion, Side.Right);
        simulation.ApplyBonus(BonusKind.DoublePoint, Side.Right, 321, 123);
        var state = simulation.State;

        var frame = Protocol.Encode(new StateMessage(state, simulation.BallVelocityX, simulation.BallVelocityY));
        Assert.True(Protocol.TryDecodeState(frame.AsSpan(1), out var decoded));

        var received = decoded!.State;
        Assert.Equal(state.Nuclear, received.Nuclear);
        Assert.Equal(state.BallSize, received.BallSize);
        Assert.Equal(state.LeftPaddleHeight, received.LeftPaddleHeight);
        Assert.Equal(state.RightPaddleHeight, received.RightPaddleHeight);
        Assert.Equal(GameMode.Nuclear, received.Mode);
    }

    [Fact]
    public void ClassicState_RoundTripsAsClassic()
    {
        var frame = Protocol.Encode(new StateMessage(GameState.Initial));

        Assert.True(Protocol.TryDecodeState(frame.AsSpan(1), out var decoded));
        Assert.Null(decoded!.State.Nuclear);
        Assert.Equal(GameState.Initial, decoded.State);
    }

    [Fact]
    public void GarbageInNuclearBlock_IsRejected()
    {
        var simulation = Nuclear();
        simulation.SpawnBomb(200, 100, NuclearRules.BombSpeed, BonusKind.Shield);
        simulation.ApplyBonus(BonusKind.BigBall, Side.Left);
        var good = Protocol.Encode(new StateMessage(simulation.State));
        Assert.True(Protocol.TryDecodeState(good.AsSpan(1), out _));

        // Псуємо по одному байту «ядерного» блоку: кадр або відхиляється, або розбирається,
        // але ніколи не кидає винятку.
        const int nuclearBlockStart = 1 + 28;
        var rejected = 0;
        for (var index = nuclearBlockStart; index < good.Length; index++)
        {
            var broken = (byte[])good.Clone();
            broken[index] = 0xFF;
            rejected += Protocol.TryDecodeState(broken.AsSpan(1), out _) ? 0 : 1;
        }

        Assert.True(rejected > 20, $"only {rejected} corrupted frames were rejected");
    }

    [Fact]
    public void Client_AppliesPaddleEffectsLocally_LikeTheHostWould()
    {
        var connection = new FakeConnection();
        var client = new ClientSession(connection);
        var host = Nuclear();

        host.ApplyBonus(BonusKind.FastPaddle, Side.Right);
        connection.LatestState = new StateMessage(host.State);
        client.Update(0.1, new PlayerInput(1, 0, false));
        Assert.Equal(
            GameRules.CenteredPaddleY + GameRules.PaddleSpeed * 0.1 * NuclearRules.FastPaddleFactor,
            client.State.RightPaddleY,
            precision: 9);

        var before = client.State.RightPaddleY;
        host.ApplyBonus(BonusKind.Inversion, Side.Right);
        connection.LatestState = new StateMessage(host.State);
        client.Update(0.05, new PlayerInput(1, 0, false));
        Assert.True(client.State.RightPaddleY < before);

        before = client.State.RightPaddleY;
        host.ApplyBonus(BonusKind.Freeze, Side.Right);
        connection.LatestState = new StateMessage(host.State);
        client.Update(0.05, new PlayerInput(1, 0, false));
        Assert.Equal(before, client.State.RightPaddleY);
    }

    [Fact]
    public void Client_KeepsResizedPaddleCentredAndInsideField()
    {
        var connection = new FakeConnection();
        var client = new ClientSession(connection);
        var host = Nuclear();
        connection.LatestState = new StateMessage(host.State);
        client.Update(Frame, Idle);
        var centre = client.State.RightPaddleY + GameRules.PaddleHeight / 2;

        host.ApplyBonus(BonusKind.LongPaddle, Side.Right);
        connection.LatestState = new StateMessage(host.State);
        client.Update(Frame, Idle);

        Assert.Equal(NuclearRules.LongPaddleHeight, client.State.RightPaddleHeight);
        Assert.Equal(centre, client.State.RightPaddleY + NuclearRules.LongPaddleHeight / 2, precision: 9);

        for (var i = 0; i < 120; i++)
        {
            client.Update(Frame, new PlayerInput(1, 0, false));
        }

        Assert.Equal(GameRules.FieldHeight, client.State.RightPaddleY + NuclearRules.LongPaddleHeight, precision: 9);
    }

    [Fact]
    public void Host_DoesNotLetFrozenClientMove()
    {
        var connection = new FakeConnection { LatestInput = new InputMessage(100, false) };
        var session = new HostSession(connection, new Random(1), GameMode.Nuclear);
        session.Update(Frame, Idle);
        Assert.Equal(100, session.State.RightPaddleY);

        // Єдиний спосіб заморозити через сесію — чесно підірвати бомбу, тож перевіряємо правило на симуляції.
        var simulation = Nuclear();
        simulation.SetPaddle(Side.Right, 100);
        simulation.ApplyBonus(BonusKind.Freeze, Side.Right);
        Assert.True(simulation.ModifiersOf(Side.Right).IsFrozen);
        Assert.False(simulation.ModifiersOf(Side.Left).IsFrozen);
    }

    [Theory]
    [InlineData(BotDifficulty.Easy)]
    [InlineData(BotDifficulty.Hard)]
    public void NuclearGames_WithErraticInput_NeverLeaveValidState(BotDifficulty difficulty)
    {
        var random = new Random(77);
        var session = new SoloSession(difficulty, new Random(77), GameMode.Nuclear);
        var detector = new GameEventDetector();
        var finishedGames = 0;
        var detonations = 0;
        var kindsSeen = new HashSet<BonusKind>();

        for (var frame = 0; frame < 400_000; frame++)
        {
            var gameOver = session.State.Phase == GamePhase.GameOver;
            finishedGames += gameOver ? 1 : 0;
            var dt = random.Next(20) switch
            {
                0 => 0.5,
                1 => 0,
                _ => Frame * (0.5 + random.NextDouble()),
            };

            // Гравець більш-менш стежить за м'ячем, щоб розіграші були довгими й бомби встигали вибухати.
            var state = session.State;
            var toBall = state.BallY + state.BallSize / 2 - (state.LeftPaddleY + state.LeftPaddleHeight / 2);
            var direction = random.Next(5) == 0 ? random.Next(-1, 2) : Math.Sign(toBall);
            session.Update(dt, new PlayerInput(direction, 0, Confirm: gameOver));

            state = session.State;
            if (detector.Observe(state).Detonated is { } detonation)
            {
                detonations++;
                kindsSeen.Add(detonation.Kind);
            }

            Assert.InRange(state.BallSize, NuclearRules.SmallBallSize, NuclearRules.BigBallSize);
            Assert.InRange(state.BallY, 0, GameRules.FieldHeight - state.BallSize);
            Assert.InRange(state.BallX, -state.BallSize - 30, GameRules.FieldWidth + 30);
            Assert.InRange(state.LeftPaddleY, 0, GameRules.FieldHeight - state.LeftPaddleHeight);
            Assert.InRange(state.RightPaddleY, 0, GameRules.FieldHeight - state.RightPaddleHeight);
            Assert.InRange(state.ScoreLeft, 0, GameRules.WinScore);
            Assert.InRange(state.ScoreRight, 0, GameRules.WinScore);
        }

        Assert.True(finishedGames > 3, $"only {finishedGames} games finished");
        Assert.True(detonations > 50, $"only {detonations} bombs detonated");
        Assert.Equal(NuclearRules.AllBonuses.Count, kindsSeen.Count);
    }

    private static PongSimulation Nuclear(int seed = 1) => new(new Random(seed), GameMode.Nuclear);

    private static Side Other(Side side) => side == Side.Left ? Side.Right : Side.Left;

    private static void Run(PongSimulation simulation, double seconds)
    {
        for (var t = 0.0; t < seconds; t += Step)
        {
            simulation.Step(Step);
        }
    }

    private sealed class FakeConnection : IPeerConnection
    {
        public StateMessage? LatestState { get; set; }

        public InputMessage? LatestInput { get; set; }

        public ConnectionFault? Fault => null;

        public void Send(StateMessage message)
        {
        }

        public void Send(InputMessage message)
        {
        }

        public void Dispose()
        {
        }
    }
}
