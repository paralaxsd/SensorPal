# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
# Build the entire solution
dotnet build SensorPal.slnx

# Run the server (ASP.NET Core, targets net10.0-windows due to NAudio/WASAPI)
dotnet run --project src/SensorPal.Server

# Run the MAUI app targeting Windows
dotnet run --project src/SensorPal.Mobile -f net10.0-windows10.0.19041.0

# Deploy to a connected Android device (USB debugging enabled)
dotnet build -t:Install -f net10.0-android src/SensorPal.Mobile/SensorPal.Mobile.csproj -c Debug

# Create EF Core migration after schema changes
dotnet ef migrations add <MigrationName> --project src/SensorPal.Server

# Run tests (none currently exist)
dotnet test SensorPal.slnx
```

The solution uses `.slnx` format (the new XML-based solution file format introduced in .NET 9/10).

## Architecture

Three projects in `src/`:

- **SensorPal.Server** — ASP.NET Core Web API (targets `net10.0-windows` for NAudio WASAPI). Runs as a long-lived server on the Windows PC with the audio interface. Core services:
  - `MonitoringStateService` — simple state machine (`Idle` ↔ `Monitoring`), manually toggled via API
  - `AudioCaptureService` (IHostedService) — captures from the Focusrite Scarlett via WASAPI (`NAudio.CoreAudioApi.WasapiCapture`); dual recording: background MP3 (compressed, continuous) + WAV clips around noise events with pre-roll buffer
  - `NoiseDetector` — RMS amplitude threshold detection in dBFS with debounce
  - `CircularAudioBuffer` — thread-safe ring buffer for pre-roll audio
  - SQLite via EF Core (`SensorPalDbContext`) — stores `MonitoringSession` and `NoiseEvent` entities; DB at `AudioConfig:StoragePath/sensorpal.db`
  - JSON serialization: source-generated `AppJsonSerializerContext` (non-AOT now but kept for consistency); add new DTO types there when adding endpoints
  - API docs via Scalar at `/scalar/v1` in Development

- **SensorPal.Mobile** — .NET MAUI app targeting Android, iOS, macCatalyst, and Windows. Three tabs: **Monitoring** (start/stop, live session stats, session history), **Events** (browse noise events by date, play back audio clips), and **Logs** (live in-app log viewer). `ConnectivityService` (singleton) pings the server periodically and shows an offline dialog via `AppShell`. `SensorPalClient` (in `Infrastructure/`) handles all HTTP communication. Platform-specific server URLs are set in embedded `appsettings.{Platform}.json` files — for a real Android device, update `appsettings.Android.json` with the PC's LAN IP. Audio playback via `Plugin.Maui.Audio`.

- **SensorPal.Shared** — Class library. Contains `NullabilityExtensions` (`.NotNull<T>()`) and shared DTOs in `Models/` (`NoiseEventDto`, `MonitoringSessionDto`, `LiveSessionStatsDto`).

### Key wiring points
- `AudioConfig:DeviceName` — empty string = use system default capture device. Set the actual device name via user secrets: `dotnet user-secrets set "AudioConfig:DeviceName" "Focusrite USB Audio" --project src/SensorPal.Server`. Enumerate available devices via `GET /audio/devices`.
- `AudioConfig:NoiseThresholdDb` (dBFS, e.g. `-30.0`) is the primary tuning knob — adjust empirically.
- `AudioConfig:StoragePath` — relative paths are resolved from the server executable directory (`AppContext.BaseDirectory`). Default is `recordings` (i.e. alongside the binary). The directory and SQLite DB are auto-created on startup.
- `ServerConfig:BaseUrl` drives the mobile → server connection; update `appsettings.Android.json` with the PC's LAN IP for physical device use.
- NAudio WASAPI types are in `NAudio.CoreAudioApi` (not `NAudio.Wave`) — requires the `NAudio.Wasapi` NuGet package separately from `NAudio`.
- New EF Core schema changes require a migration: `dotnet ef migrations add <Name> --project src/SensorPal.Server`.

## Git Workflow

- **Never commit without explicit approval** — after completing a task, present a summary of changes and wait for the user to say "commit" (or similar) before running `git commit`.
- **Never push without explicit approval** — same rule applies to `git push`.

## Build & CI

- When working with Nuke build automation, always use the attribute-based approach (e.g., `[GitHubActions]` attributes) to generate CI YAML files. Never manually edit Nuke-generated YAML files directly.

## Tech Stack Notes

This is a .NET MAUI / .NET ecosystem project. Be aware of:
- **AOT compilation constraints** — not all reflection-heavy APIs are AOT-safe; check AOT compatibility first when debugging unexplained runtime failures.
- **MAUI threading requirements** — UI updates must run on the main thread; use `Dispatcher.Dispatch` / `MainThread.BeginInvokeOnMainThread`.
- **Platform-specific API differences** — behavior on Android vs. Windows can differ significantly; check platform first when debugging.
- When debugging, check AOT compatibility and threading before other hypotheses.

## Nuke Build

The solution uses [Nuke](https://nuke.build) as its build automation tool. The build project lives in `build/SensorPal.Build.csproj` and is invoked via the bootstrapper scripts in the repo root. No global Nuke installation required — just a working .NET SDK.

```powershell
# Windows (PowerShell)
.\build.ps1               # default target: Compile (runs Restore → Compile)
.\build.ps1 Clean
.\build.ps1 Test
.\build.ps1 DeployAndroid # builds APK and installs on a connected Android device via ADB
```

```bash
# Linux/macOS/Git Bash
./build.sh
./build.sh Clean
./build.sh Test
./build.sh DeployAndroid
```

Pass `--configuration Release` to override the default (`Debug` for local builds):

```powershell
.\build.ps1 DeployAndroid --configuration Release
```

**Notes:**
- `DeployAndroid` requires a physical Android device connected via USB with USB debugging enabled. ADB is resolved automatically through the Android SDK that Visual Studio installs.
- The build project targets `net10.0` because `Nuke.Common` 10.x ships only a `net10.0` lib. Do not downgrade this TFM.
- `.nuke/temp/` contains ephemeral build logs — excluded from source control.

## C# Style (from Copilot instructions)

- **Prefer conciseness**: `var`, expression-bodied members, file-scoped namespaces, primary constructors, pattern matching, collection expressions.
- **Visibility**: omit `private`; omit `internal` for nested types. Use `sealed` by default.
- **Member order**: fields → properties → constructors → methods; sorted public → internal → protected → private within each group; static members after all instance members. Fields: `_camelCase`; static/protected fields: `PascalCase`.
- **Prefer records** where appropriate; **prefer extension types** for utility helpers.
- **Minimize negations** in conditionals; prefer early returns.
- **Prefer functional expressions** (LINQ, etc.) over loops where readable.
- Max line length: **100 characters**.
- Use **strategic blank lines** to separate logical blocks, error handling, and return statements.

## MAUI Gotchas

- **`DisplayAlertAsync` on Shell**: Requires the Shell to be window-attached. Start background services from `OnAppearing()` (with a `_started` guard), not the constructor — otherwise the dialog silently returns `false` before the window is ready.
- **App quit on Android**: `Application.Current?.Quit()` doesn't reliably kill the process. Use `Environment.Exit(0)`.
- **Splash screen `BaseSize` on Android 12+**: Has no effect on visual icon size — Android constrains it to ~240dp regardless. Only the background `Color` fills the screen.
- **System font names in `FontFamily`**: Names like `"Courier New"` require the font to be registered as a `<MauiFont>` asset. Omit `FontFamily` and let Grid column widths handle alignment instead.
- **DI accessibility**: Top-level service classes injected into `public` constructors must themselves be `public` (C# CS0051).
- **`AppShell` in DI**: Register as singleton; `App` receives it via constructor injection because `UseMauiApp<App>()` registers `App` in DI automatically.
- **`InMemoryLoggerProvider` pattern**: Create the store before `builder.Build()`, share via `builder.Services.AddSingleton(logStore)` + `builder.Logging.AddProvider(new InMemoryLoggerProvider(logStore))`.
