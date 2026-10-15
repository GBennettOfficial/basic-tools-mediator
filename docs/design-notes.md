# Design notes

These notes explain why SimpleMediator is built the way it is. Each step of the plan adds a section.

## Step 1: core contracts and dispatch

### Commands and queries are separate types

CQRS (Command Query Responsibility Segregation) splits the operations of a system into two kinds:

- **Commands** change state (`CreateUser`, `RenameUser`). They may return a small result, such as a new id.
- **Queries** read state (`GetUser`) and must not change anything.

MediatR uses one `IRequest<T>` for both, so nothing stops a "query" from writing to the database.
SimpleMediator gives each kind its own interface (`ICommand`, `ICommand<TResult>`, `IQuery<TResult>`)
and its own handler interface. The benefits:

- The intent of a message is visible from its type.
- Pipeline behaviors (step 2) can target only commands, for example wrapping them in a database
  transaction, or only queries, for example caching.
- `ISender` has `Send` for commands and `Query` for queries, so a call site reads as what it does.

Both still derive from a shared `IRequest<TResult>` so the dispatcher and behaviors that do not
care about the difference can treat them uniformly.

### `Unit` instead of `void`

C# cannot write `Task<void>`, so a result-less command is modelled as `ICommand : ICommand<Unit>`.
`Unit` is a value type with a single value and no data. Internally every request then has a
`TResult`, which means one dispatcher and (in step 2) one pipeline shape cover everything.

Handlers of result-less commands should not have to write `return Unit.Value;`, so there is a
separate `ICommandHandler<TCommand>` that returns a plain `Task`. The mediator adapts it.

### Invariant result types

MediatR declares `IRequest<out TResponse>` (covariant). SimpleMediator leaves the result type
invariant. Covariance would make a `GetName : IQuery<string>` also count as an `IQuery<object>`,
so `Query<object>(new GetName())` would compile and then fail at runtime looking for an
`IQueryHandler<GetName, object>`. Invariance turns that mistake into a compile error.

### Exactly one handler, resolved from DI

A command or query has exactly one handler. If none is registered, the mediator throws
`HandlerNotFoundException`, which names both the request and the handler interface it looked for.
It derives from `InvalidOperationException` because a missing handler is a configuration bug.

The mediator does not create handlers itself; it asks `IServiceProvider` for them. That way
handlers get their own dependencies injected, and their lifetime (transient, scoped, singleton)
is controlled by the container. `Mediator` depends only on `System.IServiceProvider`, so the core
library has no package dependencies.

### Dispatching without per-call reflection

The mediator receives an `IRequest<TResult>` but must resolve, say,
`IQueryHandler<GetUser, UserDto>`. That closed generic type depends on the request's runtime type,
so it has to be built with reflection (`MakeGenericType`). Reflection is slow compared to a normal
call, so it is done once per request type:

1. The first time `GetUser` is sent, the mediator builds a `QueryHandlerWrapper<GetUser, UserDto>`
   and stores it in a `ConcurrentDictionary` keyed by `typeof(GetUser)`.
2. Every later send is a dictionary lookup plus ordinary, strongly typed calls.

The cache is a static field of `RequestHandlerWrapper<TResult>`. Because static fields are per
closed generic type, there is one dictionary per result type, and entries never need casting from
`object`. This is the same "wrapper" technique MediatR uses.

### `Task` rather than `ValueTask`

`ValueTask` can avoid an allocation when a handler completes synchronously, but it has rules
(await it once, never await it twice) that are easy to break. Handlers usually do real I/O, so
`Task` is the simpler, safer default for a learning-focused library.

### Build settings

- `Directory.Build.props` holds settings shared by all projects (target framework, nullable).
- `Directory.Packages.props` turns on central package management so versions live in one place.
- The library sets `GenerateDocumentationFile` and `TreatWarningsAsErrors`, so a public member
  without a doc comment fails the build.

## Step 2: notifications and pipeline behaviors

### Notifications are events, not requests

A notification (`INotification`) announces something that already happened, such as
`UserRegistered`. It differs from a command or query in three ways:

- It can have **any number of handlers**, including none. Publishing with no handlers is not an
  error, because the publisher should not need to know who is listening.
- It has **no result**. Handlers react; they do not answer.
- It goes through `IPublisher.Publish`, not `ISender`, so call sites make the difference obvious.

`IMediator` combines `ISender` and `IPublisher` for convenience, but injecting the narrower
interface documents what a class actually does.

`Publish` dispatches on the notification's **runtime** type (`notification.GetType()`), using the
same cached-wrapper trick as requests. So code that holds a list of `INotification` (for example
domain events collected on an entity) can publish each one and still reach the right handlers.

### Publishing strategy is pluggable

How several handlers should run is a real trade-off, so it is a strategy (`INotificationPublisher`)
rather than hard-coded:

- `SequentialNotificationPublisher` (default) awaits handlers one at a time in registration order.
  The first exception stops the rest. Handlers never overlap, so they can share scoped services
  such as an EF Core `DbContext`, which is not thread-safe.
- `ParallelNotificationPublisher` starts all handlers and waits for all of them. Every handler runs
  even if one fails. `await Task.WhenAll(...)` only rethrows the *first* exception, so when several
  fail it throws an `AggregateException` with all of them. It also turns a handler that throws
  synchronously (a non-`async` method) into a faulted task, so one bad handler cannot stop the
  others from starting.

Each handler reaches the strategy as a `NotificationHandlerExecutor`: the handler instance plus a
callback. The strategy can run handlers without knowing the notification's generic type, and can
still see which handler it is running (useful for logging).

### Pipeline behaviors are middleware for requests

`IPipelineBehavior<TRequest, TResult>` wraps command and query handling, exactly like ASP.NET Core
middleware wraps HTTP requests. Each behavior gets the request and a `next` delegate:

```csharp
public async Task<TResult> Handle(TRequest request, RequestHandlerDelegate<TResult> next, CancellationToken ct)
{
    // before the handler
    var result = await next(ct);
    // after the handler
    return result;
}
```

A behavior can run code before and after, change the result, or never call `next` (a validation
behavior that throws, or a cache that returns a stored value). The first registered behavior is the
outermost. The dispatcher builds the chain from the inside out: it starts with "call the handler",
then wraps it with the last behavior, then the one before, and so on.

The `next` delegate takes a `CancellationToken` so a behavior can pass on a different one, for
example a linked token that adds a timeout.

### Targeting commands or queries with generic constraints

Separating commands and queries in step 1 pays off here. A behavior that should only wrap commands
(say, a database transaction) just adds a constraint:

```csharp
public class TransactionBehavior<TRequest, TResult> : IPipelineBehavior<TRequest, TResult>
    where TRequest : ICommand<TResult>
```

When it is registered as an open generic, Microsoft's container checks the constraint and leaves
the behavior out for queries. No marker attributes or `if (request is ICommand)` checks are needed.
A behavior can also target a single request type by implementing, say,
`IPipelineBehavior<GetOrder, string>` directly.

### The handler is resolved before the behaviors run

If no handler is registered, the mediator throws `HandlerNotFoundException` before any behavior
runs. Otherwise a missing registration would only show up if the innermost behavior called `next`,
and a caching behavior could hide it entirely until the cache was cold. The small cost is that the
handler is created even when a behavior short-circuits.

## Step 3: dependency injection and assembly scanning

### One call to set everything up

```csharp
builder.Services.AddSimpleMediator(options =>
{
    options.RegisterServicesFromAssemblyContaining<Program>();
    options.AddOpenBehavior(typeof(LoggingBehavior<,>));
    options.AddOpenBehavior(typeof(ValidationBehavior<,>));
});
```

There is also `AddSimpleMediator(params Assembly[])` for the common case with no behaviors.

The extension method lives in the `Microsoft.Extensions.DependencyInjection` namespace. That is a
convention most libraries follow (EF Core, logging, options) so the method appears in IntelliSense
wherever services are configured, without an extra `using`.

The library depends only on `Microsoft.Extensions.DependencyInjection.Abstractions`, which defines
`IServiceCollection` but contains no container. Apps bring the container; a library should not.

### What scanning registers, and what it does not

Scanning looks at every concrete class in the given assemblies (public or internal) and registers
it for each handler interface it implements: `ICommandHandler<>`, `ICommandHandler<,>`,
`IQueryHandler<,>` and `INotificationHandler<>`.

- **Abstract classes and open generic classes are skipped.** The container cannot build
  `Handler<T>` without knowing `T`.
- **Two classes handling the same command or query is an error at startup.** A request has exactly
  one handler, and silently picking one ("last registration wins") would hide a bug until runtime.
- **Notification handlers can repeat**, since many handlers per notification is the point.
- **Pipeline behaviors are not scanned.** This deliberately differs from the original plan.
  Behavior order matters (validation before a transaction, logging around everything), and the
  order classes come back from reflection is not something to rely on. So behaviors are added
  explicitly with `AddOpenBehavior` / `AddBehavior`, and run in the order they are added.

### Safe to call twice, and respects manual registrations

Every registration uses the `TryAdd` family. `TryAdd` skips a service that is already registered,
so a handler you register by hand before calling `AddSimpleMediator` wins over the scanned one.
`TryAddEnumerable` (for notification handlers and behaviors) allows many implementations of one
interface but skips an exact duplicate. Calling `AddSimpleMediator` twice, which happens easily
when several modules each call it, therefore registers nothing twice.

### Lifetimes

`options.Lifetime` (default transient) applies to the mediator, handlers and behaviors.
Transient is the safest default: a transient handler may depend on a scoped `DbContext`, while a
singleton that does so is rejected by the container's scope validation.

The `Lifetime` is read when services are registered, not when `AddOpenBehavior` is called, so the
order of lines inside the configure callback does not matter.

`Mediator` is registered once as itself, and `IMediator`, `ISender` and `IPublisher` are forwarded
to it with factory registrations. With a scoped lifetime, all three resolve to the same instance
within a scope instead of three separate ones.

The notification publisher is registered as a singleton, since strategies are stateless.

### Testing the scanner

The scanning loop takes a plain list of types (an `internal` method, visible to the tests through
`InternalsVisibleTo`), so tests can hand it two handlers for the same query without putting a
duplicate in a real assembly, which would break every other scan. The tests also build the
container with `ValidateOnBuild` and `ValidateScopes`, which check that every registration can
actually be constructed and that no singleton captures a scoped service.

## Step 4: packaging and CI

### Package identity

The package id is `GBennett.SimpleMediator` because `SimpleMediator` and `BasicMediator` are
already taken on nuget.org. An owner prefix is common practice, and nuget.org lets you reserve a
prefix (such as `GBennett.`) so nobody else can publish under it. The C# namespace stays
`SimpleMediator`; package id and namespace do not have to match.

### What goes in the package

- `SimpleMediator.dll` and `SimpleMediator.xml`. The XML file holds the doc comments, which is
  what makes them appear in IntelliSense for people using the package.
- `README.md`, shown on the nuget.org page (`PackageReadmeFile`).
- A separate `.snupkg` symbols package. Together with Source Link, which the .NET SDK enables
  automatically for GitHub repositories, users can step into the library's source while debugging.
- License metadata as an SPDX expression (`MIT`) rather than a file, which nuget.org prefers.

`ContinuousIntegrationBuild` is switched on only under GitHub Actions. It makes builds
deterministic (file paths are normalised), so the same commit always produces the same package.
It is off locally because it would make local debugging paths less useful.

### CI and releases

- `ci.yml` runs on every push to `main` and every pull request: restore, build, test, pack, and
  upload the package as a build artifact. Nothing is published.
- `release.yml` runs only when a tag like `v1.0.0` is pushed. It reads the version from the tag,
  tests, packs and pushes to nuget.org using a repository secret named `NUGET_API_KEY`.

Making the git tag the single source of truth for the version means the version in the project
file never has to be edited for a release, and a published version always matches a tag in the
repository. `--skip-duplicate` makes re-running a release job harmless.

To publish: create an API key on nuget.org scoped to `GBennett.SimpleMediator`, add it as the
`NUGET_API_KEY` secret in the GitHub repository settings, then `git tag v0.1.0 && git push --tags`.
