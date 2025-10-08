using Entities;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.ImageSharp;
using OxyPlot.Series;
using UseCases.InputPorts.ClubMemberActivity;

namespace UseCases.UseCases.ClubMemberActivity;

public class RenderHistoryUseCase : IRenderHistoryUseCase
{
    public MemoryStream RenderHistory(List<HistoryEntry> history)
    {
        if (history.Count < 2)
        {
            throw new ArgumentException("Need at least 2 data points to determine time ranges.");
        }
        
        // Create a new plot model
        var model = new PlotModel
        {
            Title = "Activity History",
            DefaultFont = "DejaVu Sans",
            Background = OxyColors.White,
        };
        
        // Create the date axis
        var dateAxis = new DateTimeAxis
        {
            Key = "TimeAxis",
            Title = "Time",
            IsZoomEnabled = false,
            IsPanEnabled = false,
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.None,
            IntervalType = DateTimeIntervalType.Days,
            Angle = 90,
            MajorStep = 1,
            Minimum = DateTimeAxis.ToDouble(history[0].Timestamp.UtcDateTime),
            Maximum = DateTimeAxis.ToDouble(history[^1].Timestamp.UtcDateTime),
        };
        
        // Add X axis: time
        model.Axes.Add(dateAxis);
        
        // Add Y axis: Xp
        model.Axes.Add(new LinearAxis
        {
            Key = "ValueAxis",
            Position = AxisPosition.Left,
            Title = "Xp",
            MinimumPadding = 0,
            AbsoluteMinimum = 0,
        });
        
        // Use RectangleBarSeries so each bar spans a time range
        var bars = new BarSeries
        {
            FillColor = OxyColors.IndianRed,
            StrokeColor = OxyColors.Black,
            StrokeThickness = 1,
            XAxisKey = "ValueAxis",
            YAxisKey = "TimeAxis",
        };
        
        // For every data element
        for (var i = 1; i < history.Count; i++)
        {
            // Get the previous and the current entry
            var previousEntry = history[i - 1];
            var currentEntry = history[i];
            
            // Convert to doubles for rectangles
            var start = DateTimeAxis.ToDouble(previousEntry.Timestamp.UtcDateTime);
            var end = DateTimeAxis.ToDouble(currentEntry.Timestamp.UtcDateTime);
            var mid = (start + end) / 2;
            var width = end - start;
            
            // Add the rectangle to the items
            bars.Items.Add(new BarItem
            {
                Value = currentEntry.Xp,
                CategoryIndex = 0,
            });
        }

        // Add the bar to the model
        model.Series.Add(bars);
        
        // Render the image to a memory stream
        var exporter = new PngExporter(800, 400);
        var ms = new MemoryStream();
        exporter.Export(model, ms);
        return ms;
    }
}