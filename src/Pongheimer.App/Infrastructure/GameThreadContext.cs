using System.Collections.Concurrent;

namespace Pongheimer.Infrastructure;

/// <summary>
/// Повертає продовження async-методів в ігровий потік. Завдяки йому код меню може просто писати
/// «await підключення» і далі чіпати стан гри, не думаючи про потоки: усе, що йде після await,
/// виконається на початку найближчого кадру.
/// </summary>
public sealed class GameThreadContext : SynchronizationContext
{
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _pending = new();

    public override void Post(SendOrPostCallback d, object? state) => _pending.Enqueue((d, state));

    public override void Send(SendOrPostCallback d, object? state) => _pending.Enqueue((d, state));

    public override SynchronizationContext CreateCopy() => this;

    /// <summary>Виконує все, що накопичилось; викликається раз на кадр з ігрового потоку.</summary>
    public void Pump()
    {
        while (_pending.TryDequeue(out var work))
        {
            work.Callback(work.State);
        }
    }
}
