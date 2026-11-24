# Application structure

Kiroling uses C# and Avalonia. The PowerShell prototype is kept outside this repository as a local reference.

The shared core owns native session discovery, result priority, persisted settings, acknowledgements, the command protocol, and per-user installation. It does not depend on Avalonia. The desktop app owns sprites, frame pacing, speech bubbles, menus, and panels. The console app provides the `kiro-pet` commands and starts or controls the desktop process.

Two executable projects keep console output reliable for commands while avoiding a console window when the companion starts. A single executable with console attachment was considered, but it would put Windows console handling into the desktop startup path.

Native session data is read in the background inside the companion process. No Kiro hooks or extra watcher service are installed. The pet data folder stays separate from program files so updates can preserve imported characters, position, preferences, and per-turn acknowledgements.

The optional Kiro extension receives session-opening links from the desktop app and uses Kiro's internal editor-chat command. It confirms the exact active chat tab in a focused IDE window before replying. Per-request reply files and per-result viewed events stay in the user's pet data folder.

Kiro can run the agent and companion extension in separate processes. The bridge publishes its window ID and the agent port from Kiro's window registry, without recording registry credentials. On Windows, the desktop maps that port to its owning process and matches the native session lease. Requests identify the target window and agent connection, and replies identify the bridge process. Ambiguous window matches leave the result unread. This ownership mapping and IDE navigation currently support Windows only.

The desktop publishes current IDE states in `ide-status.json` for the bridge. This live view stays separate from the native reader's durable recovery cache, so one recovering session cannot leave another chat's focus acknowledgement stale. Read actions compare the completion identity against the current snapshot; opening or viewing an old result cannot acknowledge a newer turn.

The extension reports manual focus only for Kiro's custom chat editor tabs. Sidebar chat focus and exact CLI terminal navigation remain unsupported. The desktop continues to monitor both IDE and CLI statuses without the extension.

Windows overlay controls and sign-in startup are implemented separately from the shared core. Avalonia provides the cross-platform UI foundation; macOS and Linux runtime behavior still require validation.
