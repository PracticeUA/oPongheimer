using System.Buffers.Binary;
using Pongheimer.Core.Game;

namespace Pongheimer.Core.Networking;

/// <summary>
/// Кодує «ядерну» частину стану: режим гри, бомби, ефекти, останній вибух. Блок має фіксований
/// розмір і в класичному режимі заповнений нулями — так кадр стану завжди однакової довжини.
/// </summary>
internal static class NuclearCodec
{
    public const int Size = 1 + BombSize * NuclearRules.MaxBombs + EffectSize * EffectCount + 1 + DetonationSize;

    private const int BombSize = 14;
    private const int EffectSize = 5;
    private const int EffectCount = 10;
    private const int DetonationSize = 11;

    private const byte ShieldLeft = 1;
    private const byte DoublePointLeft = 2;
    private const byte ShieldRight = 4;
    private const byte DoublePointRight = 8;

    private const double PositionSlack = 50;
    private const double MaxBombVelocity = 500;
    private const double MaxEffectSeconds = 60;
    private static readonly byte MaxBonusKind = (byte)NuclearRules.AllBonuses.Max();

    public static void Write(Span<byte> target, NuclearSnapshot? snapshot)
    {
        target[..Size].Clear();
        if (snapshot is null)
        {
            return;
        }

        target[0] = (byte)GameMode.Nuclear;
        var cursor = target[1..];
        for (var i = 0; i < NuclearRules.MaxBombs; i++)
        {
            cursor = WriteBomb(cursor, i < snapshot.Bombs.Count ? snapshot.Bombs[i] : null);
        }

        cursor = WriteEffect(cursor, snapshot.BallSize);
        cursor = WriteEffect(cursor, snapshot.BallSpeed);
        foreach (var side in (ReadOnlySpan<SideEffects>)[snapshot.Left, snapshot.Right])
        {
            cursor = WriteEffect(cursor, side.Size);
            cursor = WriteEffect(cursor, side.Speed);
            cursor = WriteEffect(cursor, side.Freeze);
            cursor = WriteEffect(cursor, side.Inversion);
        }

        cursor[0] = (byte)(
            (snapshot.Left.HasShield ? ShieldLeft : 0)
            | (snapshot.Left.HasDoublePoint ? DoublePointLeft : 0)
            | (snapshot.Right.HasShield ? ShieldRight : 0)
            | (snapshot.Right.HasDoublePoint ? DoublePointRight : 0));
        cursor = cursor[1..];

        var detonation = snapshot.LastDetonation;
        cursor[0] = detonation.Count;
        BinaryPrimitives.WriteSingleLittleEndian(cursor[1..], (float)detonation.X);
        BinaryPrimitives.WriteSingleLittleEndian(cursor[5..], (float)detonation.Y);
        cursor[9] = (byte)detonation.Kind;
        cursor[10] = (byte)detonation.Owner;
    }

    /// <summary>Читає блок; у класичному режимі <paramref name="snapshot"/> — null. False для некоректних даних.</summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out NuclearSnapshot? snapshot)
    {
        snapshot = null;
        switch (source[0])
        {
            case (byte)GameMode.Classic:
                return true;
            case (byte)GameMode.Nuclear:
                break;
            default:
                return false;
        }

        var cursor = source[1..];
        var bombs = new List<Bomb>(NuclearRules.MaxBombs);
        for (var i = 0; i < NuclearRules.MaxBombs; i++)
        {
            if (!TryReadBomb(ref cursor, out var bomb))
            {
                return false;
            }

            if (bomb is { } value)
            {
                bombs.Add(value);
            }
        }

        Span<TimedEffect> effects = stackalloc TimedEffect[EffectCount];
        for (var i = 0; i < EffectCount; i++)
        {
            if (!TryReadEffect(ref cursor, out effects[i]))
            {
                return false;
            }
        }

        var flags = cursor[0];
        cursor = cursor[1..];
        if (flags > (ShieldLeft | DoublePointLeft | ShieldRight | DoublePointRight))
        {
            return false;
        }

        double detonationX = BinaryPrimitives.ReadSingleLittleEndian(cursor[1..]);
        double detonationY = BinaryPrimitives.ReadSingleLittleEndian(cursor[5..]);
        if (!IsPosition(detonationX, GameRules.FieldWidth)
            || !IsPosition(detonationY, GameRules.FieldHeight)
            || cursor[9] > MaxBonusKind
            || cursor[10] > (byte)Side.Right)
        {
            return false;
        }

        snapshot = new NuclearSnapshot(
            new BombList(bombs),
            effects[0],
            effects[1],
            new SideEffects(
                effects[2], effects[3], effects[4], effects[5],
                (flags & ShieldLeft) != 0, (flags & DoublePointLeft) != 0),
            new SideEffects(
                effects[6], effects[7], effects[8], effects[9],
                (flags & ShieldRight) != 0, (flags & DoublePointRight) != 0),
            new Detonation(cursor[0], detonationX, detonationY, (BonusKind)cursor[9], (Side)cursor[10]));
        return true;
    }

    private static Span<byte> WriteBomb(Span<byte> target, Bomb? bomb)
    {
        if (bomb is { } value)
        {
            target[0] = 1;
            BinaryPrimitives.WriteSingleLittleEndian(target[1..], (float)value.X);
            BinaryPrimitives.WriteSingleLittleEndian(target[5..], (float)value.Y);
            BinaryPrimitives.WriteSingleLittleEndian(target[9..], (float)value.VelocityY);
            target[13] = (byte)value.Kind;
        }

        return target[BombSize..];
    }

    private static bool TryReadBomb(ref ReadOnlySpan<byte> cursor, out Bomb? bomb)
    {
        bomb = null;
        var source = cursor;
        cursor = cursor[BombSize..];
        switch (source[0])
        {
            case 0:
                return true;
            case 1:
                break;
            default:
                return false;
        }

        double x = BinaryPrimitives.ReadSingleLittleEndian(source[1..]);
        double y = BinaryPrimitives.ReadSingleLittleEndian(source[5..]);
        double velocityY = BinaryPrimitives.ReadSingleLittleEndian(source[9..]);
        var kind = source[13];
        if (!IsPosition(x, GameRules.FieldWidth)
            || !IsPosition(y, GameRules.FieldHeight)
            || !double.IsFinite(velocityY)
            || Math.Abs(velocityY) > MaxBombVelocity
            || kind == (byte)BonusKind.None
            || kind > MaxBonusKind)
        {
            return false;
        }

        bomb = new Bomb(x, y, velocityY, (BonusKind)kind);
        return true;
    }

    private static Span<byte> WriteEffect(Span<byte> target, TimedEffect effect)
    {
        if (effect.IsActive)
        {
            target[0] = (byte)effect.Kind;
            BinaryPrimitives.WriteSingleLittleEndian(target[1..], (float)effect.Remaining);
        }

        return target[EffectSize..];
    }

    private static bool TryReadEffect(ref ReadOnlySpan<byte> cursor, out TimedEffect effect)
    {
        effect = default;
        var kind = cursor[0];
        double remaining = BinaryPrimitives.ReadSingleLittleEndian(cursor[1..]);
        cursor = cursor[EffectSize..];
        if (kind > MaxBonusKind || !double.IsFinite(remaining) || remaining < 0 || remaining > MaxEffectSeconds)
        {
            return false;
        }

        if (kind != (byte)BonusKind.None && remaining > 0)
        {
            effect = new TimedEffect((BonusKind)kind, remaining);
        }

        return true;
    }

    private static bool IsPosition(double value, double fieldSize) =>
        double.IsFinite(value) && value >= -PositionSlack && value <= fieldSize + PositionSlack;
}
