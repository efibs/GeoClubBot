using Entities;

namespace UseCases.OutputPorts;

public interface IExcusesRepository
{
    Task<Dictionary<string, List<GeoGuessrClubMemberExcuse>>> ReadExcusesAsync();
    
    Task WriteExcuseAsync(string memberNickname, GeoGuessrClubMemberExcuse excuse);
    
    Task<bool> DeleteExcuseAsync(Guid excuseId);
}