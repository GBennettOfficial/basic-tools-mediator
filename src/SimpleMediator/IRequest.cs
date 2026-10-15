namespace SimpleMediator;

/// <summary>
/// Common base for every message that is handled by exactly one handler and produces a result.
/// </summary>
/// <typeparam name="TResult">The type of value the handler returns.</typeparam>
/// <remarks>
/// Application code should implement <see cref="ICommand"/>, <see cref="ICommand{TResult}"/> or
/// <see cref="IQuery{TResult}"/> rather than this interface directly. It exists so that code which
/// does not care whether a message is a command or a query (the dispatcher, and pipeline
/// behaviors) can work with both through one type.
/// </remarks>
public interface IRequest<TResult>;
