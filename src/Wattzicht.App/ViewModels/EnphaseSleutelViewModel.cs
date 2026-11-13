using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wattzicht.App.Services;

namespace Wattzicht.App.ViewModels;

/// <summary>
/// Hoe je je eigen Enphase-sleutel aanmaakt.
///
/// Waarom dit scherm bestaat: Enphase telt aanroepen per developer-app en niet per woning, dus met
/// onze gedeelde sleutel delen alle klanten samen duizend per maand. Daarom lezen we een Enphase
/// maar eens per vijf uur uit en staan er nog geen dagcijfers per paneel. Wie zelf een gratis app
/// aanmaakt heeft zijn eigen duizend.
///
/// Dat stond alleen op de website, en wie de telefoon gebruikt komt daar nooit. Het hoort te staan
/// waar iemand de vraag krijgt, en dat is bij zijn panelen.
/// </summary>
public partial class EnphaseSleutelViewModel : BaseViewModel
{
    /// <summary>
    /// Het adres dat in de eigen Enphase-app als Redirect URI moet staan.
    ///
    /// Eén vast adres en geen keuze: het moet letterlijk overeenkomen met wat onze server accepteert,
    /// en een bewoner die hier zelf iets verzint komt halverwege het toestemmingsscherm vast te
    /// zitten met een foutmelding van Enphase waar niemand iets aan heeft.
    /// </summary>
    public string Terugkeeradres => "https://wattzicht.nl/oauth/enphase";

    [ObservableProperty] private string _kopieerTekst = Taal.T("sleutel.kopieer");

    [RelayCommand]
    private async Task KopieerAsync()
    {
        try
        {
            await Clipboard.Default.SetTextAsync(Terugkeeradres);
            KopieerTekst = Taal.T("sleutel.gekopieerd");

            // Even laten staan en dan terug: anders blijft er op het scherm staan dat je zojuist iets
            // kopieerde terwijl je al drie minuten verder bent.
            await Task.Delay(2000);
            KopieerTekst = Taal.T("sleutel.kopieer");
        }
        catch (Exception)
        {
            KopieerTekst = Taal.T("sleutel.kopieer");
        }
    }

    [RelayCommand]
    private Task NaarEnphaseAsync() => Open("https://developer-v4.enphase.com");

    [RelayCommand]
    private Task NaarKoppelenAsync() => Open("https://wattzicht.nl/koppelen");

    private static async Task Open(string adres)
    {
        try
        {
            await Launcher.Default.OpenAsync(adres);
        }
        catch (Exception)
        {
            // Geen browser beschikbaar. Niets om te herstellen en niets om over te klagen: het adres
            // staat op het scherm, dus wie wil kan het overtypen.
        }
    }
}
