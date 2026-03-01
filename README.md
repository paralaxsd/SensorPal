# SensorPal [![ci-android](https://github.com/paralaxsd/SensorPal/actions/workflows/ci-android.yml/badge.svg)](https://github.com/paralaxsd/SensorPal/actions/workflows/ci-android.yml) [![tests](https://github.com/paralaxsd/SensorPal/actions/workflows/tests.yml/badge.svg)](https://github.com/paralaxsd/SensorPal/actions/workflows/tests.yml)

Night-time noise monitoring for Windows + Android. Captures audio from a USB audio interface, detects noise events above a configurable threshold, records WAV clips with pre/post-roll, and lets you browse and play back events from a MAUI mobile app.

## Prerequisites

| Component | Requirement |
|---|---|
| PC | Windows 10/11, .NET 10 SDK |
| Audio interface | Focusrite Scarlett (or any WASAPI-compatible USB interface) |
| Phone | Android 5.0+ |
| Network | Phone and PC on the same LAN |

---

## Server Setup (Windows PC)

### 1. Configure the audio device

If your audio interface is already the **Windows default input device**, you can skip this step — the server will use it automatically.

Otherwise, find the exact device name: run the server, open `http://localhost:50194/scalar/v1` → `GET /audio/devices`, note the `name` of your input.

Store it as a user secret (never committed to git):

```
dotnet user-secrets set "AudioConfig:DeviceName" "Analogue 1 + 2 (Focusrite USB Audio)" --project src/SensorPal.Server
```

### 2. Configure Windows audio

In **Control Panel → Sound → Recording** → your Focusrite device → Properties → **Enhancements**:
- Disable **Automatic Gain Control**
- Disable all other audio processing

Recommended format: **1 channel, 16-bit or 32-bit, 48000 Hz**. The server auto-detects the format.

### 3. Run the server

```
dotnet run --project src/SensorPal.Server
```

The server starts on `http://localhost:50194`. The SQLite database and recordings are created in `src/SensorPal.Server/recordings/` by default.

### 4. Note the API key

On the very first start, the server generates a random API key and prints it to the console:

```
warn: SensorPal.Server.Storage.SettingsRepository[0]
      API Key generated — copy this into the mobile app Settings: <your-key-here>
```

Copy this key — you will need it in the mobile app. The key is stored in the SQLite database and is not regenerated on subsequent restarts.

---

## Mobile App Setup (Android)

### 1. Enter the API key

Open the app → **Settings** tab → scroll to **Security** → paste the key from the server console → **Save**.

All API calls include this key as `Authorization: Bearer <key>`. The `/status` endpoint is intentionally public (used for the connectivity check).

### 2. Set the server address

Edit `src/SensorPal.Mobile/appsettings.Android.json` and replace `192.168.1.X` with your PC's LAN IP (`ipconfig` → IPv4 Address):

```json
{
  "ServerConfig": {
    "BaseUrl": "http://192.168.1.X:50194"
  }
}
```

Alternatively, skip this step and set the server URL at runtime in the app under **Settings → Server URL**.

### 3. Deploy to device

Enable **USB Debugging** on the phone: Settings → About → tap Build Number 7× → Developer Options → USB Debugging.

Verify the device is visible (no need to know where ADB is installed):

```powershell
.\build.ps1 ListAndroidDevices
```

Then deploy:

```powershell
.\build.ps1 DeployAndroid --configuration Release
```

If multiple devices are connected, pass the ID from `ListAndroidDevices`:

```powershell
.\build.ps1 DeployAndroid --configuration Release --deviceId emulator-5554
```

---

## Daily Use

1. **Start the server** on the PC (`dotnet run --project src/SensorPal.Server`)
2. **Open SensorPal** on the phone — the status indicator shows the server connection
3. Go to the **Monitoring** tab → tap **Start Monitoring**
4. The live input meter shows the current dBFS level in real time
5. Noise events are automatically recorded; tap **Stop Monitoring** when done
6. Browse recorded events in the **Events** tab — tap any event to play the audio clip

---

## Tuning the Threshold

The key setting is `AudioConfig:NoiseThresholdDb` (default: **-30.0 dBFS**).

Use the live level meter on the Monitoring tab to calibrate:

- Watch the baseline level during silence → that is your noise floor
- Set the threshold a few dB above the noise floor
- Typical values: `-40` (sensitive) to `-20` (loud events only)

Set via user secrets:

```
dotnet user-secrets set "AudioConfig:NoiseThresholdDb" "-35" --project src/SensorPal.Server
```

Server restart required after changes.

---

## Configuration Reference

All settings live in `src/SensorPal.Server/appsettings.json` or as user secrets.

| Key | Default | Description |
|---|---|---|
| `AudioConfig:DeviceName` | *(system default)* | Substring match against WASAPI device name |
| `AudioConfig:NoiseThresholdDb` | `-30.0` | Detection threshold in dBFS |
| `AudioConfig:PreRollSeconds` | `30` | Seconds of audio buffered before an event |
| `AudioConfig:PostRollSeconds` | `30` | Seconds recorded after an event ends |
| `AudioConfig:BackgroundBitrate` | `64` | Background MP3 bitrate (kbps) |
| `AudioConfig:ClipBitrate` | `128` | Event clip MP3 bitrate (kbps) |
| `AudioConfig:StoragePath` | `recordings` | Where recordings and the SQLite DB are stored |
| `ServerConfig:BaseUrl` | `http://localhost:50194` | Used by the mobile app to reach the server |

---

## API (Development)

Interactive API docs at `http://localhost:50194/scalar/v1` when running in Development mode.

Notable endpoints:

| Endpoint | Description |
|---|---|
| `GET /audio/devices` | List available WASAPI capture devices |
| `GET /monitoring/level` | Current input level in dBFS (live) |
| `POST /monitoring/start` | Start a monitoring session |
| `POST /monitoring/stop` | Stop the current session |
| `GET /monitoring/sessions` | List all sessions |
| `GET /events?date=YYYY-MM-DD` | Events for a given date |
| `GET /events/{id}/audio` | Download the WAV clip for an event |

---

## Building & Development

The solution uses [Nuke](https://nuke.build) as its build automation tool. No global installation required — the bootstrapper scripts in the repo root handle everything.

### Targets

| Target | Description |
|---|---|
| `Compile` *(default)* | Restore packages and build the entire solution |
| `Clean` | Remove all build outputs |
| `Test` | Build and run all test projects |
| `PublishAndroid` | Build a Release Android APK → `artifacts/android/` |
| `PublishServer` | Publish a self-contained server build → `artifacts/server/` |
| `DeployAndroid` | Build and install the Android app on a connected device via ADB |
| `ListAndroidDevices` | List all Android devices currently visible to ADB |

### PowerShell (Windows)

```powershell
.\build.ps1                                          # Compile (default)
.\build.ps1 Clean
.\build.ps1 Test
.\build.ps1 PublishServer
.\build.ps1 PublishAndroid
.\build.ps1 DeployAndroid                            # Deploy Debug to default device
.\build.ps1 DeployAndroid --configuration Release    # Release build
.\build.ps1 DeployAndroid --aot                      # AOT compilation
.\build.ps1 DeployAndroid --device-id emulator-5554  # Target a specific device
.\build.ps1 ListAndroidDevices                       # Show connected devices
```

### Bash (Linux / macOS / Git Bash)

```bash
./build.sh          # Compile
./build.sh Test
./build.sh PublishServer
```

### Plain dotnet

You can also work without Nuke for the server and tests:

```bash
dotnet build SensorPal.slnx
dotnet test SensorPal.slnx
dotnet run --project src/SensorPal.Server
```
