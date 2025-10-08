using Entities;
using SkiaSharp;
using UseCases.InputPorts.ClubMemberActivity;

namespace UseCases.UseCases.ClubMemberActivity;

public class RenderHistoryUseCase : IRenderHistoryUseCase
{
    public MemoryStream RenderHistory(List<HistoryEntry> history)
    {
        const int width = 800;
        const int height = 600;
        const int padding = 60;
        const int bottomPadding = 80;

        var chartWidth = width - 2 * padding;
        var chartHeight = height - padding - bottomPadding;

        // Create bitmap and canvas
        var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);

        // White background
        canvas.Clear(SKColors.White);

        // Ensure history is sorted by timestamp (oldest first)
        var sortedHistory = history.OrderBy(e => e.Timestamp).ToList();

        // Find min/max for scaling
        var minTime = sortedHistory[0].Timestamp.Ticks;
        var maxTime = sortedHistory[^1].Timestamp.Ticks;
        var maxValue = sortedHistory.Select(e => e.Xp).Max();
        var minValue = sortedHistory.Select(e => e.Xp).Min();
        if (minValue > 0) minValue = 0; // Start from 0 if all positive

        // Calculate Y-axis steps (round to nearest 20)
        const double yStep = 20.0;
        var yMin = Math.Floor(minValue / yStep) * yStep;
        var yMax = Math.Ceiling(maxValue / yStep) * yStep;

        var timeRange = maxTime - minTime;
        var valueRange = yMax - yMin;

        // Helper function to convert data to pixel coordinates
        double TimeToX(long ticks) => padding + (ticks - minTime) * chartWidth / (double)timeRange;
        double ValueToY(double val) => height - bottomPadding - ((val - yMin) * chartHeight / valueRange);

        // Draw bars
        var barPaint = new SKPaint
        {
            Color = new SKColor(70, 130, 180),
            Style = SKPaintStyle.Fill
        };

        var borderPaint = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1
        };

        for (int i = 0; i < sortedHistory.Count - 1; i++)
        {
            var x1 = (float)TimeToX(sortedHistory[i].Timestamp.Ticks);
            var x2 = (float)TimeToX(sortedHistory[i + 1].Timestamp.Ticks);

            // The bar from timestamp[i] to timestamp[i+1] shows the value at timestamp[i]
            var y = (float)ValueToY(sortedHistory[i].Xp);
            var baseY = (float)ValueToY(yMin);

            var rect = new SKRect(x1, y, x2, baseY);
            canvas.DrawRect(rect, barPaint);
            canvas.DrawRect(rect, borderPaint);
        }

        // Draw axes
        var axisPaint = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2
        };

        // Y-axis
        canvas.DrawLine(padding, padding, padding, height - bottomPadding, axisPaint);

        // X-axis
        canvas.DrawLine(padding, height - bottomPadding, width - padding, height - bottomPadding, axisPaint);

        // Draw grid and labels
        var gridPaint = new SKPaint
        {
            Color = new SKColor(220, 220, 220),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1
        };

        var textPaint = new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = true
        };
        var textFont = SKTypeface.FromFamilyName("Arial");

        // Y-axis labels and grid (in 20-steps)
        for (double val = yMin; val <= yMax; val += yStep)
        {
            var y = (float)ValueToY(val);

            canvas.DrawLine(padding, y, width - padding, y, gridPaint);

            using var font = new SKFont(textFont, 12);
            canvas.DrawText(val.ToString("F0"), padding - 35, y + 5, font, textPaint);
        }

        // X-axis labels (timestamps) - label every timestamp as a tick mark
        var tickPaint = new SKPaint
        {
            Color = SKColors.Black,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2
        };

        for (int i = 0; i < sortedHistory.Count; i++)
        {
            var x = (float)TimeToX(sortedHistory[i].Timestamp.Ticks);

            // Draw tick mark
            canvas.DrawLine(x, height - bottomPadding, x, height - bottomPadding + 5, tickPaint);

            // Draw label
            var label = sortedHistory[i].Timestamp.ToString("dd.MM.yyyy");

            canvas.Save();
            canvas.Translate(x, height - bottomPadding + 10);
            canvas.RotateDegrees(45);
            using var font = new SKFont(textFont, 12);
            canvas.DrawText(label, 0, 0, font, textPaint);
            canvas.Restore();
        }

        // Also label the end point
        if (sortedHistory.Count > 1)
        {
            var x = (float)TimeToX(sortedHistory[^1].Timestamp.Ticks) +
                (float)TimeToX(sortedHistory[^1].Timestamp.Ticks) - (float)TimeToX(sortedHistory[^2].Timestamp.Ticks);
            canvas.DrawLine(x, height - bottomPadding, x, height - bottomPadding + 5, tickPaint);
        }

        // Title
        var titlePaint = new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = true
        };
        var titleFont = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold);
        var titleSkFont = new SKFont(titleFont, 18);
        canvas.DrawText("Time Series Column Chart", width / 2 - 100, 30, titleSkFont, titlePaint);

        // Axis labels
        using var axisFont = new SKFont(textFont, 12);
        canvas.DrawText("Time", width / 2 - 20, height - 10, axisFont, textPaint);

        canvas.Save();
        canvas.Translate(15, height / 2);
        canvas.RotateDegrees(-90);
        canvas.DrawText("Value", 0, 0, axisFont, textPaint);
        canvas.Restore();

        // Save to memory stream
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