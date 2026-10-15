using Microsoft.Extensions.DependencyInjection;

namespace BasicTools.Mediator.Tests;

public class DependencyInjectionTests
{
    // Scanning the test assembly picks up the handlers of every test class, some of which need a
    // List<string> for logging, so the helper always registers one.
    private static ServiceProvider Build(Action<MediatorOptions> configure, Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<List<string>>();
        extra?.Invoke(services);
        services.AddMediator(configure);

        // ValidateOnBuild checks every registration can be constructed; ValidateScopes catches a
        // singleton depending on a scoped service. Both catch registration mistakes early.
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private static void ScanTestAssembly(MediatorOptions options) =>
        options.RegisterServicesFromAssemblyContaining<DependencyInjectionTests>();

    [Fact]
    public async Task Scanned_handlers_can_be_used_through_ISender_and_IPublisher()
    {
        using var provider = Build(ScanTestAssembly);
        var log = provider.GetRequiredService<List<string>>();

        var sender = provider.GetRequiredService<ISender>();
        var publisher = provider.GetRequiredService<IPublisher>();

        Assert.Equal("pong", await sender.Query(new Ping()));
        await sender.Send(new RecordVisit("home"));
        await publisher.Publish(new Greeted());

        Assert.Equal(["visit:home", "greeted:first", "greeted:second"], log);
    }

    [Fact]
    public void Assembly_overload_registers_the_same_services()
    {
        var services = new ServiceCollection();

        services.AddMediator(typeof(DependencyInjectionTests).Assembly);

        Assert.Contains(services, d => d.ServiceType == typeof(IMediator));
        Assert.Contains(services, d => d.ServiceType == typeof(IQueryHandler<Ping, string>) && d.ImplementationType == typeof(PingHandler));
    }

    [Fact]
    public async Task Configured_behaviors_run_in_the_order_they_were_added()
    {
        using var provider = Build(options =>
        {
            ScanTestAssembly(options);
            options.AddOpenBehavior(typeof(TagBehavior<,>));
            options.AddBehavior<IPipelineBehavior<Ping, string>, ExclaimBehavior>();
        });

        var result = await provider.GetRequiredService<ISender>().Query(new Ping());

        // ExclaimBehavior is inner, so it changes the handler's result first; TagBehavior then wraps that.
        Assert.Equal("[pong!]", result);
    }

    [Fact]
    public void Behaviors_are_not_found_by_scanning()
    {
        var services = new ServiceCollection();

        services.AddMediator(ScanTestAssembly);

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IPipelineBehavior<,>));
    }

    [Fact]
    public void Lifetime_applies_to_mediator_handlers_and_behaviors_even_when_set_last()
    {
        var services = new ServiceCollection();

        services.AddMediator(options =>
        {
            ScanTestAssembly(options);
            options.AddOpenBehavior(typeof(TagBehavior<,>));
            options.Lifetime = ServiceLifetime.Scoped;
        });

        Assert.Equal(ServiceLifetime.Scoped, services.Single(d => d.ServiceType == typeof(ISender)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, services.Single(d => d.ServiceType == typeof(IQueryHandler<Ping, string>)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, services.Single(d => d.ServiceType == typeof(IPipelineBehavior<,>)).Lifetime);
    }

    [Fact]
    public void Mediator_interfaces_resolve_to_one_instance_per_scope_when_scoped()
    {
        using var provider = Build(options =>
        {
            ScanTestAssembly(options);
            options.Lifetime = ServiceLifetime.Scoped;
        });
        using var scope = provider.CreateScope();

        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        Assert.Same(sender, scope.ServiceProvider.GetRequiredService<IPublisher>());
        Assert.Same(sender, scope.ServiceProvider.GetRequiredService<IMediator>());
    }

    [Fact]
    public async Task Calling_AddMediator_twice_does_not_register_anything_twice()
    {
        using var provider = Build(ScanTestAssembly, services => services.AddMediator(options =>
        {
            ScanTestAssembly(options);
            options.AddOpenBehavior(typeof(TagBehavior<,>));
        }));

        // If the second call had added the behavior or the notification handlers again, the
        // result would be tagged twice and the handlers would run twice.
        var log = provider.GetRequiredService<List<string>>();
        await provider.GetRequiredService<IPublisher>().Publish(new Greeted());

        Assert.Equal("[pong]", await provider.GetRequiredService<ISender>().Query(new Ping()));
        Assert.Equal(["greeted:first", "greeted:second"], log);
    }

    [Fact]
    public void Handlers_registered_by_hand_beforehand_are_kept()
    {
        var services = new ServiceCollection();
        services.AddTransient<IQueryHandler<Ping, string>, AlternativePingHandler<int>>();

        services.AddMediator(ScanTestAssembly);

        Assert.Equal(typeof(AlternativePingHandler<int>), services.Single(d => d.ServiceType == typeof(IQueryHandler<Ping, string>)).ImplementationType);
    }

    [Fact]
    public async Task Configured_notification_publisher_is_used()
    {
        var spy = new SpyPublisher();
        using var provider = Build(options =>
        {
            ScanTestAssembly(options);
            options.NotificationPublisher = spy;
        });

        await provider.GetRequiredService<IPublisher>().Publish(new Greeted());

        Assert.Equal(2, spy.HandlerCount);
    }

    [Fact]
    public void Two_handlers_for_the_same_query_are_rejected()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            MediatorServiceCollectionExtensions.RegisterHandlers(
                services, [typeof(PingHandler), typeof(AlternativePingHandler<int>)], ServiceLifetime.Transient));

        Assert.Contains("AlternativePingHandler", ex.Message);
    }

    [Fact]
    public void Abstract_and_open_generic_classes_are_not_registered()
    {
        var services = new ServiceCollection();

        MediatorServiceCollectionExtensions.RegisterHandlers(
            services, [typeof(AbstractPingHandler), typeof(GenericCommandHandler<>)], ServiceLifetime.Transient);

        Assert.Empty(services);
    }

    [Fact]
    public void No_assemblies_is_rejected()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddMediator(_ => { }));
    }

    [Theory]
    [InlineData(typeof(ExclaimBehavior))]
    [InlineData(typeof(TagBehavior<Ping, string>))]
    [InlineData(typeof(List<>))]
    public void AddOpenBehavior_rejects_types_that_are_not_open_generic_behaviors(Type type)
    {
        var options = new MediatorOptions();

        Assert.Throws<ArgumentException>(() => options.AddOpenBehavior(type));
    }

    private sealed record Ping : IQuery<string>;

    private sealed class PingHandler : IQueryHandler<Ping, string>
    {
        public Task<string> Handle(Ping query, CancellationToken cancellationToken) => Task.FromResult("pong");
    }

    // A second Ping handler that scanning never sees: GetTypes() only returns the open definition
    // AlternativePingHandler<T>, which scanning skips. Tests use the closed AlternativePingHandler<int>.
    private sealed class AlternativePingHandler<T> : IQueryHandler<Ping, string>
    {
        public Task<string> Handle(Ping query, CancellationToken cancellationToken) => Task.FromResult("other");
    }

    private sealed record RecordVisit(string Page) : ICommand;

    private sealed class RecordVisitHandler(List<string> log) : ICommandHandler<RecordVisit>
    {
        public Task Handle(RecordVisit command, CancellationToken cancellationToken)
        {
            log.Add($"visit:{command.Page}");
            return Task.CompletedTask;
        }
    }

    private sealed record Greeted : INotification;

    private sealed class FirstGreetedHandler(List<string> log) : INotificationHandler<Greeted>
    {
        public Task Handle(Greeted notification, CancellationToken cancellationToken)
        {
            log.Add("greeted:first");
            return Task.CompletedTask;
        }
    }

    private sealed class SecondGreetedHandler(List<string> log) : INotificationHandler<Greeted>
    {
        public Task Handle(Greeted notification, CancellationToken cancellationToken)
        {
            log.Add("greeted:second");
            return Task.CompletedTask;
        }
    }

    private sealed class TagBehavior<TRequest, TResult> : IPipelineBehavior<TRequest, TResult>
        where TRequest : IRequest<TResult>
    {
        public async Task<TResult> Handle(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
        {
            var result = await next(cancellationToken);
            return result is string text ? (TResult)(object)$"[{text}]" : result;
        }
    }

    private sealed class ExclaimBehavior : IPipelineBehavior<Ping, string>
    {
        public async Task<string> Handle(Ping request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken) =>
            await next(cancellationToken) + "!";
    }

    private abstract class AbstractPingHandler : IQueryHandler<Ping, string>
    {
        public abstract Task<string> Handle(Ping query, CancellationToken cancellationToken);
    }

    private sealed class GenericCommandHandler<TCommand> : ICommandHandler<TCommand>
        where TCommand : ICommand
    {
        public Task Handle(TCommand command, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class SpyPublisher : INotificationPublisher
    {
        public int HandlerCount { get; private set; }

        public Task Publish(IReadOnlyList<NotificationHandlerExecutor> handlers, INotification notification, CancellationToken cancellationToken)
        {
            HandlerCount = handlers.Count;
            return Task.CompletedTask;
        }
    }
}
