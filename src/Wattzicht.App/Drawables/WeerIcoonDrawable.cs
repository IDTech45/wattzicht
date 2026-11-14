using Wattzicht.App.Services;
using Wattzicht.Shared;

namespace Wattzicht.App.Drawables;

/// <summary>
/// Een klein weericoon: zon, wolk, regen, sneeuw, onweer of mist.
///
/// Getekend en niet als emoji of plaatje. Een emoji ziet er op elk toestel anders uit en haalt op
/// een donkere achtergrond vaak geen contrast; een png zou per thema en per schermdichtheid
/// meegeleverd moeten worden. Dit is een paar lijnen in de kleuren die al in Colors.xaml staan, dus
/// het past automatisch bij de rest en bij beide thema's.
/// </summary>
public sealed class WeerIcoonDrawable : IDrawable
{
    public WeerSoort Soort { get; set; } = WeerSoort.Bewolkt;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.Antialias = true;

        var m = new PointF(dirtyRect.Center.X, dirtyRect.Center.Y);
        var maat = Math.Min(dirtyRect.Width, dirtyRect.Height) / 32f;

        switch (Soort)
        {
            case WeerSoort.Zonnig:
                Zon(canvas, m, maat, 8f);
                break;
            case WeerSoort.HalfBewolkt:
                Zon(canvas, new PointF(m.X + 6f * maat, m.Y - 6f * maat), maat, 5f);
                Wolk(canvas, new PointF(m.X - 2f * maat, m.Y + 2f * maat), maat);
                break;
            case WeerSoort.Bewolkt:
                Wolk(canvas, m, maat);
                break;
            case WeerSoort.Mist:
                Wolk(canvas, new PointF(m.X, m.Y - 3f * maat), maat);
                Strepen(canvas, m, maat, Grijs());
                break;
            case WeerSoort.Regen:
                Wolk(canvas, new PointF(m.X, m.Y - 4f * maat), maat);
                Druppels(canvas, m, maat, ThemeColors.Get("EnergyCharger"));
                break;
            case WeerSoort.Sneeuw:
                Wolk(canvas, new PointF(m.X, m.Y - 4f * maat), maat);
                Vlokken(canvas, m, maat);
                break;
            case WeerSoort.Onweer:
                Wolk(canvas, new PointF(m.X, m.Y - 4f * maat), maat);
                Bliksem(canvas, m, maat);
                break;
        }
    }

    private static void Zon(ICanvas canvas, PointF p, float maat, float radius)
    {
        var geel = ThemeColors.Get("EnergySolar");
        canvas.FillColor = geel;
        canvas.FillCircle(p, radius * maat);

        canvas.StrokeColor = geel;
        canvas.StrokeSize = 2f * maat;
        canvas.StrokeLineCap = LineCap.Round;
        for (var i = 0; i < 8; i++)
        {
            var hoek = i * MathF.PI / 4f;
            var cos = MathF.Cos(hoek);
            var sin = MathF.Sin(hoek);
            canvas.DrawLine(
                p.X + cos * (radius + 3f) * maat, p.Y + sin * (radius + 3f) * maat,
                p.X + cos * (radius + 6f) * maat, p.Y + sin * (radius + 6f) * maat);
        }
    }

    private static void Wolk(ICanvas canvas, PointF p, float maat)
    {
        canvas.FillColor = Grijs();
        canvas.FillCircle(p.X - 5f * maat, p.Y + 1f * maat, 5f * maat);
        canvas.FillCircle(p.X + 1f * maat, p.Y - 2f * maat, 7f * maat);
        canvas.FillCircle(p.X + 7f * maat, p.Y + 1f * maat, 5f * maat);
        canvas.FillRoundedRectangle(p.X - 9f * maat, p.Y + 1f * maat, 17f * maat, 5f * maat, 2.5f * maat);
    }

    private static void Druppels(ICanvas canvas, PointF p, float maat, Color kleur)
    {
        canvas.StrokeColor = kleur;
        canvas.StrokeSize = 2f * maat;
        canvas.StrokeLineCap = LineCap.Round;
        for (var i = -1; i <= 1; i++)
        {
            var x = p.X + i * 5f * maat;
            canvas.DrawLine(x, p.Y + 5f * maat, x - 1.5f * maat, p.Y + 10f * maat);
        }
    }

    private static void Vlokken(ICanvas canvas, PointF p, float maat)
    {
        canvas.FillColor = ThemeColors.Themed("TextMuted", "TextBodyDark");
        for (var i = -1; i <= 1; i++)
        {
            canvas.FillCircle(p.X + i * 5f * maat, p.Y + 7f * maat, 1.6f * maat);
        }
    }

    private static void Strepen(ICanvas canvas, PointF p, float maat, Color kleur)
    {
        canvas.StrokeColor = kleur;
        canvas.StrokeSize = 2f * maat;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.DrawLine(p.X - 8f * maat, p.Y + 6f * maat, p.X + 8f * maat, p.Y + 6f * maat);
        canvas.DrawLine(p.X - 5f * maat, p.Y + 11f * maat, p.X + 6f * maat, p.Y + 11f * maat);
    }

    private static void Bliksem(ICanvas canvas, PointF p, float maat)
    {
        var pad = new PathF();
        pad.MoveTo(p.X + 1f * maat, p.Y + 3f * maat);
        pad.LineTo(p.X - 4f * maat, p.Y + 3f * maat);
        pad.LineTo(p.X + 0f * maat, p.Y + 11f * maat);
        pad.LineTo(p.X - 1f * maat, p.Y + 6f * maat);
        pad.LineTo(p.X + 4f * maat, p.Y + 6f * maat);
        pad.Close();

        canvas.FillColor = ThemeColors.Get("EnergySolar");
        canvas.FillPath(pad);
    }

    private static Color Grijs() => ThemeColors.Themed("EnergyGrid", "TextMutedDark");
}
