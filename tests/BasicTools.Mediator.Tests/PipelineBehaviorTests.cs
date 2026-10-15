using Microsoft.Extensions.DependencyInjection;

namespace BasicTools.Mediator.Tests;

public class PipelineBehaviorTests
{
    private readonly List<string> _log = [];

    private ISender CreateSender(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_log);
        services.AddTransient<ICommandHandler<PlaceOrder, int>, PlaceOrderHandler>();
        services.AddTransient<ICommandHandler<CancelOrder>, CancelOrderHandler>();
        services.AddTransient<IQueryHandler<GetOrder, string>, GetOrderHandler>();
        configure(services);
        services.AddTransient<ISender, Mediator>();
        return services.BuildServiceProvider().GetRequiredService<ISender>();
    }

    [Fact]
    public async Task Behaviors_run_around_the_handler_with_the_first_registered_outermost()
    {
        var sender = CreateSender(s => s
            .AddTransient(typeof(IPipelineBehavior<,>), typeof(OuterBehavior<,>))
            .AddTransient(typeof(IPipelineBehavior<,>), typeof(InnerBehavior<,>)));

        var id = await sender.Send(new PlaceOrder(3));

        Assert.Equal(42, id);
        Assert.Equal(["outer:before", "inner:before", "handler", "inner:after", "outer:after"], _log);
    }

    [Fact]
    public async Task Behaviors_also_wrap_commands_without_a_result()
    {
        var sender = CreateSender(s => s.AddTransient(typeof(IPipelineBehavior<,>), typeof(OuterBehavior<,>)));

        await sender.Send(new CancelOrder());

        Assert.Equal(["outer:before", "handler", "outer:after"], _log);
    }

    [Fact]
    public async Task Validation_behavior_can_reject_a_request_before_the_handler_runs()
    {
        var sender = CreateSender(s => s.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>)));

        await Assert.ThrowsAsync<ArgumentException>(() => sender.Send(new PlaceOrder(Quantity: 0)));

        Assert.DoesNotContain("handler", _log);
    }

    [Fact]
    public async Task Behavior_constrained_to_commands_is_skipped_for_queries()
    {
        var sender = CreateSender(s => s.AddTransient(typeof(IPipelineBehavior<,>), typeof(CommandOnlyBehavior<,>)));

        await sender.Query(new GetOrder());
        await sender.Send(new PlaceOrder(1));

        Assert.Equal(["handler", "command-only", "handler"], _log);
    }

    [Fact]
    public async Task Behavior_registered_for_one_request_type_only_applies_to_that_type()
    {
        var sender = CreateSender(s => s.AddTransient<IPipelineBehavior<GetOrder, string>, UppercaseOrderBehavior>());

        var order = await sender.Query(new GetOrder());
        await sender.Send(new PlaceOrder(1));

        Assert.Equal("ORDER #1", order);
    }

    [Fact]
    public async Task Missing_handler_is_reported_even_when_behaviors_are_registered()
    {
        var sender = CreateSender(s => s.AddTransient(typeof(IPipelineBehavior<,>), typeof(OuterBehavior<,>)));

        await Assert.ThrowsAsync<HandlerNotFoundException>(() => sender.Query(new Unhandled()));

        Assert.Empty(_log);
    }

    // A typical logging behavior: runs for every command and query.
    private sealed class OuterBehavior<TRequest, TResult>(List<string> log) : IPipelineBehavior<TRequest, TResult>
        where TRequest : IRequest<TResult>
    {
        public async Task<TResult> Handle(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
        {
            log.Add("outer:before");
            var result = await next(cancellationToken);
            log.Add("outer:after");
            return result;
        }
    }

    private sealed class InnerBehavior<TRequest, TResult>(List<string> log) : IPipelineBehavior<TRequest, TResult>
        where TRequest : IRequest<TResult>
    {
        public async Task<TResult> Handle(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
        {
            log.Add("inner:before");
            var result = await next(cancellationToken);
            log.Add("inner:after");
            return result;
        }
    }

    // A typical validation behavior: checks requests that know how to validate themselves.
    private interface IValidatable
    {
        IEnumerable<string> Validate();
    }

    private sealed class ValidationBehavior<TRequest, TResult> : IPipelineBehavior<TRequest, TResult>
        where TRequest : IRequest<TResult>
    {
        public Task<TResult> Handle(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
        {
            var errors = (request as IValidatable)?.Validate().ToArray() ?? [];
            return errors.Length > 0
                ? throw new ArgumentException(string.Join("; ", errors))
                : next(cancellationToken);
        }
    }

    // The constraint means the container only uses this behavior for commands.
    private sealed class CommandOnlyBehavior<TRequest, TResult>(List<string> log) : IPipelineBehavior<TRequest, TResult>
        where TRequest : ICommand<TResult>
    {
        public Task<TResult> Handle(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
        {
            log.Add("command-only");
            return next(cancellationToken);
        }
    }

    private sealed class UppercaseOrderBehavior : IPipelineBehavior<GetOrder, string>
    {
        public async Task<string> Handle(GetOrder request, RequestHandlerDelegate<string> next, CancellationToken cancellationToken) =>
            (await next(cancellationToken)).ToUpperInvariant();
    }

    private sealed record PlaceOrder(int Quantity) : ICommand<int>, IValidatable
    {
        public IEnumerable<string> Validate()
        {
            if (Quantity <= 0)
            {
                yield return "Quantity must be positive.";
            }
        }
    }

    private sealed class PlaceOrderHandler(List<string> log) : ICommandHandler<PlaceOrder, int>
    {
        public Task<int> Handle(PlaceOrder command, CancellationToken cancellationToken)
        {
            log.Add("handler");
            return Task.FromResult(42);
        }
    }

    private sealed record CancelOrder : ICommand;

    private sealed class CancelOrderHandler(List<string> log) : ICommandHandler<CancelOrder>
    {
        public Task Handle(CancelOrder command, CancellationToken cancellationToken)
        {
            log.Add("handler");
            return Task.CompletedTask;
        }
    }

    private sealed record GetOrder : IQuery<string>;

    private sealed class GetOrderHandler(List<string> log) : IQueryHandler<GetOrder, string>
    {
        public Task<string> Handle(GetOrder query, CancellationToken cancellationToken)
        {
            log.Add("handler");
            return Task.FromResult("Order #1");
        }
    }

    private sealed record Unhandled : IQuery<string>;
}
