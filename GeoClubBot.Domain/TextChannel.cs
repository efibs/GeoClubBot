namespace Entities;

public record TextChannel(ulong Id)
{
    public string? Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Category to move the channel into. Null leaves it where it is.</summary>
    public ulong? CategoryId { get; init; }
}
