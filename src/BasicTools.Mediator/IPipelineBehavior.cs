namespace BasicTools.Mediator;

/// <summary>
/// Continues the pipeline: calls the next behavior, or the handler when no behaviors remain.
/// </summary>
/// <typeparam name="TResult">The result type of the request being handled.</typeparam>
/// <param name="cancellationToken">The token to pass on. Usually the one the behavior received.</param>
/// <returns>The result produced by the rest of the pipeline.</returns>
public delegate Task<TResult> RequestHandlerDelegate<TResult>(CancellationToken cancellationToken);

/// <summary>
/// Wraps the handling of commands and queries with cross-cutting logic such as logging,
/// validation, transactions or caching.
/// </summary>
/// <typeparam name="TRequest">The request type the behavior applies to.</typeparam>
/// <typeparam name="TResult">The request's result type.</typeparam>
/// <remarks>
/// <para>
/// Behaviors form a chain around the handler, like ASP.NET Core middleware. Each one receives the
/// request and a <c>next</c> delegate. It can run code before and after calling
/// <c>next</c>, change the result, or skip <c>next</c> entirely (for example to return a cached
/// value or reject an invalid request). The behavior registered first is the outermost.
/// </para>
/// <para>
/// To apply a behavior only to commands or only to queries, register it as an open generic and
/// add a constraint, for example <c>where TRequest : ICommand&lt;TResult&gt;</c>. The container
/// skips behaviors whose constraints a request does not satisfy.
/// </para>
/// </remarks>
public interface IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    /// <summary>Runs this behavior's logic around the rest of the pipeline.</summary>
    /// <param name="request">The command or query being handled.</param>
    /// <param name="next">Invokes the next behavior or the handler. Call it at most once.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>The result to give back to the previous behavior, or to the caller.</returns>
    Task<TResult> Handle(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken);
}
