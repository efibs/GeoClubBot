using MediatR;
using UseCases.Abstractions;
using UseCases.OutputPorts.Repositories;

namespace UseCases.Behaviors;

public class UnitOfWorkBehavior<TRequest, TResponse>(IUnitOfWork unitOfWork)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var response = await next(cancellationToken).ConfigureAwait(false);

        if (IsCommand)
        {
            // SaveChangesAsync dispatches domain events that entities collected during the handler.
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    private static readonly bool IsCommand = typeof(ICommand).IsAssignableFrom(typeof(TRequest))
        || typeof(TRequest).GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));
}
