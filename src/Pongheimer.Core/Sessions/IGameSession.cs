using Pongheimer.Core.Game;
using Pongheimer.Core.Networking;

namespace Pongheimer.Core.Sessions;

/// <summary>
/// Ввід із клавіатури за кадр. Напрямки: -1 — вгору, 1 — вниз, 0 — стояти.
/// «Ліва рука» — W/S, «права рука» — стрілки: коли за клавіатурою один гравець, він керує
/// будь-якою з них, а коли двоє — кожному дістається своя.
/// </summary>
/// <param name="LeftHand">Напрямок від клавіш W/S.</param>
/// <param name="RightHand">Напрямок від стрілок.</param>
/// <param name="Confirm">Утримується клавіша «ще раз».</param>
/// <param name="TogglePause">Цього кадру натиснули паузу (одноразова подія, а не утримання).</param>
public readonly record struct PlayerInput(int LeftHand, int RightHand, bool Confirm, bool TogglePause = false)
{
    /// <summary>Напрямок для єдиного гравця за клавіатурою.</summary>
    public int Direction => Math.Sign(Math.Sign(LeftHand) + Math.Sign(RightHand));
}

/// <summary>
/// Одна партія з погляду цієї машини. UI не знає, хто суперник — бот, людина в мережі чи сусід
/// по клавіатурі — і хто веде симуляцію: він лише передає ввід і малює <see cref="State"/>.
/// </summary>
public interface IGameSession : IDisposable
{
    /// <summary>Бік гравця за цією машиною або null, якщо за нею грають обидва.</summary>
    Side? LocalSide { get; }

    GameState State { get; }

    /// <summary>Не null, якщо партію неможливо продовжити через втрату зв'язку.</summary>
    ConnectionFault? Fault { get; }

    void Update(double dt, PlayerInput input);
}
