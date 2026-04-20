# CowLauncher

Tiny Windows launcher for Cow Colony Sim.

## What it does

1. Queries GitHub Releases for the latest build.
2. Compares the remote tag/date against the local install (`%LOCALAPPDATA%\CowColonySim\install\version.txt`).
3. Shows both versions and an "Update" button if they differ.
4. On update: downloads the release zip, wipes the install dir, extracts, launches.

## Publish (manual)

```powershell
cd launcher
dotnet publish -c Release
# → launcher\bin\Release\net8.0-windows\win-x64\publish\CowLauncher.exe
```

Self-contained single-file exe (~70 MB compressed). No .NET runtime required on the target machine.
