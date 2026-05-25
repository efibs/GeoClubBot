namespace UseCases.OutputPorts.Rendering;

public interface IHistoryRenderer
{
    MemoryStream RenderHistory(List<int> values, List<DateTimeOffset> timestamps, int target);
}
