namespace BasicTools.Mediator;

/// <summary>
/// Thrown when a command or query is sent but no handler for it is registered in the container.
/// </summary>
/// <remarks>
/// This almost always means a handler was not registered (or its assembly was not scanned),
/// so it derives from <see cref="InvalidOperationException"/>: it is a configuration problem,
/// not something callers are expected to catch and recover from.
/// </remarks>
public sealed class HandlerNotFoundException : InvalidOperationException
{
    /// <summary>Creates the exception for the given request and the handler type that was looked up.</summary>
    /// <param name="requestType">The runtime type of the command or query that was sent.</param>
    /// <param name="handlerType">The handler interface that could not be resolved.</param>
    public HandlerNotFoundException(Type requestType, Type handlerType)
        : base($"No handler registered for '{requestType.FullName}'. Register an implementation of '{handlerType.FullName}'.")
    {
        RequestType = requestType;
        HandlerType = handlerType;
    }

    /// <summary>The runtime type of the command or query that was sent.</summary>
    public Type RequestType { get; }

    /// <summary>The handler interface that could not be resolved from the container.</summary>
    public Type HandlerType { get; }
}
