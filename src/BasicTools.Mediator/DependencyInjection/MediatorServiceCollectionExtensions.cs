using System.Reflection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BasicTools.Mediator;

// Extension methods for IServiceCollection conventionally live in this namespace, so they show up
// in IntelliSense wherever services are configured without needing an extra using directive.
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Registers BasicTools.Mediator and the handlers found by assembly scanning.
/// </summary>
public static class MediatorServiceCollectionExtensions
{
    // The handler interfaces scanning looks for. Command and query handlers must be unique per
    // request; notification handlers may have many implementations.
    private static readonly Type[] SingleHandlerInterfaces =
    [
        typeof(ICommandHandler<>),
        typeof(ICommandHandler<,>),
        typeof(IQueryHandler<,>),
    ];

    private static readonly Type NotificationHandlerInterface = typeof(INotificationHandler<>);

    /// <summary>
    /// Registers the mediator and every command, query and notification handler in <paramref name="assemblies"/>.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="assemblies">The assemblies to scan for handlers.</param>
    /// <returns>The same service collection, so calls can be chained.</returns>
    public static IServiceCollection AddMediator(this IServiceCollection services, params Assembly[] assemblies) =>
        services.AddMediator(options => options.RegisterServicesFromAssemblies(assemblies));

    /// <summary>
    /// Registers the mediator, the handlers found in the configured assemblies, and the configured
    /// pipeline behaviors and notification publisher.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="configure">Sets the assemblies to scan, behaviors, lifetime and publisher.</param>
    /// <returns>The same service collection, so calls can be chained.</returns>
    /// <exception cref="ArgumentException">No assemblies were configured.</exception>
    /// <exception cref="InvalidOperationException">
    /// Two different classes handle the same command or query.
    /// </exception>
    /// <remarks>
    /// Safe to call more than once: every registration uses the <c>TryAdd</c> family, so nothing
    /// is registered twice and handlers you registered yourself beforehand are kept.
    /// </remarks>
    public static IServiceCollection AddMediator(this IServiceCollection services, Action<MediatorOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new MediatorOptions();
        configure(options);

        if (options.Assemblies.Count == 0)
        {
            throw new ArgumentException(
                "No assemblies to scan. Call RegisterServicesFromAssemblies or RegisterServicesFromAssemblyContaining<T>.",
                nameof(configure));
        }

        RegisterMediator(services, options);
        RegisterHandlers(services, options.Assemblies.SelectMany(GetLoadableTypes), options.Lifetime);

        // TryAddEnumerable allows many registrations of the same interface but skips an exact
        // duplicate (same service and implementation), and it preserves order, which is the
        // order the behaviors run in.
        foreach (var (service, implementation) in options.Behaviors)
        {
            services.TryAddEnumerable(new ServiceDescriptor(service, implementation, options.Lifetime));
        }

        return services;
    }

    private static void RegisterMediator(IServiceCollection services, MediatorOptions options)
    {
        services.TryAddSingleton(options.NotificationPublisher);

        // Register the concrete type once, then forward each interface to it, so that within a
        // scope ISender, IPublisher and IMediator resolve to the same instance.
        services.TryAdd(new ServiceDescriptor(typeof(Mediator), typeof(Mediator), options.Lifetime));
        services.TryAdd(new ServiceDescriptor(typeof(IMediator), sp => sp.GetRequiredService<Mediator>(), options.Lifetime));
        services.TryAdd(new ServiceDescriptor(typeof(ISender), sp => sp.GetRequiredService<Mediator>(), options.Lifetime));
        services.TryAdd(new ServiceDescriptor(typeof(IPublisher), sp => sp.GetRequiredService<Mediator>(), options.Lifetime));
    }

    /// <summary>Registers every handler implemented by <paramref name="types"/>.</summary>
    /// <remarks>
    /// Takes plain types rather than assemblies so tests can exercise it with a hand-picked set,
    /// for example two handlers for the same command, without putting them in a real assembly.
    /// </remarks>
    internal static void RegisterHandlers(IServiceCollection services, IEnumerable<Type> types, ServiceLifetime lifetime)
    {
        // Remember which class was found for each command or query handler interface, so a
        // second class for the same request is reported at startup instead of silently winning.
        var singleHandlers = new Dictionary<Type, Type>();

        foreach (var type in types.Where(IsConcreteClass).Distinct())
        {
            foreach (var handlerInterface in type.GetInterfaces().Where(i => i.IsGenericType))
            {
                var definition = handlerInterface.GetGenericTypeDefinition();

                if (definition == NotificationHandlerInterface)
                {
                    services.TryAddEnumerable(new ServiceDescriptor(handlerInterface, type, lifetime));
                }
                else if (SingleHandlerInterfaces.Contains(definition))
                {
                    if (singleHandlers.TryGetValue(handlerInterface, out var existing))
                    {
                        throw new InvalidOperationException(
                            $"Both '{existing.FullName}' and '{type.FullName}' implement '{handlerInterface}'. " +
                            "A command or query must have exactly one handler.");
                    }

                    singleHandlers.Add(handlerInterface, type);
                    services.TryAdd(new ServiceDescriptor(handlerInterface, type, lifetime));
                }
            }
        }
    }

    // Open generic handlers (class Handler<T> : ICommandHandler<T>) are skipped: the container
    // cannot construct them without knowing T, and they are rare for commands and queries.
    private static bool IsConcreteClass(Type type) =>
        type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false };

    // An assembly can reference types whose dependencies are missing at runtime. GetTypes then
    // throws, but the exception still carries every type that did load, so scan those.
    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }
}
