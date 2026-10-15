using BasicTools.Mediator.Internal;

namespace BasicTools.Mediator;

/// <summary>
/// Default implementation of <see cref="IMediator"/>. Resolves handlers and pipeline behaviors
/// from the dependency injection container and invokes them.
/// </summary>
/// <remarks>
/// The mediator holds no state of its own besides its constructor arguments. Register it with the
/// same lifetime as your handlers (normally scoped) so that handlers resolved through it share the
/// current request's scope, for example the same database context.
/// </remarks>
/// <param name="services">The container that handlers and behaviors are resolved from.</param>
/// <param name="notificationPublisher">
/// How notification handlers are run. Defaults to <see cref="SequentialNotificationPublisher"/>.
/// </param>
public sealed class Mediator(IServiceProvider services, INotificationPublisher? notificationPublisher = null) : IMediator
{
    private readonly IServiceProvider _services = services ?? throw new ArgumentNullException(nameof(services));
    private readonly INotificationPublisher _notificationPublisher = notificationPublisher ?? new SequentialNotificationPublisher();

    /// <inheritdoc />
    public Task Send(ICommand command, CancellationToken cancellationToken = default) =>
        Dispatch(command, cancellationToken);

    /// <inheritdoc />
    public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default) =>
        Dispatch(command, cancellationToken);

    /// <inheritdoc />
    public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default) =>
        Dispatch(query, cancellationToken);

    /// <inheritdoc />
    public Task Publish(INotification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        // Use the runtime type, so a notification passed around as INotification still reaches
        // the handlers registered for its concrete type.
        return NotificationHandlerWrapper
            .For(notification.GetType())
            .Handle(notification, _services, _notificationPublisher, cancellationToken);
    }

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
