using ControlRoom.Server;

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
