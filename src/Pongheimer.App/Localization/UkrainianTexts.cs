using Pongheimer.Core.Game;
using Pongheimer.Core.Networking;

namespace Pongheimer.Localization;

public sealed class UkrainianTexts : Texts
{
    public override string ModeClassic => "Класичний";

    public override string ModeNuclear => "Ядерний";

    public override string HostGame => "Створити гру (хост)";

    public override string PlayBot => "Грати проти бота";

    public override string BotEasy => "Легкий";

    public override string BotMedium => "Середній";

    public override string BotHard => "Складний";

    public override string PlayLocalDuel => "Грати вдвох на цьому ПК";

    public override string DuelControlsHint => "Лівий: W/S  ·  Правий: ↑/↓  ·  P — пауза  ·  Esc — меню";

    public override string LeftPlayerWins => "Переміг лівий гравець!";

    public override string RightPlayerWins => "Переміг правий гравець!";

    public override string JoinPrompt => "або підключитись за IP хоста:";

    public override string Join => "Підключитись";

    public override string Cancel => "Скасувати";

    public override string ControlsHint => "W/S або ↑/↓ — рух  ·  P — пауза  ·  Esc — меню";

    public override string Paused => "Пауза";

    public override string ResumeHint => "P — продовжити";

    public override string Victory => "Перемога!";

    public override string Defeat => "Поразка";

    public override string RematchHint => "Space — ще раз";

    public override string SoundOn => "Звук: увімк.";

    public override string SoundOff => "Звук: вимк.";

    public override string Connecting => "Підключаюсь…";

    public override string UnexpectedError => "Сталася несподівана помилка. Деталі — у журналі.";

    public override string StartupFailed => "oPongheimer не зміг запуститись.";

    public override string WaitingForOpponent(IReadOnlyList<string> addresses, int port)
    {
        var ip = addresses.Count > 0 ? string.Join(", ", addresses) : "не знайдено — перевір мережу";
        return $"Чекаю на суперника…\nТвій IP: {ip}\nПорт: {port}";
    }

    public override string Describe(ConnectionError error, int port) => error switch
    {
        ConnectionError.InvalidAddress => "Не схоже на адресу. Введи IP хоста, наприклад 192.168.1.20.",
        ConnectionError.ConnectionRefused => "За цією адресою ніхто не чекає на гру. Хост уже натиснув «Створити гру»?",
        ConnectionError.HostUnreachable => "Хост не відповідає. Перевір IP, мережу та брандмауер на боці хоста.",
        ConnectionError.PortInUse => $"Порт {port} уже зайнятий — можливо, гра вже запущена як хост.",
        ConnectionError.ListenFailed => $"Не вдалося відкрити порт {port}.",
        ConnectionError.NotPongheimer => "За цією адресою відповідає не oPongheimer.",
        ConnectionError.VersionMismatch => "У тебе й у хоста різні версії гри. Візьміть однаковий exe.",
        _ => UnexpectedError,
    };

    public override string BonusName(BonusKind kind) => kind switch
    {
        BonusKind.BigBall => "Великий м'яч",
        BonusKind.SmallBall => "Малий м'яч",
        BonusKind.FastBall => "Швидкий м'яч",
        BonusKind.NormalSpeedBall => "Звичайна швидкість",
        BonusKind.Shockwave => "Ударна хвиля",
        BonusKind.LongPaddle => "Довга ракетка",
        BonusKind.ShortPaddle => "Коротка ракетка",
        BonusKind.FastPaddle => "Швидка ракетка",
        BonusKind.SlowPaddle => "Повільна ракетка",
        BonusKind.Shield => "Щит",
        BonusKind.DoublePoint => "Подвійне очко",
        BonusKind.Freeze => "Заморозка",
        BonusKind.Inversion => "Інверсія",
        _ => "",
    };

    public override string Describe(ConnectionFault fault) => fault switch
    {
        ConnectionFault.Closed => "Суперник вийшов із гри.",
        ConnectionFault.TimedOut => "Зв'язок із суперником втрачено.",
        ConnectionFault.ProtocolViolation => "Суперник надіслав некоректні дані — з'єднання розірвано.",
        _ => UnexpectedError,
    };
}
