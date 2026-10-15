namespace SimpleMediator;

/// <summary>
/// Marks a class as a notification: an event announcing that something has already happened,
/// such as <c>UserRegistered</c>.
/// </summary>
/// <remarks>
/// Unlike commands and queries, a notification can have any number of handlers, including none,
/// and produces no result. The publisher does not know or care who is listening.
/// </remarks>
public interface INotification;
