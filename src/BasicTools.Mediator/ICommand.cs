namespace BasicTools.Mediator;

/// <summary>
/// A request to change the state of the system that returns a result,
/// for example the id of a newly created record.
/// </summary>
/// <typeparam name="TResult">The type of value returned once the command has been handled.</typeparam>
/// <remarks>Handled by exactly one <see cref="ICommandHandler{TCommand, TResult}"/>.</remarks>
public interface ICommand<TResult> : IRequest<TResult>;

/// <summary>
/// A request to change the state of the system that returns nothing.
/// </summary>
/// <remarks>
/// Handled by exactly one <see cref="ICommandHandler{TCommand}"/>. It derives from
/// <see cref="ICommand{TResult}"/> with <see cref="Unit"/> so that, internally, every command
/// looks like it returns a value and can flow through the same pipeline.
/// </remarks>
public interface ICommand : ICommand<Unit>;
