using ControlRoom.Server;

if (VersionCommand.IsCommand(args))
{
    return VersionCommand.Run(args, Console.Out, Console.Error);
}

if (TokenCommand.IsCommand(args))
{
    return TokenCommand.Run(args, Console.Out, Console.Error);
}

WebApplication? app = ServerApp.Create(args, Console.Error);
if (app is null)
{
    return 1;
}

app.Run();
return 0;
