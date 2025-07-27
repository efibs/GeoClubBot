namespace UseCases.InputPorts;

public interface IAddStrikeUseCase
{
    Task<Guid?> AddStrikeAsync(string memberNickname, DateTimeOffset strikeDate);
}