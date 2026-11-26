# Kiro Agent Pet

An unofficial animated desktop companion for Kiro IDE and Kiro CLI V3. It watches each native session automatically and shows Needs input, Blocked/error, unread Done, Working, or Idle. No Kiro hooks or manual workspace connection are required. Kiro Agent Pet is an independent project and is not affiliated with Kiro or AWS.

Kiro Agent Pet is written in C# with Avalonia. Windows has been tested. Avalonia also runs on macOS and Linux, but overlay behavior, shortcuts, application focus, and native session compatibility on those systems still need verification.

## Run on Windows

1. Download [kiro-agent-pet-0.4.2-windows.zip](https://github.com/jaeskang0/kiro-agent-pet/releases/download/v0.4.2/kiro-agent-pet-0.4.2-windows.zip) from the [Kiro Agent Pet v0.4.2 for Windows release](https://github.com/jaeskang0/kiro-agent-pet/releases/tag/v0.4.2).
2. Extract the ZIP.
3. Double-click **`KiroPet.Desktop.exe`** to launch the pet.

No terminal or installation is needed for this portable mode. Keep the extracted folder wherever you want to run Kiro Agent Pet from. Your pets and settings are saved separately and persist across launches.

`kiro-pet.exe` is the optional command-line tool. Use `KiroPet.Desktop.exe` when launching from File Explorer.

## Optional installation on Windows

To copy Kiro Agent Pet to a permanent per-user location and add its terminal commands, open a terminal in the extracted folder and run:

```powershell
.\kiro-pet.exe install
```

The command copies the app to `%LOCALAPPDATA%\Programs\KiroPet`, adds `kiro-pet` to your user PATH, and keeps imported pets and settings in `%LOCALAPPDATA%\KiroPet`. Open a new terminal, then run:

```powershell
kiro-pet start
```

Installation and updates are per-user and do not need administrator rights. To update, extract a newer package and run its `kiro-pet.exe install` command. A graphical installer and automatic update checks are not available yet.

Kiro Agent Pet uses the `kiro-pet` command and `KiroPet` data folder to preserve existing installations. The IDE bridge keeps its original extension ID so existing Kiro connections continue to work.

## Use it

The pet stays visible while Kiro is minimized or closed. Drag it to move it; dragging never opens an agent or dismisses a result. Idle hover stays quiet. Clicking while idle plays the chosen reaction and shows a locally selected message for four seconds without opening an application. Starting a drag or changing task status clears the temporary idle message. For active or unread work, click the pet or its bubble to open the selected IDE chat or supported Windows CLI terminal. The pet checks only that result after confirming the exact target is visible. If opening fails, the result stays unread and its session row remains available for manual checking.

Right-click for pet selection and import, agents and history, animation speed, action duration, actions by status, reduced motion, start at sign-in, position reset, and connection health. Hover over Animation speed or Repeat each action to reveal its slider. Changes preview and save immediately; both menu rows show the current values. The separate session list remains available from the Agents submenu or Windows+Alt+P.

Click reaction lets you choose one idle-click animation per pet. Waving is the default. Selecting a reaction saves it and previews it immediately when reduced motion is off. Clicking during active or unread work still opens the relevant agent.

### Pet messages

Importing a pet automatically queues a personality phrase pack when generated messages are enabled for that pet. Kiro receives the pet's name, description, kind, and a static portrait extracted from its sprite sheet. The pet remains usable with built-in messages while generation runs. A configured Kiro CLI is required; missing Kiro, failed requests, and invalid replies leave the built-in messages available.

Generation runs in a separate private workspace using Kiro CLI V3's background mode and a local profile with only read access to the prepared portrait. It does not send prompts into your existing chats. Generator sessions are excluded from the pet's status, notifications, and session list. One job runs at a time; closing the pet cancels its jobs.

The **Messages** submenu shows generation status and offers **Use generated messages**, **Use default messages**, and **Regenerate messages**. Choices are saved per pet. Packs are stored locally under `%LOCALAPPDATA%\KiroPet\personalities` and work without Kiro running. Reimporting the same pet reuses its valid pack. Regeneration replaces a pack only after a complete, valid reply.

Phrases are chosen locally when a status or interaction changes, avoiding immediate repeats. Status polling does not continually rotate the bubble text. Generated lines are character-inspired, not verified quotations, and chat titles and counts still come from real session data. The animation mappings, status priority, and completion acknowledgement rules are independent of generated speech.

### Open IDE chats

On Windows, Kiro Agent Pet checks its bundled Kiro helper automatically at startup. It installs a missing helper or updates an older version, and leaves an equal or newer version alone. If the helper changes while Kiro is open, Kiro Agent Pet asks you to reload those IDE windows using **Ctrl+Shift+P → Developer: Reload Window**. Pet updates that do not change the helper require no helper reinstall. Setup runs in the background; **Connection health…** shows its progress or errors. Restart Kiro Agent Pet to retry a failed setup or after installing Kiro IDE.

On the first chat-opening request, Kiro asks whether the bridge may open the link. Choose **Open**; select **Do not ask me again for this extension** for subsequent pet clicks to open directly.

The bridge opens the selected chat in a Kiro editor tab. Manually focusing a chat editor tab also marks that session's completed result read. Sidebar chats are not automatically checked because they do not expose the same reliable focus signal. A failed opening or a stale completion reply leaves the result unread.

| Command | Action |
| --- | --- |
| `kiro-pet start` / `stop` | Start one pet or exit it |
| `kiro-pet show` / `hide` | Show the pet or hide its window while tracking continues |
| `kiro-pet status` / `sessions` | Show process health or session status |
| `kiro-pet tray` | Open the session picker |
| `kiro-pet pets` / `select <name>` | List installed characters or switch instantly |
| `kiro-pet import "<ZIP-or-folder>"` | Import and select a Codex pet |
| `kiro-pet settings` | Open the pet's right-click menu |
| `kiro-pet startup on/off/status` | Manage optional start at sign-in on Windows |
| `kiro-pet uninstall` | Remove the command and sign-in startup |

The app reads Kiro's native event files in the background. It does not start a command for each task event. Each session has its own state. The pet prioritizes Needs input, Blocked/error, unread Done, Working, then Idle. When several agents share the highest-priority non-idle status, each pet or bubble click opens the next agent. The bubble shows the next target. A completion or terminal error stays unread until you view its IDE chat or mark that result checked in the session picker. Checking one session leaves the others unread.

The picker shows native chat titles and retains checked results. Old completed chats remain unread until you check them; there is no age cutoff or timeout. Exact IDE chat opening requires the bridge. CLI V2 is not supported. Ordinary conversational questions are shown as Needs input only when Kiro emits a structured pending interaction.

### Open CLI terminals on Windows

CLI opening supports regular console windows and Windows Terminal tabs containing one terminal pane. Kiro Agent Pet matches the native session lease to its process and verifies the selected pane or tab. If Kiro's backend has no console window, Kiro Agent Pet follows its process chain to the visible terminal. It restores minimized windows. Windows Terminal tab selection briefly changes the console title to a unique marker and restores the original title afterward.

Closed CLI chats reopen in a standalone console with `kiro-cli chat --resume-id` in their saved workspace, without sending a prompt. Kiro's usual workspace trust checks still apply.

Herdr-hosted CLI agents are excluded from Kiro Agent Pet's alerts, click cycling, and session picker. Host markers are cached so identified Herdr sessions remain excluded after they close or Kiro Agent Pet restarts. Kiro's saved chats remain untouched.

Windows Terminal split panes, fixed tab titles that suppress application titles, consoles shared by multiple live CLI chats, and IDE-integrated terminals are not yet supported. Those results stay unread when Kiro Agent Pet cannot verify their target. Manually focusing a CLI terminal does not automatically check its result.

## Characters and animation

Spark is included. Import a compatible Codex pet ZIP or a folder containing `pet.json` and its sprite sheet. Version 1 (8×9) and version 2 (8×11) sheets are supported, with 192×208 pixel cells. Imported WebP sheets become local transparent PNGs. The pet library stays outside the installed program.

The right-click menu offers a playback speed slider from 0.10× to 10.00×, an independent action duration slider, reduced motion, and animation choices for each status. The nine standard sprite rows are Idle, Running right, Running left, Waving, Jumping, Failed, Waiting, Running, and Review. Each action uses the visible frames from its row. Version-2 pets also expose their two turn rows when present. Default task animations are Running for work, Waiting for input, Failed for errors, Jumping for unread Done, and Idle for rest. Actions by status lets you choose multiple actions, retaining at least one, and Preview animation plays a chosen action on the pet. Older saved action names are converted to the corresponding row names. Normal playback is 1.00× at 900 ms per frame, with finer slider control at slower speeds. Older saved speeds are converted once to preserve their actual playback pace. Action choices are saved per character. The bubble has one position per character, based on visible pixels across its frames, so switching actions does not move it. The pet remains visible and rests when there is no active task.

## Build from source

Coding agents should read [AGENTS.md](AGENTS.md) and the [architecture notes](docs/architecture.md) before changing the project.

Install the .NET 10 SDK, then run:

```powershell
dotnet build KiroPet.slnx
dotnet run --project tests/KiroPet.Checks
dotnet run --project src/KiroPet.Desktop
```

The CLI and desktop are separate executables. For a development CLI command, set `KIRO_PET_DESKTOP` to the built desktop executable and run `dotnet run --project src/KiroPet.Cli -- start`.

To build the IDE bridge, use Node.js 22 or later:

```powershell
cd extensions/kiroling-bridge
npm ci --ignore-scripts
npm run check
npm run package
```

The Windows release ZIP includes the bridge VSIX. The desktop checks and installs it automatically at startup. For a source build, place the packaged VSIX beside the desktop executable as `kiroling-bridge.vsix`, or set `KIRO_PET_IDE_VSIX` to its path.

The [architecture notes](docs/architecture.md) explain ownership of session state, graphics, commands, and settings. The [Windows checks](.github/workflows/checks.yml) build, verify, and package the app. Imported pets and settings are never placed in a release archive.

## Data and removal

`KIRO_PET_HOME` selects a different pet data folder. `KIRO_NATIVE_HOME` selects a different Kiro session store; the default is `~/.kiro`. Settings, selected pet, position, acknowledgements, and local session summaries live under `%LOCALAPPDATA%\KiroPet` on Windows.

`kiro-pet uninstall` removes the command and sign-in startup. It keeps imported pets, settings, and old installed version folders so they can be recovered. Delete those folders manually only after confirming you no longer need them.

## License

Kiro Agent Pet's code and original Spark artwork use the [MIT license](LICENSE). Imported artwork keeps its original license. See [third-party notices](THIRD-PARTY.md).
