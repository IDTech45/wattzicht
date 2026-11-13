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
///
/// Waarom de binding naar <see cref="Taal.Versie"/> kijkt en niet naar een indexer op de sleutel:
/// een pad als <c>[home.vandaag]</c> moet door de padlezer van MAUI ontleed worden, en die splitst
/// op punten. Of hij punten binnen blokhaken netjes overslaat durf ik niet op te schrijven zonder
/// het op een toestel gezien te hebben, en als het misgaat is elk label in de app leeg. Een pad dat
/// gewoon een propertynaam is kan niet verkeerd gelezen worden; de sleutel gaat als
/// ConverterParameter mee, en die raakt een padlezer nooit aan.
/// </summary>
[ContentProperty(nameof(Key))]
// AcceptEmptyServiceProvider: deze extensie heeft de serviceprovider van XAML niet nodig, en met dit
// attribuut gebruikt de compiler het snelle pad in plaats van een waarschuwing te geven.
[AcceptEmptyServiceProvider]
// De naam is kort met opzet: XAML laat het achtervoegsel Extension weg, dus dit leest als
// {t:T sleutel}. Honderdzeventig keer een lange naam zou de XAML onleesbaar maken.
public sealed class TExtension : IMarkupExtension<BindingBase>
{
    /// <summary>De sleutel uit het taalpakket, bijvoorbeeld <c>meer.uitloggen</c>.</summary>
    public string Key { get; set; } = string.Empty;

    public BindingBase ProvideValue(IServiceProvider serviceProvider) => new Binding
    {
        Mode = BindingMode.OneWay,
        Path = nameof(Taal.Versie),
        Source = Taal.Nu,
        Converter = TaalConverter.Instance,
        ConverterParameter = Key,
    };

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) =>
        ProvideValue(serviceProvider);
}

/// <summary>
/// Zet de sleutel om naar de tekst in de gekozen taal.
///
/// De waarde die binnenkomt doet niet mee; die is er alleen om de binding te laten vuren als de taal
/// wisselt. Wat telt is de sleutel in <paramref name="parameter"/>.
/// </summary>
public sealed class TaalConverter : IValueConverter
{
    public static readonly TaalConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Taal.T(parameter as string ?? string.Empty);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Hetzelfde, maar als vaste tekst in plaats van een binding: <c>{t:Ts sleutel}</c>.
///
/// Voor plekken waar geen binding kan, bijvoorbeeld een attribuut dat de XAML-compiler als vaste
/// waarde wil zien. Zo'n tekst verandert niet mee bij het wisselen van taal.
/// </summary>
[AcceptEmptyServiceProvider]
public sealed class TsExtension : IMarkupExtension<string>
{
    public string Key { get; set; } = string.Empty;

    public string ProvideValue(IServiceProvider serviceProvider) => Taal.T(Key);

    object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) =>
        ProvideValue(serviceProvider);
}
