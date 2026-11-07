using ControlRoom.Server;

WebApplication? app = ServerApp.Create(args, Console.Error);
if (app is null)
{
    return 1;
}

app.Run();
return 0;
