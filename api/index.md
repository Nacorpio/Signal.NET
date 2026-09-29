# API reference

Generated from the XML documentation comments of the four packages:

| Package | Contents |
|---|---|
| [Signal.Domain](Signal.Domain.ValueObjects.yml) | Value objects and unions (`PhoneNumber`, `Recipient`, …), envelopes, messages, entities, domain events |
| [Signal.Application](Signal.Application.Abstractions.yml) | Ports, options, message pipeline, events, command system |
| [Signal.Infrastructure](Signal.Infrastructure.yml) | signal-cli-rest-api adapters and receivers |
| [Signal.Hosting](Signal.Hosting.yml) | `AddSignal()`, `ISignalBuilder`, background service, health check |

C# 15 unions such as `Recipient` and `CommandResult` compile to structs, so they're listed as structs.
Their case types are named in each union's summary.
