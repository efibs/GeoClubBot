namespace UseCases.OutputPorts.Projections;

/// <summary>
/// A GeoGuessr user's id and nickname, for pickers that show the nickname but pass the id on:
/// nicknames are not unique and can change, ids are neither.
/// </summary>
public sealed record UserNameProjection(string UserId, string Nickname);
