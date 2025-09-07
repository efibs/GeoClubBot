namespace UseCases.InputPorts.Strikes;

public interface IAddStrikeUseCase
{
    Task<Guid?> AddStrikeAsync(string memberNickname, DateTimeOffset strikeDate);
}