# Kiroling

An unofficial animated desktop companion for Kiro IDE and Kiro CLI V3. It watches each native session automatically and shows Needs input, Blocked/error, unread Done, Working, or Idle. No Kiro hooks or manual workspace connection are required. Kiroling is an independent project and is not affiliated with Kiro or AWS.

Kiroling is written in C# with Avalonia. Windows has been tested. Avalonia also runs on macOS and Linux, but overlay behavior, shortcuts, application focus, and native session compatibility on those systems still need verification.

## Run on Windows

1. Download [kiroling-0.3.0-windows.zip](https://github.com/jaeskang0/kiroling/releases/download/v0.3.0/kiroling-0.3.0-windows.zip) from the [Kiroling v0.3.0 for Windows release](https://github.com/jaeskang0/kiroling/releases/tag/v0.3.0).
2. Extract the ZIP.
3. Double-click **`KiroPet.Desktop.exe`** to launch the pet.

No terminal or installation is needed for this portable mode. Keep the extracted folder wherever you want to run Kiroling from. Your pets and settings are saved separately and persist across launches.

`kiro-pet.exe` is the optional command-line tool. Use `KiroPet.Desktop.exe` when launching from File Explorer.

## Optional installation on Windows

To copy Kiroling to a permanent per-user location and add its terminal commands, open a terminal in the extracted folder and run:

```powershell
.\kiro-pet.exe install
```

The command copies the app to `%LOCALAPPDATA%\Programs\KiroPet`, adds `kiro-pet` to your user PATH, and keeps imported pets and settings in `%LOCALAPPDATA%\KiroPet`. Open a new terminal, then run:

```powershell
kiro-pet start
```

Installation and updates are per-user and do not need administrator rights. To update, extract a newer package and run its `kiro-pet.exe install` command. A graphical installer and automatic update checks are not available yet.

Kiroling uses the `kiro-pet` command and `KiroPet` data folder in this version to preserve compatibility with existing installations.

## Use it

The pet stays visible while Kiro is minimized or closed. Drag it to move it. Double-click to open the selected IDE workspace. Right-click for pet selection, ZIP or folder import, settings, the session picker, connection health, animation preview, and Quit. Windows+Alt+P opens the session picker if the shortcut is available.

| Command | Action |
| --- | --- |
| `kiro-pet start` / `stop` | Start one pet or exit it |
| `kiro-pet show` / `hide` | Show the pet or hide its window while tracking continues |
| `kiro-pet status` / `sessions` | Show process health or session status |
| `kiro-pet tray` | Open the session picker |
| `kiro-pet pets` / `select <name>` | List installed characters or switch instantly |
| `kiro-pet import "<ZIP-or-folder>"` | Import and select a Codex pet |
| `kiro-pet settings` | Open animation and startup settings |
| `kiro-pet startup on/off/status` | Manage optional start at sign-in on Windows |
| `kiro-pet uninstall` | Remove the command and sign-in startup |

The app reads Kiro's native event files in the background. It does not start a command for each task event. Each session has its own state. The pet prioritizes Needs input, Blocked/error, unread Done, Working, then Idle. A completion or terminal error stays unread until you mark that result checked in the session picker. Checking one session leaves the others unread.

The picker shows native chat titles. Opening an IDE workspace cannot yet select an exact chat. Opening a CLI session's terminal is also not implemented. CLI V2 is not supported. Ordinary conversational questions are shown as Needs input only when Kiro emits a structured pending interaction.

## Characters and animation

Spark is included. Import a compatible Codex pet ZIP or a folder containing `pet.json` and its sprite sheet. Version 1 (8×9) and version 2 (8×11) sheets are supported, with 192×208 pixel cells. Imported WebP sheets become local transparent PNGs. The pet library stays outside the installed program.

Settings offers a playback speed slider from 0.10× to 2.00×, an independent action duration slider, reduced motion, and animation choices for each status. Action choices are saved per character. The bubble has one position per character, based on visible pixels across its frames, so switching actions does not move it. The pet remains visible and rests when there is no active task.

## Build from source

Install the .NET 10 SDK, then run:

```powershell
dotnet build KiroPet.slnx
dotnet run --project tests/KiroPet.Checks
dotnet run --project src/KiroPet.Desktop
```

The CLI and desktop are separate executables. For a development CLI command, set `KIRO_PET_DESKTOP` to the built desktop executable and run `dotnet run --project src/KiroPet.Cli -- start`.

The [architecture notes](docs/architecture.md) explain ownership of session state, graphics, commands, and settings. The [Windows checks](.github/workflows/checks.yml) build, verify, and package the app. Imported pets and settings are never placed in a release archive.

## Data and removal

`KIRO_PET_HOME` selects a different pet data folder. `KIRO_NATIVE_HOME` selects a different Kiro session store; the default is `~/.kiro`. Settings, selected pet, position, acknowledgements, and local session summaries live under `%LOCALAPPDATA%\KiroPet` on Windows.

`kiro-pet uninstall` removes the command and sign-in startup. It keeps imported pets, settings, and old installed version folders so they can be recovered. Delete those folders manually only after confirming you no longer need them.

## License

Kiroling's code and original Spark artwork use the [MIT license](LICENSE). Imported artwork keeps its original license. See [third-party notices](THIRD-PARTY.md).
