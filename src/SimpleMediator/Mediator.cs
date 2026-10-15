using SimpleMediator.Internal;

namespace SimpleMediator;

/// <summary>
/// Default implementation of <see cref="ISender"/>. Resolves each request's handler from the
/// dependency injection container and invokes it.
/// </summary>
/// <remarks>
/// The mediator holds no state of its own besides the <see cref="IServiceProvider"/>. Register it
/// with the same lifetime as your handlers (normally scoped) so that handlers resolved through it
/// share the current request's scope, for example the same database context.
/// </remarks>
/// <param name="services">The container that handlers are resolved from.</param>
public sealed class Mediator(IServiceProvider services) : ISender
{
    private readonly IServiceProvider _services = services ?? throw new ArgumentNullException(nameof(services));

    /// <inheritdoc />
    public Task Send(ICommand command, CancellationToken cancellationToken = default) =>
        Dispatch(command, cancellationToken);

    /// <inheritdoc />
    public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default) =>
        Dispatch(command, cancellationToken);

    /// <inheritdoc />
    public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default) =>
        Dispatch(query, cancellationToken);

    // Commands and queries are dispatched the same way; only the handler interface differs, and
    // the wrapper picks that based on the request's runtime type.
    private Task<TResult> Dispatch<TResult>(IRequest<TResult> request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return RequestHandlerWrapper<TResult>
            .For(request.GetType())
            .Handle(request, _services, cancellationToken);
    }
}
