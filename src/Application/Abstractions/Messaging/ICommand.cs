﻿using MediatR;
using SharedKernel;

namespace Application.Abstractions.Messaging;

public interface ICommand : FastEndpoints.ICommand<Result>, IBaseCommand;

public interface ICommand<TResponse> : FastEndpoints.ICommand<Result<TResponse>>, IBaseCommand;

public interface IBaseCommand;