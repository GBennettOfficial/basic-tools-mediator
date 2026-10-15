namespace SimpleMediator;

/// <summary>
/// Publishes notifications to every handler registered for them.
/// </summary>
public interface IPublisher
{
    /// <summary>Invokes every handler registered for the notification's runtime type.</summary>
    /// <param name="notification">The notification to publish.</param>
    /// <param name="cancellationToken">Passed through to every handler.</param>
    /// <returns>A task that completes when all handlers have finished.</returns>
    /// <remarks>
    /// Publishing a notification that has no handlers is not an error; it simply does nothing.
    /// How handlers are run (one after another, or concurrently) is decided by the configured
    /// <see cref="INotificationPublisher"/>.
    /// </remarks>
    Task Publish(INotification notification, CancellationToken cancellationToken = default);
}
