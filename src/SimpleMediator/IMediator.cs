namespace SimpleMediator;

/// <summary>
/// Combines <see cref="ISender"/> and <see cref="IPublisher"/> for code that needs both.
/// </summary>
/// <remarks>
/// Prefer injecting the narrower interface when a class only sends requests or only publishes
/// notifications; it documents what the class actually does and makes it easier to test.
/// </remarks>
public interface IMediator : ISender, IPublisher;
