namespace SimpleMediator;

/// <summary>
/// Reacts to a notification of type <typeparamref name="TNotification"/>.
/// </summary>
/// <typeparam name="TNotification">The notification this handler listens for.</typeparam>
/// <remarks>
/// Register as many handlers for the same notification as you need; each one is invoked when the
/// notification is published. Keep each handler focused on one reaction (send an email, update a
/// read model) so they can be added and removed independently.
/// </remarks>
public interface INotificationHandler<in TNotification>
    where TNotification : INotification
{
    /// <summary>Reacts to the notification.</summary>
    /// <param name="notification">The notification that was published.</param>
    /// <param name="cancellationToken">Signals that the publisher no longer needs the work done.</param>
    /// <returns>A task that completes when this handler has finished.</returns>
    Task Handle(TNotification notification, CancellationToken cancellationToken);
}
