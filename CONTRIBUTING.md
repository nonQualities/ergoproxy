# Contributing to ERGOPROXY

Thank you for your interest in contributing to **ERGOPROXY**!

## Architecture Overview

ERGOPROXY is organized into three projects:

- `src/ErgoProxy.Core`: Core domain models, validation, credential vaults, proxy testing pipeline, and platform adapters.
- `src/ErgoProxy.Cli`: Console entry point, command handlers, and interactive Spectre.Console terminal UI.
- `tests/ErgoProxy.Tests`: xUnit acceptance and unit test suite covering criteria AT-01 through AT-15.

## Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) or later
- Supported OS: Linux (GNOME / KDE), Windows (desktop), or macOS

## Building

```bash
dotnet build ErgoProxy.sln
```

## Running Tests

```bash
dotnet test tests/ErgoProxy.Tests/ErgoProxy.Tests.csproj --verbosity normal
```

All 15 acceptance tests (`AT-01` to `AT-15`) and core unit tests must pass before opening a pull request.

## Code Standards

- Maintain zero warnings (`0 Warning(s), 0 Error(s)`).
- Never log, display, or commit plain-text passwords or authentication tokens.
- Keep external commands structured with argument lists; never construct unescaped shell strings.
- Platform-specific code must remain isolated behind `IPlatformAdapter` or `ICredentialStore`.
