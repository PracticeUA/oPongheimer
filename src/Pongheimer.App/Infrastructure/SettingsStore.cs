using System.IO;
using System.Text.Json;
using Pongheimer.Core.Diagnostics;
using Pongheimer.Core.Game;

namespace Pongheimer.Infrastructure;

public sealed class AppSettings
{
    public string LastHost { get; set; } = "";

    /// <summary>Код мови («uk», «en») або порожньо, якщо гравець ще не обирав.</summary>
    public string Language { get; set; } = "";

    public bool SoundEnabled { get; set; } = true;

    public BotDifficulty BotDifficulty { get; set; } = BotDifficulty.Medium;
}

public interface ISettingsStore
{
    AppSettings Current { get; }

    /// <summary>Зберігає <see cref="Current"/>.</summary>
    void Save();
}

/// <summary>Налаштування в JSON-файлі. Зіпсований чи недоступний файл — не привід не запуститись.</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string _path;
    private readonly ILog _log;

    public JsonSettingsStore(string path, ILog log)
    {
        _path = path;
        _log = log;
        Current = Load();
    }

    public AppSettings Current { get; }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(Current));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"Cannot save settings: {ex.Message}");
        }
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path)) ?? new AppSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _log.Warn($"Cannot load settings: {ex.Message}");
        }

        return new AppSettings();
    }
}
