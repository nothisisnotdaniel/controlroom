# 007 Host-header check

As the user, I want the server to refuse requests with an unexpected Host header, so that a web page can't reach it through DNS rebinding.

## What

The server refuses any request whose Host header is not `127.0.0.1:8100`, `localhost:8100` or the configured tailnet name (a fixed error text), so DNS rebinding can't reach it from a browser. Full path, high risk.

## Why

A malicious site can point its own domain at 127.0.0.1 and have my browser talk to the server as if it were that site. Checking the Host header against the known names closes that path; it belongs with the access token, which settles the tailnet name.
