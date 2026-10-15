namespace SimpleMediator;

/// <summary>
/// Represents "no value". Used as the result type of commands that do not return anything,
/// so that every request can be treated as producing a <c>TResult</c>.
/// </summary>
/// <remarks>
/// C# has no way to use <see langword="void"/> as a generic argument, so <c>Task&lt;void&gt;</c>
/// is impossible. <see cref="Unit"/> fills that gap: it has exactly one value, carries no data,
/// and lets the dispatcher and pipeline behaviors use a single generic shape for every request.
/// </remarks>
public readonly record struct Unit
{
    /// <summary>The single value of <see cref="Unit"/>.</summary>
    public static readonly Unit Value = default;

    /// <summary>A completed task holding <see cref="Value"/>, cached to avoid allocating a new one each time.</summary>
    public static readonly Task<Unit> Task = System.Threading.Tasks.Task.FromResult(Value);

    /// <inheritdoc />
    public override string ToString() => "()";
}
