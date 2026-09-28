using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Signal.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.AddSignal()                                   // binds the "Signal" section of appsettings.json
    .AddCommands(typeof(Program).Assembly)            // PingCommand, UtilityModule, GroupModule
    .AddEventHandlers(typeof(Program).Assembly)       // ReactionLogger, WelcomeHandler
    .MapCommand("about", ctx => ctx.ReplyAsync("Signal.NET sample bot 🤖"), "Shows information about this bot");

builder.Services.AddHealthChecks().AddSignalApi();

await builder.Build().RunAsync();
