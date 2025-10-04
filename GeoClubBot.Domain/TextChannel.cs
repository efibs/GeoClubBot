namespace Entities;

public record TextChannel(ulong Id)
{
    public string? Name { get; init; }

    public string? Description { get; init; }
}