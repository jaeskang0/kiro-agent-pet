# Kiro Agent Pet

Read `README.md` for supported behavior and `docs/architecture.md` for module ownership and runtime flow. This is an unofficial C# and Avalonia desktop companion for Kiro IDE and Kiro CLI V3. Windows is the tested platform. Herdr has a separate pet; this app excludes Herdr-hosted sessions.

## Code map

- `src/KiroPet.Core` owns native Kiro session discovery, status priority, acknowledgements, IDE and CLI navigation, phrase packs, persistent settings, and installation.
- `src/KiroPet.Desktop` owns the floating window, sprites, animation, bubble, menus, and the session picker.
- `src/KiroPet.Cli` owns the `kiro-pet` commands.
- `extensions/kiro-agent-pet-bridge` owns exact Kiro IDE chat opening and focus acknowledgements.
- `tests/KiroPet.Checks` covers core behavior and compatibility. `tools/KiroPet.Package` builds the portable Windows ZIP.

## Compatibility

- The product and repository are named Kiro Agent Pet. Keep `kiro-pet` commands and the `%LOCALAPPDATA%\KiroPet` data folder compatible with existing installations. The app installs `jaeskang0.kiro-agent-pet-bridge` and removes the old extension ID after confirming the new one.
- Keep imported pets, generated phrase packs, saved positions, settings, and acknowledgements in the user data folder across updates. The only bundled pet is the original MIT-licensed Spark. Do not add imported or third-party pet artwork to releases without redistribution rights.
- The pet reads Kiro's native session files. It does not require Kiro task hooks. The IDE bridge opens an exact chat; Windows CLI navigation supports verified consoles and single-pane Windows Terminal tabs. Ambiguous targets stay unread.
- Priority is Needs input, error, unread Done, working, then idle. Repeated clicks cycle agents at the highest active priority. Idle hover has no bubble; an idle click shows a temporary phrase without opening an IDE.

## Verify changes

Run `dotnet build KiroPet.slnx -c Release`, `dotnet run --project tests/KiroPet.Checks -c Release --no-build`, and `npm --prefix extensions/kiro-agent-pet-bridge run check` for relevant changes. Package the bridge with `npm --prefix extensions/kiro-agent-pet-bridge run package` before making a Windows ZIP. The GitHub Actions workflow in `.github/workflows/checks.yml` is the release build reference.

Do not commit local `artifacts`, imported pets, user data, credentials, or downloaded pet packs. Preserve unrelated workspace changes.

## Working style

Keep changes focused on the requested task. Before meaningful edits, state the file and intended change. Report the result and relevant checks afterward. Show the command and relevant output when a check fails, with secrets redacted. Use concise, plain language.
