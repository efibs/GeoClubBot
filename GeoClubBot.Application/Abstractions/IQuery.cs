using MediatR;

namespace UseCases.Abstractions;

public interface IQuery<TResponse> : IRequest<TResponse>;
