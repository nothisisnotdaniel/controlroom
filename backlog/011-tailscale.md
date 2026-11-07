# 011 Tailscale

As the user, I want to reach ControlRoom on my iPhone over Tailscale, so that I can use it away from the Mac without exposing it to the internet.

## What

Install Tailscale on the Mac and the iPhone and sign in to the same account, run `tailscale serve --bg 8100` on the Mac, then open the `https://<mac-name>.<tailnet>.ts.net` address on the iPhone and add it to the home screen. Set the Mac to stay awake (or wake for network access) while you are away.

## Why

Only I can do this: it needs my accounts on both devices and settings on the Mac. Until it's done, ControlRoom works only on the Mac itself.
