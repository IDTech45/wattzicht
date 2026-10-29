using System.Globalization;
using System.Text.Json;

namespace Wattzicht.Api.Connectors;

/// <summary>
/// Defensieve hulpfuncties voor JSON van fabrikanten: veldnamen wisselen per firmware,
/// getallen komen soms als string en keys verschillen in hoofdlettergebruik.
/// </summary>
internal static class JsonHelpers
{
    /// <summary>Zoekt een property hoofdletterongevoelig.</summary>
    public static bool TryGetPropertyIgnoreCase(this JsonElement element, string name, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (element.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }

    /// <summary>Leest een getal; accepteert ook numerieke strings. Geeft <c>null</c> als het veld ontbreekt of onleesbaar is.</summary>
    public static double? GetDoubleOrNull(this JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetPropertyIgnoreCase(name, out var value))
            {
                continue;
            }

            switch (value.ValueKind)
            {
                case JsonValueKind.Number when value.TryGetDouble(out var d):
                    return d;
                case JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                    return parsed;
            }
        }

        return null;
    }

    /// <summary>Als <see cref="GetDoubleOrNull"/>, maar met 0 als fallback.</summary>
    public static double GetDoubleOrZero(this JsonElement element, params string[] names) =>
        element.GetDoubleOrNull(names) ?? 0d;

    /// <summary>Leest een string; getallen worden naar tekst omgezet.</summary>
    public static string? GetStringOrNull(this JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetPropertyIgnoreCase(name, out var value))
            {
                continue;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
        }

        return null;
    }

    /// <summary>Haalt een geneste property op via een pad, bijvoorbeeld <c>data.plants</c>.</summary>
    public static bool TryGetPath(this JsonElement element, string path, out JsonElement value)
    {
        value = element;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!value.TryGetPropertyIgnoreCase(segment, out value))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Haalt een waarde uit een credentials-dictionary, of <c>null</c> als die leeg is.</summary>
    public static string? Get(this IReadOnlyDictionary<string, string> credentials, string key)
    {
        if (credentials.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        // Fallback hoofdletterongevoelig (dictionaries uit JSON kunnen een andere comparer hebben).
        foreach (var pair in credentials)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(pair.Value))
            {
                return pair.Value.Trim();
            }
        }

        return null;
    }
}
