# 010 AC-1 test without reflection

As the user, I want the listen-address test to stop reading a private Kestrel property, so that a .NET update can't break it for no real reason.

## What

The listen-address test checks `127.0.0.1:8100` without reading a non-public Kestrel property (still no real port).

## Why

The test that checks the server listens only on `127.0.0.1:8100` uses reflection on a non-public property, which can change in any .NET release. The code reviewer of job 125 noted it; the fix must still open no real port.
