# BasicTools.Mediator

A small mediator for .NET built around CQRS. It routes **commands** and **queries** to a single
handler, **publishes notifications** to any number of handlers, wraps requests in **pipeline
behaviors**, and registers everything with **Microsoft.Extensions.DependencyInjection** by scanning
your assemblies.

It is inspired by [MediatR](https://github.com/jbogard/MediatR) but keeps commands and queries as
separate types, so the intent of every message is visible from its type.

```
dotnet add package BasicTools.Mediator
```

Requires .NET 10.

## Setup

```csharp
builder.Services.AddMediator(options =>
{
    options.RegisterServicesFromAssemblyContaining<Program>();

    // Optional: behaviors run in the order they are added, first added is outermost.
    options.AddOpenBehavior(typeof(LoggingBehavior<,>));
    options.AddOpenBehavior(typeof(ValidationBehavior<,>));
});
```

Or, with no options: `builder.Services.AddMediator(typeof(Program).Assembly);`

Inject `ISender` to send commands and queries, `IPublisher` to publish notifications, or
`IMediator` for both.

## Commands

A command changes state. It can return nothing:

```csharp
public sealed record RenameUser(int Id, string Name) : ICommand;

public sealed class RenameUserHandler(AppDbContext db) : ICommandHandler<RenameUser>
{
    public async Task Handle(RenameUser command, CancellationToken cancellationToken)
    {
        var user = await db.Users.FindAsync([command.Id], cancellationToken);
        user!.Name = command.Name;
        await db.SaveChangesAsync(cancellationToken);
    }
}

await sender.Send(new RenameUser(7, "Ada"));
```

or a result, such as the id of something it created:

```csharp
public sealed record CreateUser(string Name) : ICommand<int>;

public sealed class CreateUserHandler(AppDbContext db) : ICommandHandler<CreateUser, int>
{
    public async Task<int> Handle(CreateUser command, CancellationToken cancellationToken)
    {
        var user = new User { Name = command.Name };
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        return user.Id;
    }
}

int id = await sender.Send(new CreateUser("Grace"));
```

## Queries

A query reads data and never changes it:

```csharp
public sealed record GetUser(int Id) : IQuery<UserDto?>;

public sealed class GetUserHandler(AppDbContext db) : IQueryHandler<GetUser, UserDto?>
{
    public Task<UserDto?> Handle(GetUser query, CancellationToken cancellationToken) =>
        db.Users.Where(u => u.Id == query.Id)
                .Select(u => new UserDto(u.Id, u.Name))
                .SingleOrDefaultAsync(cancellationToken);
}

UserDto? user = await sender.Query(new GetUser(7));
```

Every command and query must have exactly one handler. Sending one without a handler throws
`HandlerNotFoundException`; scanning two handlers for the same one fails at startup.

## Notifications

A notification announces that something happened. It can have any number of handlers, or none:

```csharp
public sealed record UserRegistered(int UserId) : INotification;

public sealed class SendWelcomeEmail(IEmailSender email) : INotificationHandler<UserRegistered> { ... }
public sealed class UpdateSignupStats(Stats stats) : INotificationHandler<UserRegistered> { ... }

await publisher.Publish(new UserRegistered(id));
```

By default handlers run one at a time in registration order, and the first exception stops the rest.
To run them concurrently instead:

```csharp
options.NotificationPublisher = new ParallelNotificationPublisher();
```

Parallel handlers must not share services that are not thread-safe, such as a scoped `DbContext`.
You can also write your own strategy by implementing `INotificationPublisher`.

## Pipeline behaviors

Behaviors wrap command and query handling, like middleware:

```csharp
public sealed class LoggingBehavior<TRequest, TResult>(ILogger<LoggingBehavior<TRequest, TResult>> logger)
    : IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    public async Task<TResult> Handle(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken cancellationToken)
    {
        logger.LogInformation("Handling {Request}", typeof(TRequest).Name);
        var result = await next(cancellationToken);
        logger.LogInformation("Handled {Request}", typeof(TRequest).Name);
        return result;
    }
}
```

To apply a behavior only to commands, constrain it. The container skips it for queries:

```csharp
public sealed class TransactionBehavior<TRequest, TResult> : IPipelineBehavior<TRequest, TResult>
    where TRequest : ICommand<TResult>
```

For a behavior that targets one request type:

```csharp
options.AddBehavior<IPipelineBehavior<GetUser, UserDto?>, CacheUserBehavior>();
```

## Options

| Option | Default | Purpose |
| --- | --- | --- |
| `RegisterServicesFromAssemblies` / `RegisterServicesFromAssemblyContaining<T>` | none, at least one required | Assemblies scanned for handlers |
| `AddOpenBehavior` / `AddBehavior` | none | Pipeline behaviors, in run order |
| `Lifetime` | `Transient` | Lifetime of the mediator, handlers and behaviors |
| `NotificationPublisher` | `SequentialNotificationPublisher` | How notification handlers are run |

Handlers you register yourself before calling `AddMediator` are kept, and calling it more than
once does not register anything twice.

## Design

[docs/design-notes.md](docs/design-notes.md) explains why the library is built the way it is.

## License

MIT
