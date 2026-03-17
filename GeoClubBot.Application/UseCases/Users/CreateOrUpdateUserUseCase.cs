using Entities;
using UseCases.InputPorts.Users;
using UseCases.OutputPorts;

namespace UseCases.UseCases.Users;

public class CreateOrUpdateUserUseCase(IUnitOfWork unitOfWork) : ICreateOrUpdateUserUseCase
{
    public async Task<GeoGuessrUser> CreateOrUpdateUserAsync(GeoGuessrUser user)
    {
        // Try to read the user
        var existingUser = await unitOfWork.GeoGuessrUsers
            .ReadUserByUserIdAsync(user.UserId)
            .ConfigureAwait(false);
        
        // If there is a user
        if (existingUser != null)
        {
            // Update the user
            return await _updateUserAsync(existingUser, user).ConfigureAwait(false);
        }

        // Create the user
        return _createUser(user);
    }

    private GeoGuessrUser _createUser(GeoGuessrUser user)
    {
        // Create the user
        var createdUser = unitOfWork.GeoGuessrUsers.CreateUser(user);

        // Build the created event
        var createdEvent = new UserCreatedEvent(createdUser);

        // Add the event
        createdUser.AddDomainEvent(createdEvent);
        
        return createdUser;
    }

    private async Task<GeoGuessrUser> _updateUserAsync(GeoGuessrUser oldUser, GeoGuessrUser newUser)
    {
        // If the old and new users are the same
        if (oldUser == newUser)
        {
            return oldUser;
        }

        // Update the user (copies properties onto the tracked entity)
        var trackedUser = await unitOfWork.GeoGuessrUsers.UpdateUserAsync(newUser).ConfigureAwait(false);

        // Build the updated event
        var updatedEvent = new UserUpdatedEvent(oldUser, trackedUser!);
        
        // Add the event to the tracked entity
        trackedUser!.AddDomainEvent(updatedEvent);

        return trackedUser;
    }
}