using System.Globalization;
using Wattzicht.App.Services;
using Wattzicht.Shared;

namespace Wattzicht.App.Converters;

public sealed class InvertedBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b ? !b : false;
}

public sealed class IsNotNullOrEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            null => false,
            string s => !string.IsNullOrWhiteSpace(s),
            System.Collections.ICollection c => c.Count > 0,
            _ => true
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Watt (int/double/decimal) naar tekst in kW, bijv. "2,4 kW". Negatief wordt absoluut gemaakt.</summary>
public sealed class PowerToKwConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double watts = value switch
        {
            int i => i,
            long l => l,
            double d => d,
            decimal m => (double)m,
            float f => f,
            _ => 0
        };
        return $"{Math.Abs(watts) / 1000.0:0.0} kW".Replace('.', ',');
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Merk naar kleur voor de initiaal-cirkel. Kleuren komen uit Colors.xaml.</summary>
public sealed class BrandToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            DeviceBrand.Growatt => "Accent500",
            DeviceBrand.Solax => "Accent500",
            DeviceBrand.Enphase => "Accent500",
            DeviceBrand.Sessy => "EnergyBattery",
            DeviceBrand.Deye => "Accent500",
            DeviceBrand.Alfen => "EnergyCharger",
            DeviceBrand.Zaptec => "EnergyCharger",
            DeviceBrand.HomeWizard => "EnergyGrid",
            DeviceBrand.Demo => "TextMuted",
            _ => "TextMuted"
        };
        return ThemeColors.Get(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class OnlineToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value is true ? "EnergyBattery" : "ErrorDot";
        return ThemeColors.Get(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Logo van de installateur (white-label): een https-link of een data:image-URL (base64) uit het portal.
/// Leeg of onleesbaar geeft <c>null</c>, dan toont de pagina niets. SVG wordt door MAUI niet als data-URL ondersteund.
/// </summary>
public sealed class LogoSourceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not string text || string.IsNullOrWhiteSpace(text))
            return null;

        if (text.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            if (text.StartsWith("data:image/svg", StringComparison.OrdinalIgnoreCase))
                return null;

            var comma = text.IndexOf(";base64,", StringComparison.Ordinal);
            if (comma < 0) return null;
            try
            {
                var bytes = System.Convert.FromBase64String(text[(comma + 8)..]);
                return ImageSource.FromStream(() => new MemoryStream(bytes));
            }
            catch (FormatException)
            {
                return null;
            }
        }

        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? ImageSource.FromUri(uri)
            : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}
