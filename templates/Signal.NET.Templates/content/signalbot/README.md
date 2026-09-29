# SignalBot

A Signal bot built with [Signal.NET](https://github.com/Nacorpio/Signal.NET).

## Run it

1. Start signal-cli-rest-api:

   ```bash
   docker compose up -d
   ```

2. Link the bot to a Signal account. Open `http://localhost:8080/v1/qrcodelink?device_name=signalbot` and scan the QR code in the Signal app (Settings → Linked devices).

3. Store the account number in user secrets. They override `appsettings.json` and never get committed.

   ```bash
   dotnet user-secrets set "Signal:Accounts:0" "+4915112345678"
   dotnet user-secrets set "Signal:Commands:Admins:0" "+4915112345678"
   ```

4. Run the bot, then send `SIGNAL_PREFIXhelp` to the account from another device:

   ```bash
   dotnet run
   ```

## Next steps

- Add commands in `Commands/`. See the [command system guide](https://github.com/Nacorpio/Signal.NET/blob/main/docs/commands.md).
- React to events in `Handlers/`.
- See the [configuration reference](https://github.com/Nacorpio/Signal.NET/blob/main/docs/configuration.md).
