# Architecture

Signal.NET follows Domain-Driven Design with a layered ("onion") structure. Dependency injection
composes all of it. Each layer is a separate project, and references point inward only.

```
┌──────────────────────────────────────────────────────────────┐
│ Signal.Hosting         AddSignal(), ISignalBuilder,           │
│                        SignalHostedService, health check      │
│   ┌──────────────────────────────────────────────────────┐   │
│   │ Signal.Infrastructure  REST adapters, receivers,      │   │
│   │                        JSON contracts, mapping        │   │
│   │   ┌──────────────────────────────────────────────┐   │   │
│   │   │ Signal.Application  ports, options, pipeline, │   │   │
│   │   │                     events, command system    │   │   │
│   │   │   ┌──────────────────────────────────────┐   │   │   │
│   │   │   │ Signal.Domain  value objects,         │   │   │   │
│   │   │   │                envelope, events       │   │   │   │
│   │   │   └──────────────────────────────────────┘   │   │   │
│   │   └──────────────────────────────────────────────┘   │   │
│   └──────────────────────────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────┘
```

## Projects and their purpose

| Project | Purpose | Depends on |
|---|---|---|
| `Signal.Domain` | The language of Signal: phone numbers, groups, recipients, envelopes, messages and events. It enforces invariants such as "a phone number is valid E.164" and "a message has a recipient and content". It has no framework dependencies. | nothing |
| `Signal.Application` | What the framework does with the domain. It defines the ports it needs from the outside world, the options model, the per-message pipeline, event dispatching and the whole command system. It knows nothing about HTTP or JSON. | Domain, Microsoft.Extensions abstractions |
| `Signal.Infrastructure` | How the framework talks to signal-cli-rest-api. It implements the ports over HTTP and WebSocket, translates the API's JSON into domain objects (an anti-corruption layer), and adds resilience. | Application, `Microsoft.Extensions.Http(.Resilience)` |
| `Signal.Hosting` | How the framework runs inside a .NET Generic Host. It provides the one-call registration, the fluent builder, the background receive loop and health checks. | Infrastructure, Microsoft.Extensions hosting / health checks |
| `samples/Signal.Sample.Bot` | A runnable reference bot. | Hosting |

### Why the layers matter

- **Testability:** the command system and pipeline can be tested with in-memory fakes of the ports, without a container. See `tests/Signal.Application.Tests/TestHarness.cs`.
- **Replaceability:** you can change the transport (for example to a signal-cli JSON-RPC socket, or a message-queue bridge) by implementing the ports. The commands don't change.
- **Stability of the domain:** API changes in signal-cli-rest-api are absorbed in `Infrastructure/Mapping` and the DTOs. They don't spread into user code.

## Dependency injection conventions

- Every layer exposes one registration method: `AddSignalApplication()` and `AddSignalInfrastructure()`. The hosting layer's `AddSignal()` composes both.
- Framework services are registered with `TryAdd*`. **If you register your own implementation first, it wins.** Collection-style registrations (converters, event handlers) use `TryAddEnumerable`.
- Lifetimes:
  - **Singleton:** stateless or shared-state services (parser, registry, cooldown tracker, rate limiter, pipeline, receiver factory).
  - **Scoped:** per-message services (middleware, command executor, event dispatcher, event handlers, class-based commands).
  - **Transient:** HTTP adapters (they wrap the typed `HttpClient` from `IHttpClientFactory`) and receivers.
- Receivers are **keyed services** keyed by `ExecutionMode`, so the mode selects the transport without conditionals.

## Life of a message

```
                 signal-cli-rest-api container
                            │
         normal/native      │      json-rpc/json-rpc-native
      GET /v1/receive/{n} ◄─┴─► ws://…/v1/receive/{n}
            (polling)                (push, auto-reconnect)
                            │
            PollingMessageReceiver / WebSocketMessageReceiver
                            │   EnvelopeMapper: JSON → IncomingEnvelope
                            ▼
                 SignalHostedService (one loop per account)
                            │   hash(conversation) % MaxConcurrency
                            ▼
              partition channel ─► worker (one per partition)
                            │   new DI scope + MessageContext
                            ▼
   ExceptionHandlingMiddleware   catches and logs; one bad message never stops the bot
   LoggingMiddleware             timing / debug log
   AccessControlMiddleware       allow list, block list, own messages
   RateLimitingMiddleware        per-sender fixed window
   [your middleware]             AddMiddleware<T>()
   DomainEventMiddleware         IEventHandler<MessageReceived|ReactionReceived|…>
   CommandMiddleware             parse → preconditions → bind → execute → result handler
                            │
                            ▼
          IMessageSender / IReactionService … → REST API → Signal
```

### Concurrency model

- One receive loop runs per configured account.
- Envelopes are spread over `MaxConcurrency` bounded channels by a hash of the conversation (group id or sender).
- Each channel has exactly one worker. **Conversations are processed in parallel. Messages within one conversation are processed strictly in order.**
- A slow command blocks only its own conversation (and any other conversations that hash to the same partition). Move long-running work to the background queue (`RunInBackgroundAsync` in commands, `IBackgroundWorkQueue` elsewhere) instead of awaiting it inside a command.
- All channels are bounded. When processing falls behind, back-pressure propagates to the receiver, which slows polling or reading from the socket, instead of growing memory without limit.

### Shutdown

When the host stops, the stopping token is cancelled. Receive loops end, the partition channels are
completed, and the workers drain what is left. Messages still queued are passed through with a cancelled
token, so they are not processed further.
