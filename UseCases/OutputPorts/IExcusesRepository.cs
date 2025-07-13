using Entities;

namespace UseCases.OutputPorts;

public interface IExcusesRepository
{
    Task<List<GeoGuessrClubMemberExcuse>> ReadAllExcusesAsync();
    
    Task<Dictionary<string, List<GeoGuessrClubMemberExcuse>>> ReadExcusesAsync();
    
    Task WriteExcuseAsync(string memberNickname, GeoGuessrClubMemberExcuse excuse);
    
    Task<bool> DeleteExcuseAsync(Guid excuseId);
    
    Task<int> DeleteExcusesAsync(List<Guid> excuseIds);
}