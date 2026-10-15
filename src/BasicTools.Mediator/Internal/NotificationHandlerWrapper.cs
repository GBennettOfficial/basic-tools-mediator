using System.Collections.Concurrent;

namespace BasicTools.Mediator.Internal;

/// <summary>
/// Resolves all handlers for a notification whose concrete type is only known at runtime.
/// </summary>
/// <remarks>
/// Uses the same cached-wrapper technique as <see cref="RequestHandlerWrapper{TResult}"/>: the
/// generic subclass for each notification type is built with reflection once, then reused.
/// </remarks>
internal abstract class NotificationHandlerWrapper
{
    private static readonly ConcurrentDictionary<Type, NotificationHandlerWrapper> _cache = new();

    /// <summary>Resolves every handler for the notification and hands them to <paramref name="publisher"/>.</summary>
    public abstract Task Handle(INotification notification, IServiceProvider services, INotificationPublisher publisher, CancellationToken cancellationToken);

    /// <summary>Returns the cached wrapper for <paramref name="notificationType"/>, creating it on first use.</summary>
    public static NotificationHandlerWrapper For(Type notificationType) =>
        _cache.GetOrAdd(notificationType, static type =>
            (NotificationHandlerWrapper)Activator.CreateInstance(typeof(NotificationHandlerWrapper<>).MakeGenericType(type))!);
}

/// <summary>Resolves every <see cref="INotificationHandler{TNotification}"/> for <typeparamref name="TNotification"/>.</summary>
internal sealed class NotificationHandlerWrapper<TNotification> : NotificationHandlerWrapper
    where TNotification : INotification
{
    public override Task Handle(INotification notification, IServiceProvider services, INotificationPublisher publisher, CancellationToken cancellationToken)
    {
        // Microsoft's container returns an empty sequence when nothing is registered; other
        // containers may return null, which is treated the same way.
        var handlers = services.GetService(typeof(IEnumerable<INotificationHandler<TNotification>>))
            as IEnumerable<INotificationHandler<TNotification>>;

        var executors = handlers?
            .Select(handler => new NotificationHandlerExecutor(
                handler,
                (n, token) => handler.Handle((TNotification)n, token)))
            .ToArray() ?? [];

        return executors.Length == 0
            ? Task.CompletedTask
            : publisher.Publish(executors, notification, cancellationToken);
    }
}
