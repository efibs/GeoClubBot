namespace UseCases.OutputPorts.GeoGuessr;

public interface IGeoGuessrClientFactory
{
    IGeoGuessrClient CreateClient(Guid clubId);

    IGeoGuessrClient CreateActivityClient();

    IGeoGuessrClient CreateMissionsClient();
}
