using System.Globalization;

namespace Wattzicht.App.Services;

/// <summary>
/// Relatieve tijd in het Nederlands ("3 min geleden"). Accepteert DateTime, DateTimeOffset of null,
/// zodat het type in Wattzicht.Shared mag verschillen zonder dat de app breekt.
/// </summary>
public static class TimeText
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    public static DateTimeOffset? ToLocal(object? value) => value switch
    {
        DateTimeOffset dto => dto.ToLocalTime(),
        DateTime dt => new DateTimeOffset(dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt).ToLocalTime(),
        _ => null
    };

    /// <summary>"zojuist", "5 min geleden", "om 14:20", "3 mrt 09:12"</summary>
    public static string Ago(object? value, string empty = "nog niet")
    {
        var local = ToLocal(value);
        if (local is null) return empty;
        var age = DateTimeOffset.Now - local.Value;
        if (age < TimeSpan.FromMinutes(1)) return "zojuist";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes} min geleden";
        if (age < TimeSpan.FromHours(24)) return $"{(int)age.TotalHours} uur geleden";
        return local.Value.ToString("d MMM HH:mm", Nl);
    }

    /// <summary>"Sinds 12 min", "Sinds 3 uur", "Sinds 3 mrt 09:12"</summary>
    public static string Since(object? value)
    {
        var local = ToLocal(value);
        if (local is null) return "";
        var age = DateTimeOffset.Now - local.Value;
        if (age < TimeSpan.FromHours(1)) return $"Sinds {(int)Math.Max(1, age.TotalMinutes)} min";
        if (age < TimeSpan.FromHours(24)) return $"Sinds {(int)age.TotalHours} uur";
        return $"Sinds {local.Value.ToString("d MMM HH:mm", Nl)}";
    }
}
