using Wattzicht.App.Services;

namespace Wattzicht.App.Drawables;

/// <summary>
/// Eén staaf in de besparingsgrafiek.
///
/// <paramref name="Label"/> is het korte opschrift onder de as ("wo", "12"), <paramref name="Uitleg"/>
/// de hele datum zoals je hem uitspreekt ("woensdag 12 november"). Die twee lopen uiteen omdat er
/// onder een staaf van acht pixels breed geen datum past, maar je hem wel wilt kunnen lezen zodra je
/// de staaf aantikt.
/// </summary>
public sealed record ChartBar(string Label, double Value, string? Uitleg = null);

/// <summary>
/// Eenvoudige staafgrafiek zonder externe library.
///
/// Wat hier gebeurt is bewust meer dan staven tekenen. De eerste versie liet alleen blokjes zien met
/// kale getallen eronder (1, 4, 7, 10, 13) en de hoogste waarde klein linksboven. De vraag die daarop
/// kwam was terecht: zijn dat dagen, en hoeveel is één staaf dan? Daarom nu:
/// - een schaal rechts, bij de lijnen waar hij over gaat, in plaats van één getal in een hoek;
/// - de waarde boven de hoogste staaf, want dat is het getal dat je zoekt;
/// - een staaf die je kunt aantikken, waarna de datum en de waarde eronder verschijnen.
/// </summary>
public sealed class SavingsChartDrawable : IDrawable
{
    public IReadOnlyList<ChartBar> Bars { get; set; } = [];
    public string ValueSuffix { get; set; } = "";
    public string ValuePrefix { get; set; } = "€ ";

    /// <summary>Welke staaf de bewoner heeft aangetikt, of -1 als er niets gekozen is.</summary>
    public int Gekozen { get; set; } = -1;

    // Geen kleurcodes hier: alle kleuren staan in Colors.xaml en worden gezet door SavingsViewModel.
    // Deze beginwaarden zijn alleen vangnet voor het geval de ViewModel nog niets heeft gezet.
    public Color BarColor { get; set; } = ThemeColors.Get("Accent500");
    public Color BarColorLight { get; set; } = ThemeColors.Get("Accent300");
    public Color AxisColor { get; set; } = ThemeColors.Get("Border");
    public Color TextColor { get; set; } = ThemeColors.Get("TextMuted");

    /// <summary>Toon maximaal zoveel labels onder de x-as (voorkomt overlap bij 30+ staven).</summary>
    public int MaxLabels { get; set; } = 7;

    /// <summary>Breedte die de schaal rechts nodig heeft. Ook gebruikt om een tik om te rekenen.</summary>
    private const float SchaalBreedte = 44f;

    private const float PadLinks = 8f;
    private const float PadBoven = 18f;
    private const float PadOnder = 22f;

    /// <summary>
    /// Welke staaf hoort bij een tik op deze x-positie? Geeft -1 buiten de staven.
    ///
    /// Staat hier en niet in de pagina, want alleen deze klasse weet waar de staven terechtkomen.
    /// </summary>
    public int StaafBij(float x, float breedte)
    {
        if (Bars.Count == 0) return -1;

        var plotBreedte = Math.Max(0, breedte - PadLinks - SchaalBreedte);
        if (plotBreedte <= 0) return -1;

        var slot = plotBreedte / Bars.Count;
        var index = (int)Math.Floor((x - PadLinks) / slot);
        return index >= 0 && index < Bars.Count ? index : -1;
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.Antialias = true;

        var plot = new RectF(
            dirtyRect.X + PadLinks,
            dirtyRect.Y + PadBoven,
            Math.Max(0, dirtyRect.Width - PadLinks - SchaalBreedte),
            Math.Max(0, dirtyRect.Height - PadBoven - PadOnder));

        // Basislijn
        canvas.StrokeColor = AxisColor;
        canvas.StrokeSize = 1;
        canvas.DrawLine(plot.Left, plot.Bottom, plot.Right, plot.Bottom);

        var max = Bars.Count == 0 ? 0 : Bars.Max(b => b.Value);

        // Geen staven, of overal nul. Dat tweede zag eruit als een werkende grafiek met een lege
        // schaal eronder, en dat is misleidend: er is dan niets gemeten, niet nul opgewekt.
        if (Bars.Count == 0 || max <= 0)
        {
            canvas.FontColor = TextColor;
            canvas.FontSize = 13;
            canvas.DrawString(Taal.T("besparen.nog-geen-gegevens"), dirtyRect,
                HorizontalAlignment.Center, VerticalAlignment.Center);
            return;
        }

        // Schaal rechts: bij de lijn waar hij over gaat, niet als los getal in een hoek.
        canvas.FontColor = TextColor;
        canvas.FontSize = 10;
        var midY = plot.Bottom - plot.Height / 2f;
        Schaal(canvas, plot, max, plot.Top);
        Schaal(canvas, plot, max / 2, midY);

        // Hulplijnen op 100% en 50%, zodat de hoogte van een staaf iets betekent.
        canvas.StrokeColor = AxisColor;
        canvas.StrokeDashPattern = [3, 3];
        canvas.DrawLine(plot.Left, plot.Top, plot.Right, plot.Top);
        canvas.DrawLine(plot.Left, midY, plot.Right, midY);
        canvas.StrokeDashPattern = null;

        int n = Bars.Count;
        float slot = plot.Width / n;
        float gap = Math.Clamp(slot * 0.25f, 1f, 8f);
        float barWidth = Math.Max(2f, slot - gap);
        float radius = Math.Min(4f, barWidth / 2f);

        int labelEvery = Math.Max(1, (int)Math.Ceiling(n / (double)MaxLabels));
        int hoogste = HoogsteIndex();

        for (int i = 0; i < n; i++)
        {
            var bar = Bars[i];
            float x = plot.Left + i * slot + gap / 2f;
            float h = (float)(bar.Value / max) * plot.Height;
            if (bar.Value > 0 && h < 2f) h = 2f;
            float y = plot.Bottom - h;

            canvas.FillColor = i == Gekozen || i == n - 1 ? BarColorLight : BarColor;
            canvas.FillRoundedRectangle(x, y, barWidth, h, radius);

            // De gekozen staaf krijgt een rand, anders is hij niet te onderscheiden van de laatste
            // staaf, die ook licht is.
            if (i == Gekozen)
            {
                canvas.StrokeColor = BarColor;
                canvas.StrokeSize = 2;
                canvas.DrawRoundedRectangle(x, y, barWidth, h, radius);
            }

            // De waarde boven de hoogste staaf: dat is het getal waar iemand naar zoekt, en het
            // maakt meteen duidelijk wat de schaal rechts betekent.
            if (i == hoogste && bar.Value > 0)
            {
                canvas.FontColor = TextColor;
                canvas.FontSize = 10;
                canvas.DrawString(Waarde(bar.Value),
                    new RectF(x - slot, Math.Max(dirtyRect.Y, y - 15), barWidth + slot * 2, 13),
                    HorizontalAlignment.Center, VerticalAlignment.Bottom);
            }

            if (i % labelEvery == 0 || i == n - 1)
            {
                canvas.FontColor = TextColor;
                canvas.FontSize = 10;
                canvas.DrawString(bar.Label,
                    new RectF(x - slot, plot.Bottom + 4, barWidth + slot * 2, 16),
                    HorizontalAlignment.Center, VerticalAlignment.Top);
            }
        }
    }

    /// <summary>De hoogste staaf; bij gelijke waarden de laatste, want dat is de meest recente.</summary>
    private int HoogsteIndex()
    {
        int beste = 0;
        for (int i = 1; i < Bars.Count; i++)
            if (Bars[i].Value >= Bars[beste].Value) beste = i;
        return beste;
    }

    private void Schaal(ICanvas canvas, RectF plot, double waarde, float y) =>
        canvas.DrawString(Waarde(waarde),
            new RectF(plot.Right + 4, y - 7, SchaalBreedte - 6, 14),
            HorizontalAlignment.Left, VerticalAlignment.Center);

    /// <summary>
    /// Een waarde zoals je hem uitspreekt. Centen bij euro's, één decimaal bij kilowatturen:
    /// "106,00 kWh" leest als een meetfout.
    /// </summary>
    public string Waarde(double waarde)
    {
        var tekst = ValueSuffix.Contains("kWh", StringComparison.OrdinalIgnoreCase)
            ? $"{ValuePrefix}{waarde:0.#}{ValueSuffix}"
            : $"{ValuePrefix}{waarde:0.00}{ValueSuffix}";
        return tekst.Replace('.', ',');
    }
}
