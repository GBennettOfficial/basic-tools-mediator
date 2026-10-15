namespace BasicTools.Mediator;

/// <summary>
/// Strategy that decides how the handlers of a published notification are run.
/// </summary>
/// <remarks>
/// Two strategies are included: <see cref="SequentialNotificationPublisher"/> (the default) and
/// <see cref="ParallelNotificationPublisher"/>. Implement this interface to add your own, for
/// example one that logs and swallows handler failures.
/// </remarks>
public interface INotificationPublisher
{
    /// <summary>Runs the given handlers for <paramref name="notification"/>.</summary>
    /// <param name="handlers">One entry per registered handler, in registration order.</param>
    /// <param name="notification">The notification being published.</param>
    /// <param name="cancellationToken">The token passed to <see cref="IPublisher.Publish"/>.</param>
    /// <returns>A task that completes when the strategy considers publishing finished.</returns>
    Task Publish(IReadOnlyList<NotificationHandlerExecutor> handlers, INotification notification, CancellationToken cancellationToken);
}

/// <summary>
/// One handler of a notification, packaged so an <see cref="INotificationPublisher"/> can run it
/// without knowing the notification's generic type.
/// </summary>
/// <param name="HandlerInstance">The resolved handler object, useful for logging or filtering.</param>
/// <param name="HandlerCallback">Invokes the handler with the notification and a cancellation token.</param>
public sealed record NotificationHandlerExecutor(
    object HandlerInstance,
    Func<INotification, CancellationToken, Task> HandlerCallback);

/// <summary>
/// Runs handlers one at a time, in registration order. If a handler throws, the exception
/// propagates and the remaining handlers are not run.
/// </summary>
/// <remarks>
/// This is the default because it is the most predictable: handlers never run concurrently, so
/// they can safely share scoped services such as an Entity Framework <c>DbContext</c>.
/// </remarks>
public sealed class SequentialNotificationPublisher : INotificationPublisher
{
    /// <inheritdoc />
    public async Task Publish(IReadOnlyList<NotificationHandlerExecutor> handlers, INotification notification, CancellationToken cancellationToken)
    {
        foreach (var handler in handlers)
        {
            await handler.HandlerCallback(notification, cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Starts every handler at once and waits for all of them to finish.
/// </summary>
/// <remarks>
/// <para>
/// Every handler runs even if another one fails. If exactly one handler fails its exception is
/// rethrown as is; if several fail, an <see cref="AggregateException"/> containing all of them
/// is thrown so no failure is lost.
/// </para>
/// <para>
/// Handlers run concurrently, so they must not share services that are not thread-safe, such as
/// a scoped <c>DbContext</c>.
/// </para>
/// </remarks>
public sealed class ParallelNotificationPublisher : INotificationPublisher
{
    /// <inheritdoc />
    public async Task Publish(IReadOnlyList<NotificationHandlerExecutor> handlers, INotification notification, CancellationToken cancellationToken)
    {
        var tasks = new Task[handlers.Count];
        for (var i = 0; i < handlers.Count; i++)
        {
            tasks[i] = Start(handlers[i], notification, cancellationToken);
        }

        var all = Task.WhenAll(tasks);
        try
        {
            await all.ConfigureAwait(false);
        }
        // `await` only rethrows the first exception of a WhenAll; surface them all when there are several.
        catch when (all.Exception is { InnerExceptions.Count: > 1 })
        {
            throw all.Exception;
        }
    }

    // A handler that is not `async` can throw before returning a task. Capture that as a faulted
    // task so it cannot stop the remaining handlers from starting.
    private static Task Start(NotificationHandlerExecutor handler, INotification notification, CancellationToken cancellationToken)
    {
        try
        {
            return handler.HandlerCallback(notification, cancellationToken);
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
    }
}
