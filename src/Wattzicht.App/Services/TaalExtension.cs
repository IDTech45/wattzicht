using System.Globalization;
using Microsoft.Maui.Controls.Xaml;

namespace Wattzicht.App.Services;

/// <summary>
/// De XAML-kant van het taalpakket: <c>Text="{t:T home.vandaag}"</c>.
///
/// Dit geeft geen vaste tekst terug maar een binding op <see cref="Taal.Nu"/>. Dat verschil is het
/// hele punt: bij een vaste tekst zou wisselen van taal pas na een herstart te zien zijn, en dan
/// zou de helft van het scherm nog in de oude taal staan.
///
/// Waarom een eigen extensie en geen gewone binding in de XAML: deze pagina's gebruiken compiled
/// bindings met een x:DataType op de pagina zelf. Een handmatige binding naar een ander object zou
/// daar per stuk een eigen x:DataType bij moeten krijgen, en dat is honderdzeventig keer dezelfde
/// regel ruis.
/// </summary>
[ContentProperty(nameof(Key))]
// De naam is kort met opzet: XAML laat het achtervoegsel Extension weg, dus dit leest als
// {t:T sleutel}. Honderdzeventig keer een lange naam zou de XAML onleesbaar maken.
public sealed class TExtension : IMarkupExtension<BindingBase>
{
    /// <summary>De sleutel uit het taalpakket, bijvoorbeeld <c>meer.uitloggen</c>.</summary>
    public string Key { get; set; } = string.Empty;

    public BindingBase ProvideValue(IServiceProvider serviceProvider) => new Binding
    {
        Mode = BindingMode.OneWay,
        Path = $"[{Key}]",
        Source = Taal.Nu,
    };

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) =>
        ProvideValue(serviceProvider);
}

/// <summary>
/// Hetzelfde, maar voor plekken waar geen binding kan: <c>ToolTipProperties.Text</c> op sommige
/// platformen, en attributen die de XAML-compiler als vaste waarde wil zien.
/// </summary>
public sealed class TsExtension : IMarkupExtension<string>
{
    public string Key { get; set; } = string.Empty;

    public string ProvideValue(IServiceProvider serviceProvider) => Taal.T(Key);

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) =>
        ProvideValue(serviceProvider);
}
