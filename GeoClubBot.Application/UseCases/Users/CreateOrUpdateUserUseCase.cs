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
            // Update the member
            return _updateUser(existingUser, user);
        }

        // Create the member
        return _createUser(user);
    }

    private GeoGuessrUser _createUser(GeoGuessrUser user)
    {
        // Build the created event
        var createdEvent = new UserCreatedEvent(user);
        
        // Add the event
        user.AddDomainEvent(createdEvent);
        
        // Create the user
        var createdUser = unitOfWork.GeoGuessrUsers.CreateUser(user);

        return createdUser;
    }

    private GeoGuessrUser _updateUser(GeoGuessrUser oldUser, GeoGuessrUser user)
    {
        // Build the updated event
        var updatedEvent = new UserUpdatedEvent(oldUser, user);
        
        // Add the event
        user.AddDomainEvent(updatedEvent);
        
        // Update the user
        var updatedUser = unitOfWork.GeoGuessrUsers.UpdateUser(user);
        
        return updatedUser;
    }
}