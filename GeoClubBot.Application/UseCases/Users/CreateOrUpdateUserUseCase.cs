using Entities;
using MediatR;
using UseCases.InputPorts.Users;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Users;

public class CreateOrUpdateUserUseCase(IPublisher publisher, IGeoGuessrUserRepository repository) : ICreateOrUpdateUserUseCase
{
    public async Task<GeoGuessrUser> CreateOrUpdateUserAsync(GeoGuessrUser user)
    {
        // Try to read the user
        var existingUser = await repository.ReadUserByUserIdAsync(user.UserId).ConfigureAwait(false);
        
        // If there is a user
        if (existingUser != null)
        {
            // Update the member
            return await _updateUserAsync(existingUser, user).ConfigureAwait(false);
        }

        // Create the member
        return await _createUserAsync(user).ConfigureAwait(false);
    }

    private async Task<GeoGuessrUser> _createUserAsync(GeoGuessrUser user)
    {
        // Create the user
        var createdUser = await repository.CreateUserAsync(user).ConfigureAwait(false);
        
        // Build the created event
        var createdEvent = new UserCreatedEvent(createdUser);
        
        // Publish the event
        await publisher.Publish(createdEvent).ConfigureAwait(false);

        return createdUser;
    }

    private async Task<GeoGuessrUser> _updateUserAsync(GeoGuessrUser oldUser, GeoGuessrUser user)
    {
        // Update the user
        var updatedUser = await repository.UpdateUserAsync(user).ConfigureAwait(false);
        
        // Build the updated event
        var updatedEvent = new UserUpdatedEvent(oldUser, updatedUser);
        
        // Publish the event
        await publisher.Publish(updatedEvent).ConfigureAwait(false);
        
        return updatedUser;
    }
}