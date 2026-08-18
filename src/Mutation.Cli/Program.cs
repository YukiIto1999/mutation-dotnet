using ConsoleAppFramework;
using Mutation.Cli.Commands;

var app = ConsoleApp.Create();
app.Add("run", RunCommand.Run);
await app.RunAsync(args);
