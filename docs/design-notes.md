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
