namespace BasicTools.Mediator;

/// <summary>
/// A request to read data without changing the state of the system.
/// </summary>
/// <typeparam name="TResult">The type of data the query returns.</typeparam>
/// <remarks>
/// Handled by exactly one <see cref="IQueryHandler{TQuery, TResult}"/>. Queries always return
/// something, so there is no result-less variant as there is for <see cref="ICommand"/>.
/// </remarks>
public interface IQuery<TResult> : IRequest<TResult>;
