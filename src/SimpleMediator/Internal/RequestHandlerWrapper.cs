using System.Collections.Concurrent;

namespace SimpleMediator.Internal;

/// <summary>
/// Bridges from a request whose concrete type is only known at runtime to a strongly typed handler.
/// </summary>
/// <typeparam name="TResult">The result type of the request being dispatched.</typeparam>
/// <remarks>
/// <para>
/// The mediator receives an <c>IRequest&lt;TResult&gt;</c>, but the handler it must resolve is
/// keyed by the request's concrete type, for example <c>IQueryHandler&lt;GetUser, UserDto&gt;</c>.
/// That type can only be built with reflection. Doing reflection on every call is slow, so for
/// each request type we build one small generic wrapper (a subclass of this type closed over the
/// concrete request type) the first time it is sent, and cache it. After that, dispatching is a
/// dictionary lookup plus ordinary virtual calls, with no reflection.
/// </para>
/// <para>
/// The cache is a static field on this generic class, so each <typeparamref name="TResult"/> gets
/// its own dictionary. That keeps every entry correctly typed without casting from <c>object</c>.
/// </para>
/// </remarks>
internal abstract class RequestHandlerWrapper<TResult>
{
    private static readonly ConcurrentDictionary<Type, RequestHandlerWrapper<TResult>> Cache = new();

    /// <summary>Resolves the handler from <paramref name="services"/> and invokes it.</summary>
    public abstract Task<TResult> Handle(IRequest<TResult> request, IServiceProvider services, CancellationToken cancellationToken);

    /// <summary>Returns the cached wrapper for <paramref name="requestType"/>, creating it on first use.</summary>
    public static RequestHandlerWrapper<TResult> For(Type requestType) => Cache.GetOrAdd(requestType, Create);

    private static RequestHandlerWrapper<TResult> Create(Type requestType)
    {
        var isCommand = typeof(ICommand<TResult>).IsAssignableFrom(requestType);
        var isQuery = typeof(IQuery<TResult>).IsAssignableFrom(requestType);

        // A message is either a command or a query, never both. Allowing both would make it
        // ambiguous which handler should run, so reject it with a clear message.
        if (isCommand && isQuery)
        {
            throw new InvalidOperationException(
                $"'{requestType.FullName}' implements both a command and a query interface. A request must be one or the other.");
        }

        // Check the result-less command first: ICommand derives from ICommand<Unit>, so it would
        // otherwise also match the isCommand branch and look for the wrong handler interface.
        Type wrapperType;
        if (typeof(ICommand).IsAssignableFrom(requestType))
        {
            wrapperType = typeof(CommandHandlerWrapper<>).MakeGenericType(requestType);
        }
        else if (isCommand)
        {
            wrapperType = typeof(CommandHandlerWrapper<,>).MakeGenericType(requestType, typeof(TResult));
        }
        else if (isQuery)
        {
            wrapperType = typeof(QueryHandlerWrapper<,>).MakeGenericType(requestType, typeof(TResult));
        }
        else
        {
            throw new InvalidOperationException(
                $"'{requestType.FullName}' must implement ICommand, ICommand<TResult> or IQuery<TResult>.");
        }

        return (RequestHandlerWrapper<TResult>)Activator.CreateInstance(wrapperType)!;
    }

    /// <summary>Resolves <typeparamref name="THandler"/> or throws <see cref="HandlerNotFoundException"/>.</summary>
    protected static THandler GetHandler<THandler>(IServiceProvider services, Type requestType)
        where THandler : class =>
        services.GetService(typeof(THandler)) as THandler
        ?? throw new HandlerNotFoundException(requestType, typeof(THandler));
}

/// <summary>Dispatches a command that returns <typeparamref name="TResult"/>.</summary>
internal sealed class CommandHandlerWrapper<TCommand, TResult> : RequestHandlerWrapper<TResult>
    where TCommand : ICommand<TResult>
{
    public override Task<TResult> Handle(IRequest<TResult> request, IServiceProvider services, CancellationToken cancellationToken)
    {
        var handler = GetHandler<ICommandHandler<TCommand, TResult>>(services, typeof(TCommand));
        return handler.Handle((TCommand)request, cancellationToken);
    }
}

/// <summary>Dispatches a command that returns nothing, adapting its <see cref="Task"/> to <c>Task&lt;Unit&gt;</c>.</summary>
internal sealed class CommandHandlerWrapper<TCommand> : RequestHandlerWrapper<Unit>
    where TCommand : ICommand
{
    public override async Task<Unit> Handle(IRequest<Unit> request, IServiceProvider services, CancellationToken cancellationToken)
    {
        var handler = GetHandler<ICommandHandler<TCommand>>(services, typeof(TCommand));
        await handler.Handle((TCommand)request, cancellationToken).ConfigureAwait(false);
        return Unit.Value;
    }
}

/// <summary>Dispatches a query that returns <typeparamref name="TResult"/>.</summary>
internal sealed class QueryHandlerWrapper<TQuery, TResult> : RequestHandlerWrapper<TResult>
    where TQuery : IQuery<TResult>
{
    public override Task<TResult> Handle(IRequest<TResult> request, IServiceProvider services, CancellationToken cancellationToken)
    {
        var handler = GetHandler<IQueryHandler<TQuery, TResult>>(services, typeof(TQuery));
        return handler.Handle((TQuery)request, cancellationToken);
    }
}
