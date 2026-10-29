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
            DeviceBrand.Growatt => "Sun",
            DeviceBrand.Sessy => "Battery",
            DeviceBrand.Deye => "Sun",
            DeviceBrand.Alfen => "Grid",
            DeviceBrand.Zaptec => "Grid",
            DeviceBrand.HomeWizard => "Export",
            DeviceBrand.Demo => "Muted",
            _ => "Muted"
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
        var key = value is true ? "Battery" : "Danger";
        return ThemeColors.Get(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
