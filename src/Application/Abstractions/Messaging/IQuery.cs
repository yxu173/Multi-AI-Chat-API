using MediatR;
using SharedKernel;

namespace Application.Abstractions.Messaging;

public interface IQuery<TResponse> : FastEndpoints.ICommand<Result<TResponse>>;
