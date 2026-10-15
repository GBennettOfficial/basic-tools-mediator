namespace BasicTools.Mediator;

/// <summary>
/// Handles a command of type <typeparamref name="TCommand"/> and returns a result.
/// </summary>
/// <typeparam name="TCommand">The command this handler processes.</typeparam>
/// <typeparam name="TResult">The type of value returned to the sender.</typeparam>
public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    /// <summary>Performs the work the command describes.</summary>
    /// <param name="command">The command to handle.</param>
    /// <param name="cancellationToken">Signals that the caller no longer needs the result.</param>
    /// <returns>The result of the command.</returns>
    Task<TResult> Handle(TCommand command, CancellationToken cancellationToken);
}

/// <summary>
/// Handles a command of type <typeparamref name="TCommand"/> that returns nothing.
/// </summary>
/// <typeparam name="TCommand">The command this handler processes.</typeparam>
/// <remarks>
/// This exists so handlers of result-less commands can return a plain <see cref="Task"/>
/// instead of having to write <c>return Unit.Value;</c>. The mediator adapts it internally.
/// </remarks>
public interface ICommandHandler<in TCommand>
    where TCommand : ICommand
{
    /// <summary>Performs the work the command describes.</summary>
    /// <param name="command">The command to handle.</param>
    /// <param name="cancellationToken">Signals that the caller no longer needs the work done.</param>
    /// <returns>A task that completes when the command has been handled.</returns>
    Task Handle(TCommand command, CancellationToken cancellationToken);
}
