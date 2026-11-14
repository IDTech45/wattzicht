using Wattzicht.App.Services;
using Wattzicht.Shared;

namespace Wattzicht.App.Drawables;

/// <summary>
/// De energiestroom als tekening: zon, huis, net, batterij en laadpaal met lijnen ertussen.
///
/// Waarom dit er is: op wattzicht.nl staat deze tekening en in de app stonden vier kaartjes met
/// kale getallen. Die getallen kloppen, maar ze vertellen niet waar de stroom heen gaat, en dat is
/// precies wat iemand wil zien als hij de app opent. Een plaatje leest in één oogopslag; vier
/// getallen moet je naast elkaar leggen en zelf uitrekenen.
///
/// Twee regels bepalen alles wat hier getekend wordt:
///
/// Alleen tonen wat er is. Geen batterij gekoppeld, geen batterij in beeld. Een tegel "Batterij,
/// 0,0 kW, Rust" bij iemand zonder batterij is geen informatie maar een apparaat dat er niet is.
///
/// En niets verzinnen. Meten we het huisverbruik en de netkant niet, dan staat er niet 0,0 kW maar
/// "Niet gemeten", en er loopt geen stroompijl naar iets waarvan we de richting niet kennen.
///
/// Tekenconventie (uit Wattzicht.Shared): GridPowerW groter dan nul is afname van het net, kleiner
/// dan nul teruglevering; BatteryPowerW groter dan nul is ontladen, kleiner dan nul laden.
/// </summary>
public sealed class EnergyFlowDrawable : IDrawable
{
    /// <summary>De momentopname die getekend wordt; <c>null</c> betekent nog geen meting.</summary>
    public LiveSnapshotDto? Snapshot { get; set; }

    /// <summary>Hoogte van één rij: de bol met daaronder twee regels tekst.</summary>
    public const float RijHoogte = 80f;

    private const float BolRadius = 22f;
    private const float Drempel = 50f;

    /// <summary>
    /// Hoe hoog de tekening moet zijn. Twee rijen zonder batterij en zonder laadpaal, drie met.
    ///
    /// De GraphicsView moet zijn hoogte van buiten krijgen (hij kan niet meten wat hij gaat
    /// tekenen), dus staat de rekensom hier en niet in de XAML: zo kan er geen gat of een afgesneden
    /// onderrand ontstaan als de indeling verandert.
    /// </summary>
    public static double HoogteVoor(LiveSnapshotDto? snapshot) =>
        (snapshot?.HasBattery == true || snapshot?.HasCharger == true) ? RijHoogte * 3 : RijHoogte * 2;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.Antialias = true;

        var s = Snapshot;
        var heeftBatterij = s?.HasBattery ?? false;
        var heeftLaadpaal = s?.HasCharger ?? false;
        var onderRij = heeftBatterij || heeftLaadpaal;

        var rijen = onderRij ? 3 : 2;
        var top = dirtyRect.Y + Math.Max(0, (dirtyRect.Height - rijen * RijHoogte) / 2f);

        float Rij(int index) => top + index * RijHoogte + BolRadius + 2f;
        float X(double deel) => dirtyRect.X + (float)(dirtyRect.Width * deel);

        var zon = new PointF(X(0.18), Rij(0));
        var net = new PointF(X(0.82), Rij(0));
        var huis = new PointF(X(0.5), Rij(1));
        var batterij = new PointF(heeftLaadpaal ? X(0.18) : X(0.5), Rij(2));
        var laadpaal = new PointF(heeftBatterij ? X(0.82) : X(0.5), Rij(2));

        var pv = s?.PvPowerW ?? 0;
        var huisW = s?.LoadPowerW ?? 0;
        var netW = s?.GridPowerW ?? 0;
        var batterijW = s?.BatteryPowerW ?? 0;
        var laadpaalW = s?.ChargerPowerW ?? 0;
        var meetHuis = s?.HasLoadData ?? true;
        var meetNet = s?.HasGridData ?? true;

        // Eerst alle lijnen, dan de bollen: zo verdwijnt het uiteinde van elke lijn onder de bol en
        // hoeft er niets op een halve pixel uitgerekend te worden.
        Lijn(canvas, zon, huis, pv > Drempel, Kleur("EnergySolar"));

        if (meetNet)
        {
            if (netW > Drempel)
            {
                Lijn(canvas, net, huis, true, Kleur("EnergyGrid"));
            }
            else
            {
                Lijn(canvas, huis, net, netW < -Drempel, Kleur("EnergyGrid"));
            }
        }
        else
        {
            // Geen pijl en geen kleur: we weten niet of er iets loopt, laat staan welke kant op.
            Lijn(canvas, huis, net, false, Kleur("EnergyGrid"));
        }

        if (heeftBatterij)
        {
            if (batterijW > Drempel)
            {
                Lijn(canvas, batterij, huis, true, Kleur("EnergyBattery"));
            }
            else
            {
                Lijn(canvas, huis, batterij, batterijW < -Drempel, Kleur("EnergyBattery"));
            }
        }

        if (heeftLaadpaal)
        {
            Lijn(canvas, huis, laadpaal, laadpaalW > Drempel, Kleur("EnergyCharger"));
        }

        Bol(canvas, zon, Kleur("EnergySolar"), Zacht("EnergySolarSoft", "EnergySolarSoftDark"));
        ZonGlyph(canvas, zon);
        Tekst(canvas, zon, Taal.T("stroom.zon"), Kw(pv));

        Bol(canvas, net, Kleur("EnergyGrid"), Zacht("EnergyGridSoft", "EnergyGridSoftDark"));
        MastGlyph(canvas, net);
        Tekst(canvas, net, Taal.T("stroom.net"), meetNet ? Kw(netW) : Taal.T("stroom.niet-gemeten"));

        Bol(canvas, huis, Thema("EnergyHome", "EnergyHomeDark"), Zacht("Surface", "SurfaceDark"));
        HuisGlyph(canvas, huis);
        Tekst(canvas, huis, Taal.T("stroom.huis"), meetHuis ? Kw(huisW) : Taal.T("stroom.niet-gemeten"));

        if (heeftBatterij)
        {
            Bol(canvas, batterij, Kleur("EnergyBattery"), Zacht("EnergyBatterySoft", "EnergyBatterySoftDark"));
            BatterijGlyph(canvas, batterij, s?.BatterySoc);
            var naam = s?.BatterySoc is { } soc
                ? $"{Taal.T("stroom.batterij")} {soc:0}%"
                : Taal.T("stroom.batterij");
            Tekst(canvas, batterij, naam, Kw(batterijW));
        }

        if (heeftLaadpaal)
        {
            Bol(canvas, laadpaal, Kleur("EnergyCharger"), Zacht("EnergyChargerSoft", "EnergyChargerSoftDark"));
            LaadpaalGlyph(canvas, laadpaal);
            Tekst(canvas, laadpaal, Taal.T("stroom.laadpaal"), Kw(laadpaalW));
        }
    }

    /// <summary>
    /// Een lijn tussen twee bollen, met een pijl halverwege als er stroom loopt.
    ///
    /// Loopt er niets, dan blijft de lijn dun en grijs: de verbinding bestaat, er gaat alleen niets
    /// over. Zonder dat verschil zou een huis in rust eruitzien als een huis waar alles tegelijk
    /// aanstaat.
    /// </summary>
    private static void Lijn(ICanvas canvas, PointF van, PointF naar, bool stroomt, Color kleur)
    {
        var dx = naar.X - van.X;
        var dy = naar.Y - van.Y;
        var lengte = MathF.Sqrt(dx * dx + dy * dy);
        if (lengte < 1)
        {
            return;
        }

        var ex = dx / lengte;
        var ey = dy / lengte;
        var rand = BolRadius + 3f;
        var a = new PointF(van.X + ex * rand, van.Y + ey * rand);
        var b = new PointF(naar.X - ex * rand, naar.Y - ey * rand);

        canvas.StrokeColor = stroomt ? kleur : Thema("Border", "BorderDark");
        canvas.StrokeSize = stroomt ? 3f : 1.5f;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeDashPattern = stroomt ? [2f, 3f] : null;
        canvas.DrawLine(a, b);
        canvas.StrokeDashPattern = null;

        if (!stroomt)
        {
            return;
        }

        var m = new PointF((a.X + b.X) / 2f, (a.Y + b.Y) / 2f);
        var pad = new PathF();
        pad.MoveTo(m.X + ex * 7f, m.Y + ey * 7f);
        pad.LineTo(m.X - ex * 4f - ey * 5f, m.Y - ey * 4f + ex * 5f);
        pad.LineTo(m.X - ex * 4f + ey * 5f, m.Y - ey * 4f - ex * 5f);
        pad.Close();
        canvas.FillColor = kleur;
        canvas.FillPath(pad);
    }

    private static void Bol(ICanvas canvas, PointF midden, Color rand, Color vulling)
    {
        canvas.FillColor = vulling;
        canvas.FillCircle(midden, BolRadius);
        canvas.StrokeColor = rand;
        canvas.StrokeSize = 2f;
        canvas.DrawCircle(midden, BolRadius);
    }

    /// <summary>De naam onder de bol en de waarde eronder; de waarde is het getal waar iemand naar kijkt.</summary>
    private static void Tekst(ICanvas canvas, PointF midden, string naam, string waarde)
    {
        var breedte = 120f;
        var vak = new RectF(midden.X - breedte / 2f, midden.Y + BolRadius + 4f, breedte, 15f);

        canvas.FontColor = Thema("TextMuted", "TextMutedDark");
        canvas.FontSize = 11f;
        canvas.DrawString(naam, vak, HorizontalAlignment.Center, VerticalAlignment.Top);

        canvas.FontColor = Thema("TextBody", "TextBodyDark");
        canvas.FontSize = 13f;
        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.DrawString(waarde, new RectF(vak.X, vak.Y + 14f, breedte, 17f),
            HorizontalAlignment.Center, VerticalAlignment.Top);
        canvas.Font = Microsoft.Maui.Graphics.Font.Default;
    }

    private static void ZonGlyph(ICanvas canvas, PointF p)
    {
        canvas.FillColor = Kleur("EnergySolar");
        canvas.FillCircle(p, 7f);

        canvas.StrokeColor = Kleur("EnergySolar");
        canvas.StrokeSize = 2f;
        canvas.StrokeLineCap = LineCap.Round;
        for (var i = 0; i < 8; i++)
        {
            var hoek = i * MathF.PI / 4f;
            var cos = MathF.Cos(hoek);
            var sin = MathF.Sin(hoek);
            canvas.DrawLine(p.X + cos * 10f, p.Y + sin * 10f, p.X + cos * 14f, p.Y + sin * 14f);
        }
    }

    private static void HuisGlyph(ICanvas canvas, PointF p)
    {
        var pad = new PathF();
        pad.MoveTo(p.X - 11f, p.Y + 2f);
        pad.LineTo(p.X, p.Y - 9f);
        pad.LineTo(p.X + 11f, p.Y + 2f);
        pad.LineTo(p.X + 8f, p.Y + 2f);
        pad.LineTo(p.X + 8f, p.Y + 11f);
        pad.LineTo(p.X - 8f, p.Y + 11f);
        pad.LineTo(p.X - 8f, p.Y + 2f);
        pad.Close();

        canvas.StrokeColor = Thema("EnergyHome", "EnergyHomeDark");
        canvas.StrokeSize = 2f;
        canvas.StrokeLineJoin = LineJoin.Round;
        canvas.DrawPath(pad);
    }

    /// <summary>Een hoogspanningsmast, dezelfde vorm als op de website.</summary>
    private static void MastGlyph(ICanvas canvas, PointF p)
    {
        canvas.StrokeColor = Kleur("EnergyGrid");
        canvas.StrokeSize = 2f;
        canvas.StrokeLineCap = LineCap.Round;

        canvas.DrawLine(p.X - 8f, p.Y + 12f, p.X - 3f, p.Y - 12f);
        canvas.DrawLine(p.X + 8f, p.Y + 12f, p.X + 3f, p.Y - 12f);
        canvas.DrawLine(p.X - 10f, p.Y - 5f, p.X + 10f, p.Y - 5f);
        canvas.DrawLine(p.X - 7f, p.Y + 3f, p.X + 7f, p.Y + 3f);
        canvas.DrawLine(p.X - 5f, p.Y - 12f, p.X + 5f, p.Y - 12f);
    }

    /// <summary>
    /// Een batterij met de laadtoestand erin. Geen laadtoestand bekend, dan alleen de omtrek:
    /// een half gevulde batterij tekenen zonder meting zou een stand suggereren.
    /// </summary>
    private static void BatterijGlyph(ICanvas canvas, PointF p, double? soc)
    {
        var kleur = Kleur("EnergyBattery");
        var romp = new RectF(p.X - 12f, p.Y - 8f, 21f, 16f);

        canvas.StrokeColor = kleur;
        canvas.StrokeSize = 2f;
        canvas.DrawRoundedRectangle(romp, 3f);
        canvas.FillColor = kleur;
        canvas.FillRoundedRectangle(new RectF(p.X + 10f, p.Y - 3f, 3f, 6f), 1f);

        if (soc is { } stand && stand > 0)
        {
            var deel = (float)Math.Clamp(stand / 100.0, 0, 1);
            canvas.FillColor = kleur;
            canvas.FillRoundedRectangle(
                new RectF(romp.X + 2f, romp.Y + 2f, (romp.Width - 4f) * deel, romp.Height - 4f), 2f);
        }
    }

    private static void LaadpaalGlyph(ICanvas canvas, PointF p)
    {
        var kleur = Kleur("EnergyCharger");
        canvas.StrokeColor = kleur;
        canvas.StrokeSize = 2f;
        canvas.StrokeLineCap = LineCap.Round;

        canvas.DrawRoundedRectangle(new RectF(p.X - 9f, p.Y - 12f, 14f, 20f), 3f);
        canvas.FillColor = kleur;
        canvas.FillCircle(p.X - 2f, p.Y - 5f, 2.5f);

        // Het kabeltje: geen cirkeltje maar een bocht, zodat je ziet dat er iets aan vastzit.
        var kabel = new PathF();
        kabel.MoveTo(p.X + 5f, p.Y - 2f);
        kabel.CurveTo(p.X + 12f, p.Y - 2f, p.X + 12f, p.Y + 8f, p.X + 7f, p.Y + 10f);
        canvas.DrawPath(kabel);
    }

    private static string Kw(double watts) =>
        $"{Math.Abs(watts) / 1000.0:0.0} kW".Replace('.', ',');

    private static Color Kleur(string sleutel) => ThemeColors.Get(sleutel);

    private static Color Thema(string licht, string donker) => ThemeColors.Themed(licht, donker);

    /// <summary>Dezelfde als <see cref="Thema"/>; apart genoemd omdat dit de vulling is en niet de lijn.</summary>
    private static Color Zacht(string licht, string donker) => ThemeColors.Themed(licht, donker);
}
