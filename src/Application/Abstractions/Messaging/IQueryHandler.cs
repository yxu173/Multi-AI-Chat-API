using MediatR;
using SharedKernel;

namespace Application.Abstractions.Messaging;

public interface IQueryHandler<in TQuery, TResponse> : FastEndpoints.ICommandHandler<TQuery, Result<TResponse>>
    where TQuery : IQuery<TResponse>;