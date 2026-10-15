namespace SimpleMediator;

/// <summary>
/// Sends commands and queries to their single handler.
/// </summary>
/// <remarks>
/// Inject this interface into the code that issues requests (controllers, endpoints, background
/// jobs). The caller only needs to know about the message type, never about the handler.
/// </remarks>
public interface ISender
{
    /// <summary>Sends a command that returns nothing to its handler.</summary>
    /// <param name="command">The command to send.</param>
    /// <param name="cancellationToken">Passed through to the handler.</param>
    /// <returns>A task that completes when the handler has finished.</returns>
    /// <exception cref="HandlerNotFoundException">No handler is registered for the command's type.</exception>
    Task Send(ICommand command, CancellationToken cancellationToken = default);

    /// <summary>Sends a command to its handler and returns the handler's result.</summary>
    /// <typeparam name="TResult">The command's result type, inferred from the command.</typeparam>
    /// <param name="command">The command to send.</param>
    /// <param name="cancellationToken">Passed through to the handler.</param>
    /// <returns>The value returned by the handler.</returns>
    /// <exception cref="HandlerNotFoundException">No handler is registered for the command's type.</exception>
    Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken cancellationToken = default);

    /// <summary>Sends a query to its handler and returns the data it reads.</summary>
    /// <typeparam name="TResult">The query's result type, inferred from the query.</typeparam>
    /// <param name="query">The query to send.</param>
    /// <param name="cancellationToken">Passed through to the handler.</param>
    /// <returns>The value returned by the handler.</returns>
    /// <exception cref="HandlerNotFoundException">No handler is registered for the query's type.</exception>
    Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken cancellationToken = default);
}
