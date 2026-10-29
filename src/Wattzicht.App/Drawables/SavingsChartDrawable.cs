namespace Wattzicht.App.Drawables;

/// <summary>Eén staaf in de besparingsgrafiek.</summary>
public sealed record ChartBar(string Label, double Value);

/// <summary>
/// Eenvoudige staafgrafiek zonder externe library. Tekent staven in de sun-kleur,
/// een basislijn, labels onder de staven en de maximale waarde linksboven.
/// </summary>
public sealed class SavingsChartDrawable : IDrawable
{
    public IReadOnlyList<ChartBar> Bars { get; set; } = [];
    public string ValueSuffix { get; set; } = "";
    public string ValuePrefix { get; set; } = "€ ";

    public Color BarColor { get; set; } = Color.FromArgb("#F59E0B");
    public Color BarColorLight { get; set; } = Color.FromArgb("#FFD166");
    public Color AxisColor { get; set; } = Color.FromArgb("#E2E8F0");
    public Color TextColor { get; set; } = Color.FromArgb("#64748B");

    /// <summary>Toon maximaal zoveel labels onder de x-as (voorkomt overlap bij 30+ staven).</summary>
    public int MaxLabels { get; set; } = 7;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.Antialias = true;

        const float padLeft = 8f;
        const float padRight = 8f;
        const float padTop = 20f;
        const float padBottom = 22f;

        var plot = new RectF(
            dirtyRect.X + padLeft,
            dirtyRect.Y + padTop,
            Math.Max(0, dirtyRect.Width - padLeft - padRight),
            Math.Max(0, dirtyRect.Height - padTop - padBottom));

        // Basislijn
        canvas.StrokeColor = AxisColor;
        canvas.StrokeSize = 1;
        canvas.DrawLine(plot.Left, plot.Bottom, plot.Right, plot.Bottom);

        if (Bars.Count == 0)
        {
            canvas.FontColor = TextColor;
            canvas.FontSize = 13;
            canvas.DrawString("Nog geen gegevens", plot, HorizontalAlignment.Center, VerticalAlignment.Center);
            return;
        }

        var max = Bars.Max(b => b.Value);
        if (max <= 0) max = 1;

        // Max-waarde linksboven
        canvas.FontColor = TextColor;
        canvas.FontSize = 11;
        canvas.DrawString($"{ValuePrefix}{max:0.00}{ValueSuffix}".Replace('.', ','),
            new RectF(plot.Left, dirtyRect.Y + 2, plot.Width, 14), HorizontalAlignment.Left, VerticalAlignment.Top);

        // Hulplijn op 50%
        canvas.StrokeColor = AxisColor;
        canvas.StrokeDashPattern = [3, 3];
        var midY = plot.Bottom - plot.Height / 2f;
        canvas.DrawLine(plot.Left, midY, plot.Right, midY);
        canvas.StrokeDashPattern = null;

        int n = Bars.Count;
        float slot = plot.Width / n;
        float gap = Math.Clamp(slot * 0.25f, 1f, 8f);
        float barWidth = Math.Max(2f, slot - gap);
        float radius = Math.Min(4f, barWidth / 2f);

        int labelEvery = Math.Max(1, (int)Math.Ceiling(n / (double)MaxLabels));

        for (int i = 0; i < n; i++)
        {
            var bar = Bars[i];
            float x = plot.Left + i * slot + gap / 2f;
            float h = (float)(bar.Value / max) * plot.Height;
            if (bar.Value > 0 && h < 2f) h = 2f;
            float y = plot.Bottom - h;

            canvas.FillColor = i == n - 1 ? BarColorLight : BarColor;
            canvas.FillRoundedRectangle(x, y, barWidth, h, radius);

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
}
