using ConsoleAppFramework;
using Mutation.Cli.Commands;

var app = ConsoleApp.Create();
app.Add("run", RunCommand.Run);
app.Add("changed-lines", ChangedLinesCommand.Run);
await app.RunAsync(args);
