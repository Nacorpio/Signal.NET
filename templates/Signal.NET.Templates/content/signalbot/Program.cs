using Signal.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// Reads the "Signal" section of appsettings.json (and user secrets / environment variables).
builder.AddSignal()
    .AddCommands(typeof(Program).Assembly)
    .AddEventHandlers(typeof(Program).Assembly);

builder.Services.AddHealthChecks().AddSignalApi();

await builder.Build().RunAsync();
