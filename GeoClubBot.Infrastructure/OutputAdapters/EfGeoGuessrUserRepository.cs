using Entities;
using Infrastructure.OutputAdapters.DataAccess;
using Microsoft.EntityFrameworkCore;
using UseCases.OutputPorts;

namespace Infrastructure.OutputAdapters;

public class EfGeoGuessrUserRepository(GeoClubBotDbContext dbContext) : IGeoGuessrUserRepository
{
    public async Task<GeoGuessrUser> CreateOrUpdateUserAsync(GeoGuessrUser user)
    {
        // Try to find an existing user with that id
        var userExists = await dbContext.GeoGuessrUsers.AnyAsync(u => u.UserId == user.UserId);

        // If the club member already exists
        if (userExists)
        {
            // Update the club member
            dbContext.Update(user);
        }
        else
        {
            // Add the club member
            dbContext.Add(user);
        }

        // Save the changes to the database
        await dbContext.SaveChangesAsync();

        return user;
    }

    public async Task<GeoGuessrUser?> ReadUserByUserIdAsync(string userId)
    {
        // Try to find the user
        var clubMember = await dbContext.GeoGuessrUsers
            .FindAsync(userId);
        
        return clubMember;
    }

    public async Task<List<GeoGuessrUser>> ReadAllLinkedUsersAsync()
    {
        // Get the club members that have a discord user id set
        var linkedUsers = await dbContext.GeoGuessrUsers
            .Where(u => u.DiscordUserId.HasValue)
            .ToListAsync();
        
        return linkedUsers;
    }
}