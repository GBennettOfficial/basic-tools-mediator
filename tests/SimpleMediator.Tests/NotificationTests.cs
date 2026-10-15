using Microsoft.Extensions.DependencyInjection;

namespace SimpleMediator.Tests;

public class NotificationTests
{
    private static IPublisher CreatePublisher(Action<IServiceCollection> configure, INotificationPublisher? strategy = null)
    {
        var services = new ServiceCollection();
        configure(services);
        if (strategy is not null)
        {
            services.AddSingleton(strategy);
        }
        services.AddTransient<IPublisher, Mediator>();
        return services.BuildServiceProvider().GetRequiredService<IPublisher>();
    }

    [Fact]
    public async Task Publish_invokes_every_handler_in_registration_order()
    {
        var log = new List<string>();
        var publisher = CreatePublisher(s => s
            .AddSingleton(log)
            .AddTransient<INotificationHandler<UserRegistered>, SendWelcomeEmail>()
            .AddTransient<INotificationHandler<UserRegistered>, UpdateStatistics>());

        await publisher.Publish(new UserRegistered("ada"));

        Assert.Equal(["email:ada", "stats:ada"], log);
    }

    [Fact]
    public async Task Publish_with_no_handlers_does_nothing()
    {
        var publisher = CreatePublisher(_ => { });

        await publisher.Publish(new UserRegistered("nobody"));
    }

    [Fact]
    public async Task Publish_uses_the_runtime_type_of_the_notification()
    {
        var log = new List<string>();
        var publisher = CreatePublisher(s => s
            .AddSingleton(log)
            .AddTransient<INotificationHandler<UserRegistered>, SendWelcomeEmail>());

        INotification notification = new UserRegistered("grace");
        await publisher.Publish(notification);

        Assert.Equal(["email:grace"], log);
    }

    [Fact]
    public async Task Publish_null_throws_ArgumentNullException()
    {
        var publisher = CreatePublisher(_ => { });

        await Assert.ThrowsAsync<ArgumentNullException>(() => publisher.Publish(null!));
    }

    [Fact]
    public async Task Sequential_publisher_stops_at_the_first_failing_handler()
    {
        var log = new List<string>();
        var publisher = CreatePublisher(s => s
            .AddSingleton(log)
            .AddTransient<INotificationHandler<UserRegistered>, FailingHandler>()
            .AddTransient<INotificationHandler<UserRegistered>, SendWelcomeEmail>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.Publish(new UserRegistered("x")));

        Assert.Empty(log);
    }

    [Fact]
    public async Task Parallel_publisher_runs_every_handler_even_when_one_fails()
    {
        var log = new List<string>();
        var publisher = CreatePublisher(
            s => s
                .AddSingleton(log)
                .AddTransient<INotificationHandler<UserRegistered>, FailingHandler>()
                .AddTransient<INotificationHandler<UserRegistered>, SendWelcomeEmail>(),
            new ParallelNotificationPublisher());

        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.Publish(new UserRegistered("x")));

        Assert.Equal(["email:x"], log);
    }

    [Fact]
    public async Task Parallel_publisher_reports_every_failure()
    {
        var publisher = CreatePublisher(
            s => s
                .AddTransient<INotificationHandler<UserRegistered>, FailingHandler>()
                .AddTransient<INotificationHandler<UserRegistered>, FailingHandler>(),
            new ParallelNotificationPublisher());

        var ex = await Assert.ThrowsAsync<AggregateException>(() => publisher.Publish(new UserRegistered("x")));

        Assert.Equal(2, ex.InnerExceptions.Count);
    }

    private sealed record UserRegistered(string UserName) : INotification;

    private sealed class SendWelcomeEmail(List<string> log) : INotificationHandler<UserRegistered>
    {
        public Task Handle(UserRegistered notification, CancellationToken cancellationToken)
        {
            log.Add($"email:{notification.UserName}");
            return Task.CompletedTask;
        }
    }

    private sealed class UpdateStatistics(List<string> log) : INotificationHandler<UserRegistered>
    {
        public Task Handle(UserRegistered notification, CancellationToken cancellationToken)
        {
            log.Add($"stats:{notification.UserName}");
            return Task.CompletedTask;
        }
    }

    // Deliberately not async, to check that a synchronous throw is handled like a faulted task.
    private sealed class FailingHandler : INotificationHandler<UserRegistered>
    {
        public Task Handle(UserRegistered notification, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("boom");
    }
}
