using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts.Repositories;

namespace Infrastructure.OutputAdapters.Repositories;

public class EfAiConversationRepository(GeoClubBotDbContext dbContext) : IAiConversationRepository
{
    public async Task<AiConversationTurn?> ReadByMessageIdAsync(
        ulong discordMessageId,
        CancellationToken cancellationToken = default) =>
        await dbContext.Set<AiConversationTurn>()
            .AsNoTracking()
            .FirstOrDefaultAsync(turn => turn.DiscordMessageId == discordMessageId, cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<AiConversationTurn>> ReadConversationAsync(
        ulong conversationId,
        CancellationToken cancellationToken = default) =>
        await dbContext.Set<AiConversationTurn>()
            .AsNoTracking()
            .Where(turn => turn.ConversationId == conversationId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public void AddTurn(AiConversationTurn turn) => dbContext.Set<AiConversationTurn>().Add(turn);

    public async Task<int> CountUserTurnsSinceAsync(
        ulong authorDiscordUserId,
        DateTimeOffset sinceUtc,
        CancellationToken cancellationToken = default) =>
        await dbContext.Set<AiConversationTurn>()
            .AsNoTracking()
            .CountAsync(
                turn => turn.AuthorDiscordUserId == authorDiscordUserId
                        && turn.Role == AiTurnRole.User
                        && turn.CreatedAtUtc >= sinceUtc,
                cancellationToken)
            .ConfigureAwait(false);

    public async Task<int> DeleteOlderThanAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken = default) =>
        await dbContext.Set<AiConversationTurn>()
            .Where(turn => turn.CreatedAtUtc < cutoffUtc)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
}
