# Signal.NET templates

`dotnet new` templates for [Signal.NET](https://github.com/Nacorpio/Signal.NET).

```bash
dotnet new install Nacorpio.Signal.Templates
dotnet new signalbot -n MyBot --mode JsonRpc --account +4915112345678
```

| Option | Default | Description |
|---|---|---|
| `--mode` | `JsonRpc` | Container execution mode: `Normal`, `Native`, `JsonRpc` or `JsonRpcNative`. Sets both `appsettings.json` and `docker-compose.yml`. |
| `--account` | `+10000000000` | The bot's phone number (E.164). Also configured as the command admin. |
| `--prefix` | `/` | Command prefix |
| `--skipDocker` | `false` | Omit `docker-compose.yml` |

The generated project is a worker service with a sample command module, an event handler, and a
`docker-compose.yml` for signal-cli-rest-api. It needs the .NET 11 SDK.
