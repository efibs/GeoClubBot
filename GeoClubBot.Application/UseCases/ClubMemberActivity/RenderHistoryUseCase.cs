using SkiaSharp;
using UseCases.InputPorts.ClubMemberActivity;

namespace UseCases.UseCases.ClubMemberActivity;

public class RenderHistoryUseCase : IRenderHistoryUseCase
{
    public MemoryStream RenderHistory(List<int> values, List<DateTimeOffset> timestamps, int target)
    {
        // Chart dimensions
        const int width = 800;
        const int height = 600;
        const int padding = 60;
        const int bottomPadding = 80;
        const double yStep = 20.0;

        var chartWidth = width - 2 * padding;
        var chartHeight = height - padding - bottomPadding;

        // Prepare data
        var minTime = timestamps[0].Ticks;
        var maxTime = timestamps[^1].Ticks;
        var timeRange = maxTime - minTime;

        // Calculate Y-axis range
        var maxValue = values.Max();
        var minValue = Math.Min(0, values.Min());
        var yMin = Math.Floor(minValue / yStep) * yStep;
        var yMax = Math.Ceiling(maxValue / yStep) * yStep;
        var valueRange = yMax - yMin;

        // Coordinate conversion helpers
        double TimeToX(long ticks) =>
            padding + (ticks - minTime) * chartWidth / (double)timeRange;

        double ValueToY(double val) =>
            height - bottomPadding - ((val - yMin) * chartHeight / valueRange);

        // Initialize canvas
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);

        // Draw grid lines and Y-axis labels
        DrawGrid(canvas, padding, width, yMin, yMax, yStep, ValueToY);

        // Draw data bars
        DrawBars(canvas, values, timestamps, TimeToX, ValueToY, yMin);

        // Draw target line
        DrawThresholdLine(canvas, padding, width, target, ValueToY);
        
        // Draw axes
        DrawAxes(canvas, padding, width, height, bottomPadding);

        // Draw X-axis labels (timestamps)
        DrawXAxisLabels(canvas, timestamps, height, bottomPadding, TimeToX);

        // Draw titles and labels
        DrawLabels(canvas, width, height);

        // Encode and return as PNG
        return EncodeToPng(bitmap);
    }

    private void DrawGrid(SKCanvas canvas, int padding, int width, double yMin, double yMax,
        double yStep, Func<double, double> valueToY)
    {
        using var gridPaint = new SKPaint();
        gridPaint.Color = new SKColor(220, 220, 220);
        gridPaint.Style = SKPaintStyle.Stroke;
        gridPaint.StrokeWidth = 1;

        using var textPaint = new SKPaint();
        textPaint.Color = SKColors.Black;
        textPaint.IsAntialias = true;

        var textFont = SKTypeface.FromFamilyName("Arial");
        using var font = new SKFont(textFont);

        for (var val = yMin; val <= yMax; val += yStep)
        {
            var y = (float)valueToY(val);
            canvas.DrawLine(padding, y, width - padding, y, gridPaint);
            canvas.DrawText(val.ToString("F0"), padding - 35, y + 5, font, textPaint);
        }
    }

    private void DrawBars(SKCanvas canvas, List<int> values, List<DateTimeOffset> timestamps,
        Func<long, double> timeToX, Func<double, double> valueToY, double yMin)
    {
        using var barPaint = new SKPaint();
        barPaint.Color = new SKColor(70, 130, 180);
        barPaint.Style = SKPaintStyle.Fill;

        using var borderPaint = new SKPaint();
        borderPaint.Color = SKColors.Black;
        borderPaint.Style = SKPaintStyle.Stroke;
        borderPaint.StrokeWidth = 1;

        for (int i = 0; i < values.Count; i++)
        {
            var x1 = (float)timeToX(timestamps[i].Ticks);
            var x2 = (float)timeToX(timestamps[i+1].Ticks);
            var y = (float)valueToY(values[i]);
            var baseY = (float)valueToY(yMin);

            var rect = new SKRect(x1, y, x2, baseY);
            canvas.DrawRect(rect, barPaint);
            canvas.DrawRect(rect, borderPaint);
        }
    }

    private void DrawAxes(SKCanvas canvas, int padding, int width, int height, int bottomPadding)
    {
        using var axisPaint = new SKPaint();
        axisPaint.Color = SKColors.Black;
        axisPaint.Style = SKPaintStyle.Stroke;
        axisPaint.StrokeWidth = 2;

        // Y-axis
        canvas.DrawLine(padding, padding, padding, height - bottomPadding, axisPaint);

        // X-axis
        canvas.DrawLine(padding, height - bottomPadding, width - padding, height - bottomPadding, axisPaint);
    }

    private void DrawThresholdLine(SKCanvas canvas, int padding, int width, 
        double thresholdValue, Func<double, double> valueToY)
    {
        var y = (float)valueToY(thresholdValue);

        using var dashedPaint = new SKPaint();
        dashedPaint.Color = new SKColor(220, 53, 69); // Red color
        dashedPaint.Style = SKPaintStyle.Stroke;
        dashedPaint.StrokeWidth = 2;
        dashedPaint.PathEffect = SKPathEffect.CreateDash([10, 5], 0);

        canvas.DrawLine(padding, y, width - padding, y, dashedPaint);

        // Optional: Draw label for threshold
        using var textPaint = new SKPaint();
        textPaint.Color = new SKColor(220, 53, 69);
        textPaint.IsAntialias = true;

        var textFont = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);
        using var font = new SKFont(textFont, 11);
        canvas.DrawText($"Target: {thresholdValue:F0}", width - padding + 5, y + 4, font, textPaint);
    }
    
    private void DrawXAxisLabels(SKCanvas canvas, List<DateTimeOffset> timestamps,
        int height, int bottomPadding, Func<long, double> timeToX)
    {
        using var tickPaint = new SKPaint();
        tickPaint.Color = SKColors.Black;
        tickPaint.Style = SKPaintStyle.Stroke;
        tickPaint.StrokeWidth = 2;

        using var textPaint = new SKPaint();
        textPaint.Color = SKColors.Black;
        textPaint.IsAntialias = true;

        var textFont = SKTypeface.FromFamilyName("Arial");
        using var font = new SKFont(textFont);

        foreach (var timestamp in timestamps)
        {
            var x = (float)timeToX(timestamp.Ticks);

            // Draw tick mark
            canvas.DrawLine(x, height - bottomPadding, x, height - bottomPadding + 5, tickPaint);

            // Draw rotated label
            var label = timestamp.ToString("dd.MM.yyyy");
            canvas.Save();
            canvas.Translate(x, height - bottomPadding + 10);
            canvas.RotateDegrees(45);
            canvas.DrawText(label, 0, 0, font, textPaint);
            canvas.Restore();
        }
    }

    private void DrawLabels(SKCanvas canvas, int width, int height)
    {
        using var textPaint = new SKPaint();
        textPaint.Color = SKColors.Black;
        textPaint.IsAntialias = true;

        var textFont = SKTypeface.FromFamilyName("Arial");

        // Title
        var titleFont = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);
        using var titleSkFont = new SKFont(titleFont, 18);
        // ReSharper disable once PossibleLossOfFraction
        canvas.DrawText("XP History", width / 2 - 100, 30, titleSkFont, textPaint);

        // Axis labels
        using var axisFont = new SKFont(textFont);
        // ReSharper disable once PossibleLossOfFraction
        canvas.DrawText("Time", width / 2 - 20, height - 10, axisFont, textPaint);

        canvas.Save();
        // ReSharper disable once PossibleLossOfFraction
        canvas.Translate(15, height / 2);
        canvas.RotateDegrees(-90);
        canvas.DrawText("XP", 0, 0, axisFont, textPaint);
        canvas.Restore();
    }

    private MemoryStream EncodeToPng(SKBitmap bitmap)
    {
        var ms = new MemoryStream();
        using (var image = SKImage.FromBitmap(bitmap))
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        {
            data.SaveTo(ms);
        }

        ms.Position = 0;
        return ms;
    }
}