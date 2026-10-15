using Microsoft.Extensions.DependencyInjection;

namespace SimpleMediator.Tests;

public class SenderTests
{
    // Handlers are registered by hand here; step 3 adds assembly scanning to do this automatically.
    private static ISender CreateSender(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        services.AddTransient<ISender, Mediator>();
        return services.BuildServiceProvider().GetRequiredService<ISender>();
    }

    [Fact]
    public async Task Send_command_without_result_invokes_its_handler()
    {
        var log = new List<string>();
        var sender = CreateSender(s => s
            .AddSingleton(log)
            .AddTransient<ICommandHandler<RenameUser>, RenameUserHandler>());

        await sender.Send(new RenameUser(7, "Ada"));

        Assert.Equal(["7:Ada"], log);
    }

    [Fact]
    public async Task Send_command_with_result_returns_handler_result()
    {
        var sender = CreateSender(s => s.AddTransient<ICommandHandler<CreateUser, int>, CreateUserHandler>());

        var id = await sender.Send(new CreateUser("Grace"));

        Assert.Equal(5, id);
    }

    [Fact]
    public async Task Query_returns_handler_result()
    {
        var sender = CreateSender(s => s.AddTransient<IQueryHandler<GetGreeting, string>, GetGreetingHandler>());

        var greeting = await sender.Query(new GetGreeting("Linus"));

        Assert.Equal("Hello, Linus", greeting);
    }

    [Fact]
    public async Task Cancellation_token_is_passed_to_handler()
    {
        using var cts = new CancellationTokenSource();
        var sender = CreateSender(s => s.AddTransient<IQueryHandler<EchoToken, CancellationToken>, EchoTokenHandler>());

        var received = await sender.Query(new EchoToken(), cts.Token);

        Assert.Equal(cts.Token, received);
    }

    [Fact]
    public async Task Missing_handler_throws_HandlerNotFoundException()
    {
        var sender = CreateSender(_ => { });

        var ex = await Assert.ThrowsAsync<HandlerNotFoundException>(() => sender.Query(new GetGreeting("x")));

        Assert.Equal(typeof(GetGreeting), ex.RequestType);
        Assert.Equal(typeof(IQueryHandler<GetGreeting, string>), ex.HandlerType);
    }

    [Fact]
    public async Task Missing_handler_for_command_without_result_names_the_right_interface()
    {
        var sender = CreateSender(_ => { });

        var ex = await Assert.ThrowsAsync<HandlerNotFoundException>(() => sender.Send(new RenameUser(1, "x")));

        Assert.Equal(typeof(ICommandHandler<RenameUser>), ex.HandlerType);
    }

    [Fact]
    public async Task Null_request_throws_ArgumentNullException()
    {
        var sender = CreateSender(_ => { });

        await Assert.ThrowsAsync<ArgumentNullException>(() => sender.Send((ICommand)null!));
    }

    [Fact]
    public async Task Request_that_is_both_command_and_query_is_rejected()
    {
        var sender = CreateSender(_ => { });

        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.Query(new Confused()));
    }

    [Fact]
    public async Task Handler_exceptions_propagate_to_the_caller()
    {
        var sender = CreateSender(s => s.AddTransient<ICommandHandler<Explode>, ExplodeHandler>());

        await Assert.ThrowsAsync<TimeoutException>(() => sender.Send(new Explode()));
    }

    private sealed record RenameUser(int Id, string Name) : ICommand;

    private sealed class RenameUserHandler(List<string> log) : ICommandHandler<RenameUser>
    {
        public Task Handle(RenameUser command, CancellationToken cancellationToken)
        {
            log.Add($"{command.Id}:{command.Name}");
            return Task.CompletedTask;
        }
    }

    private sealed record CreateUser(string Name) : ICommand<int>;

    private sealed class CreateUserHandler : ICommandHandler<CreateUser, int>
    {
        public Task<int> Handle(CreateUser command, CancellationToken cancellationToken) =>
            Task.FromResult(command.Name.Length);
    }

    private sealed record GetGreeting(string Name) : IQuery<string>;

    private sealed class GetGreetingHandler : IQueryHandler<GetGreeting, string>
    {
        public Task<string> Handle(GetGreeting query, CancellationToken cancellationToken) =>
            Task.FromResult($"Hello, {query.Name}");
    }

    private sealed record EchoToken : IQuery<CancellationToken>;

    private sealed class EchoTokenHandler : IQueryHandler<EchoToken, CancellationToken>
    {
        public Task<CancellationToken> Handle(EchoToken query, CancellationToken cancellationToken) =>
            Task.FromResult(cancellationToken);
    }

    private sealed record Confused : ICommand<int>, IQuery<int>;

    private sealed record Explode : ICommand;

    private sealed class ExplodeHandler : ICommandHandler<Explode>
    {
        public Task Handle(Explode command, CancellationToken cancellationToken) => throw new TimeoutException();
    }
}
