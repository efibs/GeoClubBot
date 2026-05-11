namespace UseCases.OutputPorts.GeoGuessr;

public interface IGeoGuessrUserProfileReader
{
    Task<UserDto?> ReadUserProfileAsync(string userId);
}
