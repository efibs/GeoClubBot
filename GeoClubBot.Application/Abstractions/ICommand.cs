using MediatR;

namespace UseCases.Abstractions;

public interface ICommand : IRequest<Unit>;

public interface ICommand<TResponse> : IRequest<TResponse>;
