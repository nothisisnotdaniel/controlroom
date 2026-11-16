# ControlRoom

ControlRoom is a control room for the software factory. A small server on your Mac reads the factory root
(the folder that holds `ControlRoom/`) and serves one web app. The app shows every project's backlog and the
factory's jobs, and lets you edit backlogs. You use the same app in a browser on the Mac and on the iPhone,
where you can add it to the home screen.

The server listens on `127.0.0.1:8100` only. To reach it from the iPhone, use `tailscale serve`, which gives
you HTTPS on your own tailnet and forwards to the local port.

## Run it

From `ControlRoom/`:

```sh
dotnet run --project src/ControlRoom.Server
```

Then open http://127.0.0.1:8100. The factory root is the parent of the folder that holds `ControlRoom.slnx`.
To use another one, add `-- --FactoryRoot <path>`.

## Get the access token

Every API call needs the access token. The app asks for it once and remembers it.

```sh
dotnet run --project src/ControlRoom.Server -- token        # prints the token (creates it when missing)
dotnet run --project src/ControlRoom.Server -- token --new  # replaces it
```

The token is stored in `~/Library/Application Support/ControlRoom/access-token`.

## Start it at login

`scripts/launch-agent.sh` installs a macOS LaunchAgent. The server then starts when you log in and starts
again if it stops.

```sh
scripts/launch-agent.sh install    # publishes the server and starts it
scripts/launch-agent.sh status     # shows installed: yes|no, running: yes|no and the log path
scripts/launch-agent.sh uninstall  # stops it and removes the agent and the published server
```

Run `install` again after you pull changes. It publishes the server again and restarts it.

- Published server: `~/Library/Application Support/ControlRoom/server/`
- Agent: `~/Library/LaunchAgents/local.controlroom.server.plist`
- Log (output and errors): `~/Library/Logs/ControlRoom/server.log`

To follow the log:

```sh
tail -f ~/Library/Logs/ControlRoom/server.log
```

`uninstall` keeps the log and the access token.
