using System.Globalization;
using Wattzicht.Shared;

namespace Wattzicht.App.Services;

/// <summary>
/// Een export van je fabrikant kiezen en inlezen.
///
/// Dit staat apart omdat het vanaf twee plekken moet kunnen. Het zat alleen in Instellingen, en de
/// knop bij je apparaten bracht je daarheen; daarna moest je zelf naar beneden scrollen om het
/// blokje te vinden. Een knop die ergens anders heen gaat en je daar laat zoeken is geen knop.
/// </summary>
public sealed class HistorieInlezer(ApiClient api)
{
    private static readonly CultureInfo Nl = CultureInfo.GetCultureInfo("nl-NL");

    /// <summary>
    /// Vraagt om een bestand en leest het in. Geeft de regel terug die op het scherm hoort, of null
    /// als de bewoner de keuze afbrak.
    /// </summary>
    public async Task<string?> KiesEnLeesAsync()
    {
        FileResult? keuze;
        try
        {
            keuze = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = Taal.T("historie.kies-bestand"),
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    // Android kent geen bestandsextensies maar MIME-typen, en een CSV uit een
                    // mailbijlage komt daar regelmatig binnen als text/plain of als octet-stream.
                    [DevicePlatform.Android] = ["text/csv", "text/comma-separated-values", "text/plain", "application/vnd.ms-excel", "application/octet-stream"],
                    [DevicePlatform.iOS] = ["public.comma-separated-values-text", "public.plain-text"],
                    [DevicePlatform.WinUI] = [".csv", ".txt"],
                }),
            });
        }
        catch (Exception)
        {
            return Taal.T("historie.mislukt");
        }

        if (keuze is null) return null;

        try
        {
            await using var stroom = await keuze.OpenReadAsync();
            var uit = await api.ImporteerHistorieAsync(stroom, keuze.FileName);

            var tekst = string.Format(
                Taal.T("historie.gelukt"),
                uit.Dagen,
                uit.Van.ToString("d MMM yyyy", Nl),
                uit.Tot.ToString("d MMM yyyy", Nl),
                uit.TotaalKwh.ToString("0", Nl));

            if (uit.Overgeslagen > 0)
            {
                tekst += " " + string.Format(Taal.T("historie.overgeslagen"), uit.Overgeslagen);
            }

            // Eerlijk erbij: een ingelezen dag heeft alleen opwek. Wat iemand die dag zelf gebruikte
            // of terugleverde weten we niet, dus blijft de besparing van die dagen leeg.
            return tekst + " " + Taal.T("historie.alleen-opwek");
        }
        catch (ApiException ex)
        {
            // De server vertelt precies wat er niet klopte, inclusief welke kolommen hij zag staan.
            // "Ongeldig bestand" helpt niemand.
            return ex.Message;
        }
        catch (Exception)
        {
            return Taal.T("historie.mislukt");
        }
    }
}
