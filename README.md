# Kiro Pet for Windows

An independent desktop companion for Kiro IDE and Kiro CLI V3. It automatically discovers native sessions across workspaces, including multiple chats in the same project. Herdr is not required.

This is a development build. It requires PowerShell 7.4 or later. Python and Pillow are required only for importing pet packs. A standalone Windows installer and automatic updates are not available yet.

Source repository: [jaeskang0/kiro-pet](https://github.com/jaeskang0/kiro-pet).

## Install and start

Keep the extracted project folder in a permanent location. Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1
```

Open a new terminal, then run:

```powershell
kiro-pet start
```

The pet discovers Kiro IDE and CLI V3 automatically. No workspace hook installation, manual connection command, or extension is required. It can recover an already-running turn when it starts.

For upgrades from the hook-based build, `Install.ps1` backs up and removes the pet's owned hooks. Kiro may retain old hooks in an existing chat until that chat reloads its hooks; open a new chat to stop those cached invocations.

The pet starts with Spark. Right-click to import a character, select an installed character, open Settings, or inspect Sessions.

For imports, install Python and then run `python -m pip install -r requirements.txt`.

## Control the pet

| Command | Behavior |
|---|---|
| `kiro-pet start` | Start one companion; repeated calls reuse it |
| `kiro-pet stop` | Exit the companion |
| `kiro-pet show` | Show the pet; start it if necessary |
| `kiro-pet hide` | Hide the character while status tracking continues |
| `kiro-pet select <name-or-key>` | Switch characters immediately |
| `kiro-pet import "<zip-or-folder>"` | Import and select a Codex pet |
| `kiro-pet pets` | List installed names and keys |
| `kiro-pet settings` | Open animation and startup settings |
| `kiro-pet status` | Show process and connection counts |
| `kiro-pet sessions` | List recorded sessions in the terminal |
| `kiro-pet connections` | List automatically discovered workspaces |
| `kiro-pet startup on/off/status` | Manage optional start at sign-in |

Windows+Alt+P shows the pet and opens its session picker. If another app owns the shortcut, commands still work.

Drag the pet to move it. Its position is saved. Double-click brings the selected IDE workspace forward. Right-click **Sessions** to choose another session or mark one completion checked.

The companion remains visible and rests when nothing is active. Closing IDE or CLI sessions does not exit the companion. Start at sign-in is off by default.

## Choose animations

Settings contains a playback slider from 0.10x to 2.00x. At 0.10x, frame playback is ten times slower than normal. A separate duration slider repeats each action for 5 to 120 seconds before moving to the next selected action. Complete animation loops finish before changing actions; a new task status takes effect immediately.

Choose one or more actions for Idle, Working, Needs input, Blocked/error, and unread Done. Assignments are saved per character. Restore defaults resets the selected character's assignments and the playback controls.

Reduced motion uses a static frame. This is a manual setting; automatic detection of the Windows accessibility preference is not implemented.

## Understand multiple sessions

Each native session has a separate state machine keyed by its session ID. One chat finishing cannot overwrite another chat's Working status. The session picker uses Kiro's saved chat titles.

Priority is **Needs input > Blocked/error > unread Done > Working > Idle**. If a completion takes priority while other sessions work, the bubble mentions the working count.

Done and terminal task errors stay until that result is explicitly marked checked in Sessions. Opening a workspace does not acknowledge a specific chat because this integration cannot select an exact IDE conversation. Completion identity comes from the native execution and turn IDs, so restarting the pet does not create a new completion.

Native lease and process checks identify closed or abandoned sessions. Unread results remain available after closing. Silence does not mean a task finished.

The reader runs inside the pet process. It reads appended native turn and approval records in the background, without launching a command hook or another process for each task event. It does not modify Kiro's session files.

## Limits

- CLI V3 uses the native session store. CLI V2 is not supported.
- Session schema `1.0.0`, data model `1`, is supported. Other schemas are reported in Connection health.
- The picker opens IDE workspaces, not exact chats. CLI terminal activation is not implemented.
- A tool error does not automatically mean the task is blocked. The connector does not infer an error from a recoverable tool failure.
- Needs input follows structured native pending interactions, including approvals. Ordinary conversational questions are not classified from their wording.
- The reader follows explicitly declared child streams. An unsupported future delegated-event format may require an adapter update.

## Diagnose connections

Right-click **Connection health** to inspect automatic discovery, startup recovery, missing files, and unsupported session schemas.

Settings, imported pets, acknowledgements, and logs live under `%LOCALAPPDATA%\KiroPet`, outside the project. `native-sessions.json` stores result summaries without prompts or tool contents. `app.stderr.log` contains renderer failures. Old hook logs are not used.

Set `KIRO_PET_HOME` to use an isolated pet data folder. Set `KIRO_NATIVE_HOME` to read a different Kiro data directory; the default is `%USERPROFILE%\.kiro`.

## Verify and package

```powershell
pwsh -NoProfile -File .\Verify.ps1
pwsh -NoProfile -File .\Verify.ps1 -Live
pwsh -NoProfile -File .\Verify-HookMigration.ps1
pwsh -NoProfile -File .\Verify-LiveCli.ps1
python .\Verify-Imports.py
pwsh -NoProfile -File .\Build-Package.ps1
```

The live UI check opens a temporary companion backed by isolated native session fixtures, operates its actual controls, then removes its temporary data. `Verify-LiveCli.ps1` runs a reply-only CLI task in a hook-free workspace to verify the compiled reader.

The development ZIP excludes personal pets, logs, and settings. Clean-machine installation testing and bundled dependency distribution are still pending. See [Artwork and dependencies](THIRD-PARTY.md).

## Uninstall

Run `pwsh -File .\Install.ps1 -Uninstall`. It removes the command, sign-in startup entry, and connected pet hooks. User pets and settings remain available.

## License

The source code and original Spark artwork use the [MIT license](LICENSE). Imported pets retain their original licensing terms.
