using System.Globalization;

namespace Tempo;

/// <summary>
/// Tempo's translation layer. English text is the key; every other language is looked up in a table
/// (see Strings.*.cs). Anything missing falls back to English, so a gap never breaks the UI.
/// </summary>
public static partial class Loc
{
    public sealed record Lang(string Code, string Name, string Culture)
    {
        public override string ToString() => Name;
    }

    // Order matches the columns of every row in the string tables.
    public static readonly Lang[] Languages =
    {
        new("en", "English", "en-US"),
        new("es", "Español", "es-ES"),
        new("fr", "Français", "fr-FR"),
        new("de", "Deutsch", "de-DE"),
        new("pt", "Português (Brasil)", "pt-BR"),
        new("it", "Italiano", "it-IT"),
        new("ja", "日本語", "ja-JP"),
        new("zh", "简体中文", "zh-CN"),
        new("ko", "한국어", "ko-KR"),
    };

    static Dictionary<string, string[]>? table;

    static Dictionary<string, string[]> Table
    {
        get
        {
            if (table != null) return table;
            var t = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var row in AllRows())
            {
                var parts = row.Split('|');
                if (parts.Length != Languages.Length) continue;      // malformed rows are ignored, never fatal
                t[parts[0]] = parts[1..];
            }
            return table = t;
        }
    }

    public static string Current { get; private set; } = "en";
    public static Lang CurrentLang => Languages.First(l => l.Code == Current);
    public static CultureInfo Culture(string? code = null) =>
        CultureInfo.GetCultureInfo((Languages.FirstOrDefault(l => l.Code == (code ?? Current)) ?? Languages[0]).Culture);

    /// <summary>Picks the saved language, or the Windows display language on first run, and applies it.</summary>
    public static void Init()
    {
        var s = State.Data.Settings;
        if (string.IsNullOrEmpty(s.Language) || Languages.All(l => l.Code != s.Language))
        {
            var two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            s.Language = Languages.Any(l => l.Code == two) ? two : "en";
        }
        Set(s.Language);
    }

    public static void Set(string code)
    {
        Current = Languages.Any(l => l.Code == code) ? code : "en";
        var ci = Culture();
        CultureInfo.DefaultThreadCurrentCulture = ci;
        CultureInfo.DefaultThreadCurrentUICulture = ci;
        CultureInfo.CurrentCulture = ci;
        CultureInfo.CurrentUICulture = ci;
    }

    /// <summary>Translate an English UI string into the current language.</summary>
    public static string T(string en) => For(Current, en);

    /// <summary>Translate into a specific language (used for invoices, which remember the language they were issued in).</summary>
    public static string For(string? code, string en)
    {
        if (string.IsNullOrEmpty(en) || string.IsNullOrEmpty(code) || code == "en") return en;
        int i = Array.FindIndex(Languages, l => l.Code == code);
        if (i <= 0) return en;
        return Table.TryGetValue(en, out var tr) && !string.IsNullOrEmpty(tr[i - 1]) ? tr[i - 1] : en;
    }

    /// <summary>Translate a template, then fill its {0}, {1}… placeholders.</summary>
    public static string F(string en, params object?[] args) => string.Format(Culture(), T(en), args);
    public static string FFor(string? code, string en, params object?[] args) => string.Format(Culture(code), For(code, en), args);

    /// <summary>"1 entry" / "5 entries": pick the right template by count, then fill {0}.</summary>
    public static string N(int count, string singular, string plural, params object?[] more) =>
        F(count == 1 ? singular : plural, new object?[] { count }.Concat(more).ToArray());

    /// <summary>Long-ish date like "Nov 17, 2026" in each language's natural order.</summary>
    public static string DatePattern(string? code = null) => (code ?? Current) switch
    {
        "ja" or "zh" => "yyyy年M月d日",
        "ko" => "yyyy년 M월 d일",
        "en" => "MMM d, yyyy",
        _ => "d MMM yyyy",
    };

    public static string ShortMonthDay(string? code = null) => (code ?? Current) switch
    {
        "ja" or "zh" => "M月d日",
        "ko" => "M월 d일",
        "en" => "MMM d",
        _ => "d MMM",
    };
}
