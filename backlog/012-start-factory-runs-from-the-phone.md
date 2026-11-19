# 012 Start factory runs from the phone

As the user, I want to start a factory run from my phone, so that I can kick off work while I'm away from the Mac.

## What

A button that starts `/factory auto --project <P>` on the Mac and shows that it is running. This runs work on the Mac from the phone, so plan it in detail first (fixed command only, one run per project, how it is stopped); Remote Control already covers this today.

## Why

Starting `/factory auto` from the phone means the phone can run work on the Mac, which needs careful limits (a fixed command, one run per project, a clear way to stop it). Remote Control already covers this today, so it should only be built with a detailed plan.
