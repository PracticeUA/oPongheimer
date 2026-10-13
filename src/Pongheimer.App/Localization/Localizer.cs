using System.Globalization;

namespace Pongheimer.Localization;

public enum Language
{
    Ukrainian,
    English,
}

/// <summary>
/// Поточна мова інтерфейсу. Екран перемальовується щокадру й щоразу бере тексти з
/// <see cref="Current"/>, тому зміна мови видна одразу, без перезапуску.
/// </summary>
public sealed class Localizer
{
    private static readonly Texts Ukrainian = new UkrainianTexts();
    private static readonly Texts English = new EnglishTexts();

    public Localizer(Language language) => Language = language;

    public Language Language { get; set; }

    public Texts Current => Language == Language.English ? English : Ukrainian;

    public static string ToCode(Language language) => language == Language.English ? "en" : "uk";

    /// <summary>
    /// Збережений вибір гравця, а якщо його немає — мова системи.
    /// Для систем іншими мовами лишається українська як основна мова застосунку.
    /// </summary>
    public static Language Resolve(string? savedCode, CultureInfo systemCulture) => savedCode switch
    {
        "uk" => Language.Ukrainian,
        "en" => Language.English,
        _ => systemCulture.TwoLetterISOLanguageName == "en" ? Language.English : Language.Ukrainian,
    };
}
