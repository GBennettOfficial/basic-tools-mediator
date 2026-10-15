namespace SimpleMediator;

/// <summary>
/// Handles a query of type <typeparamref name="TQuery"/> and returns its data.
/// </summary>
/// <typeparam name="TQuery">The query this handler answers.</typeparam>
/// <typeparam name="TResult">The type of data returned to the caller.</typeparam>
public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    /// <summary>Reads and returns the data the query asks for.</summary>
    /// <param name="query">The query to answer.</param>
    /// <param name="cancellationToken">Signals that the caller no longer needs the result.</param>
    /// <returns>The query's result.</returns>
    Task<TResult> Handle(TQuery query, CancellationToken cancellationToken);
}
