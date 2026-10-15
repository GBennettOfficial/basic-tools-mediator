using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace BasicTools.Mediator;

/// <summary>
/// Configures what <c>AddMediator</c> registers in the service collection.
/// </summary>
/// <example>
/// <code>
/// services.AddMediator(options =>
/// {
///     options.RegisterServicesFromAssemblyContaining&lt;Program&gt;();
///     options.AddOpenBehavior(typeof(LoggingBehavior&lt;,&gt;));
///     options.AddOpenBehavior(typeof(ValidationBehavior&lt;,&gt;));
/// });
/// </code>
/// </example>
public sealed class MediatorOptions
{
    private readonly List<Assembly> _assemblies = [];
    private readonly List<(Type Service, Type Implementation)> _behaviors = [];

    /// <summary>
    /// The lifetime used for the mediator, scanned handlers and behaviors. Defaults to
    /// <see cref="ServiceLifetime.Transient"/>.
    /// </summary>
    /// <remarks>
    /// Transient is the safest default: a transient handler can depend on scoped services such as
    /// a <c>DbContext</c>, whereas a singleton handler that does so is rejected by the container.
    /// </remarks>
    public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Transient;

    /// <summary>
    /// The strategy used to run notification handlers. Defaults to
    /// <see cref="SequentialNotificationPublisher"/>. Registered as a singleton, so it must be
    /// stateless or thread-safe.
    /// </summary>
    public INotificationPublisher NotificationPublisher { get; set; } = new SequentialNotificationPublisher();

    /// <summary>The assemblies that will be scanned for handlers, without duplicates.</summary>
    public IReadOnlyList<Assembly> Assemblies => _assemblies;

    // Stored as types rather than ServiceDescriptors so that Lifetime is read when services are
    // registered, not when AddBehavior is called. Setting Lifetime after adding behaviors still works.
    internal IReadOnlyList<(Type Service, Type Implementation)> Behaviors => _behaviors;

    /// <summary>Scans the given assemblies for command, query and notification handlers.</summary>
    /// <param name="assemblies">The assemblies to scan.</param>
    /// <returns>These options, so calls can be chained.</returns>
    public MediatorOptions RegisterServicesFromAssemblies(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        foreach (var assembly in assemblies)
        {
            ArgumentNullException.ThrowIfNull(assembly, nameof(assemblies));
            if (!_assemblies.Contains(assembly))
            {
                _assemblies.Add(assembly);
            }
        }

        return this;
    }

    /// <summary>Scans the assembly that defines <typeparamref name="T"/> for handlers.</summary>
    /// <typeparam name="T">Any type in the assembly to scan, commonly <c>Program</c>.</typeparam>
    /// <returns>These options, so calls can be chained.</returns>
    public MediatorOptions RegisterServicesFromAssemblyContaining<T>() =>
        RegisterServicesFromAssemblies(typeof(T).Assembly);

    /// <summary>
    /// Adds an open generic behavior, such as <c>typeof(LoggingBehavior&lt;,&gt;)</c>, that applies to
    /// every request satisfying its generic constraints.
    /// </summary>
    /// <param name="openBehaviorType">
    /// A generic type definition with two type parameters that implements
    /// <see cref="IPipelineBehavior{TRequest, TResult}"/>.
    /// </param>
    /// <returns>These options, so calls can be chained.</returns>
    /// <exception cref="ArgumentException">The type is not an open generic pipeline behavior.</exception>
    public MediatorOptions AddOpenBehavior(Type openBehaviorType)
    {
        ArgumentNullException.ThrowIfNull(openBehaviorType);

        var implementsBehavior = openBehaviorType.GetInterfaces()
            .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IPipelineBehavior<,>));

        if (!openBehaviorType.IsGenericTypeDefinition || openBehaviorType.GetGenericArguments().Length != 2 || !implementsBehavior)
        {
            throw new ArgumentException(
                $"'{openBehaviorType.FullName}' must be an open generic type with two type parameters that implements IPipelineBehavior<,>.",
                nameof(openBehaviorType));
        }

        _behaviors.Add((typeof(IPipelineBehavior<,>), openBehaviorType));
        return this;
    }

    /// <summary>
    /// Adds a behavior for one specific request type, for example
    /// <c>AddBehavior&lt;IPipelineBehavior&lt;GetOrder, OrderDto&gt;, CacheOrderBehavior&gt;()</c>.
    /// </summary>
    /// <typeparam name="TService">The closed <see cref="IPipelineBehavior{TRequest, TResult}"/> interface.</typeparam>
    /// <typeparam name="TImplementation">The behavior class.</typeparam>
    /// <returns>These options, so calls can be chained.</returns>
    public MediatorOptions AddBehavior<TService, TImplementation>()
        where TService : class
        where TImplementation : class, TService
    {
        var service = typeof(TService);
        if (!service.IsGenericType || service.GetGenericTypeDefinition() != typeof(IPipelineBehavior<,>))
        {
            throw new ArgumentException($"'{service.FullName}' must be a closed IPipelineBehavior<TRequest, TResult>.", nameof(TService));
        }

        _behaviors.Add((service, typeof(TImplementation)));
        return this;
    }
}
