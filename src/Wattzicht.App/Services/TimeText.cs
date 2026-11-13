using System.Globalization;

namespace Wattzicht.App.Services;

/// <summary>
/// Relatieve tijd in de taal van de app ("3 min geleden"). Accepteert DateTime, DateTimeOffset of
/// null, zodat het type in Wattzicht.Shared mag verschillen zonder dat de app breekt.
/// </summary>
public static class TimeText
{
    /// <summary>
    /// De datumopmaak volgt de gekozen taal, niet de telefoon. Anders staat er "3 mrt" onder een
    /// Turks scherm, en dat leest als een foutje.
    /// </summary>
    private static CultureInfo Cultuur => Taal.Code switch
    {
        "en" => CultureInfo.GetCultureInfo("en-GB"),
        "tr" => CultureInfo.GetCultureInfo("tr-TR"),
        _ => CultureInfo.GetCultureInfo("nl-NL"),
    };

    public static DateTimeOffset? ToLocal(object? value) => value switch
    {
        DateTimeOffset dto => dto.ToLocalTime(),
        DateTime dt => new DateTimeOffset(dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt).ToLocalTime(),
        _ => null
    };

    /// <summary>"zojuist", "5 min geleden", "om 14:20", "3 mrt 09:12"</summary>
    public static string Ago(object? value, string? empty = null)
    {
        var local = ToLocal(value);
        if (local is null) return empty ?? Taal.T("tijd.nog-niet");
        var age = DateTimeOffset.Now - local.Value;
        if (age < TimeSpan.FromMinutes(1)) return Taal.T("tijd.zojuist");
        if (age < TimeSpan.FromHours(1)) return string.Format(Taal.T("tijd.minuten-geleden"), (int)age.TotalMinutes);
        if (age < TimeSpan.FromHours(24)) return string.Format(Taal.T("tijd.uren-geleden"), (int)age.TotalHours);
        return local.Value.ToString("d MMM HH:mm", Cultuur);
    }

    /// <summary>"Sinds 12 min", "Sinds 3 uur", "Sinds 3 mrt 09:12"</summary>
    public static string Since(object? value)
    {
        var local = ToLocal(value);
        if (local is null) return "";
        var age = DateTimeOffset.Now - local.Value;
        if (age < TimeSpan.FromHours(1)) return string.Format(Taal.T("tijd.sinds-minuten"), (int)Math.Max(1, age.TotalMinutes));
        if (age < TimeSpan.FromHours(24)) return string.Format(Taal.T("tijd.sinds-uren"), (int)age.TotalHours);
        return string.Format(Taal.T("tijd.sinds-datum"), local.Value.ToString("d MMM HH:mm", Cultuur));
    }
}
