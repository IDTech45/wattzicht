namespace Wattzicht.App.Services;

/// <summary>Leest kleuren uit Colors.xaml vanuit C# (converters, drawables, views).</summary>
public static class ThemeColors
{
    public static Color Get(string key)
    {
        if (Application.Current is { } app && app.Resources.TryGetValue(key, out var value) && value is Color color)
            return color;
        return Colors.Gray;
    }

    /// <summary>Kiest de light- of dark-variant op basis van het huidige thema.</summary>
    public static Color Themed(string lightKey, string darkKey) =>
        Application.Current?.RequestedTheme == AppTheme.Dark ? Get(darkKey) : Get(lightKey);
}
