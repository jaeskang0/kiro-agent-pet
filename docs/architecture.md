# Application structure

Kiro Agent Pet uses C# and Avalonia. The PowerShell prototype is kept outside this repository as a local reference.

The shared core owns native session discovery, result priority, persisted settings, acknowledgements, the command protocol, and per-user installation. It does not depend on Avalonia. The desktop app owns sprites, frame pacing, speech bubbles, the right-click controls, and the optional session list. The console app provides the `kiro-pet` commands and starts or controls the desktop process.

Two executable projects keep console output reliable for commands while avoiding a console window when the companion starts. A single executable with console attachment was considered, but it would put Windows console handling into the desktop startup path.

Native session data is read in the background inside the companion process. No Kiro hooks or extra watcher service are installed. The pet data folder stays separate from program files so updates can preserve imported characters, position, preferences, and per-turn acknowledgements.

`PetArtwork` retains import descriptions and character kinds and exports a neutral-background idle portrait. Both import entry points use the same desktop import method, which queues personality generation after selecting the pet. `PhraseGeneration` deduplicates per-pet jobs, serializes Kiro requests, and cancels owned work at shutdown. `KiroPhraseGenerator` invokes a fresh, noninteractive V3 chat using a local portrait-read profile. It reads the native `sessionUpdate` stream, accepting only agent message text, and validates the completed JSON before caching. Kiro CLI handles its own authentication; the pet does not copy credentials.

`PhrasePacks` validates the eight phrase categories and atomically stores complete packs by pet key. `PhraseSpeaker` caches the displayed line for a speech event, so the 200 ms status poll cannot cause message flicker. Generated/default preferences are per pet and survive restarts. Job workspaces under the pet's `phrase-jobs` directory are excluded both from native discovery and restored result caches, preventing generation from appearing as an agent completion. The existing status policy and animation row mappings remain independent.

The Kiro extension receives session-opening links from the desktop app and uses Kiro's internal editor-chat command. It confirms the exact active chat tab in a focused IDE window before replying. Per-request reply files and per-result viewed events stay in the user's pet data folder.

On Windows, the desktop checks the bundled VSIX identity and version against Kiro's installed extension list during background startup. Missing or older helpers are installed through Kiro's extension CLI; equal or newer helpers are left alone. Installation is verified by rereading the installed versions, so a failed or interrupted setup can be retried on the next startup. Helper updates prompt for a window reload only when Kiro is already running. Setup status and failures appear in connection health. No Kiro installation leaves CLI monitoring available.

Kiro can run the agent and companion extension in separate processes. The bridge publishes its window ID and the agent port from Kiro's window registry, without recording registry credentials. On Windows, the desktop maps that port to its owning process and matches the native session lease. Requests identify the target window and agent connection, and replies identify the bridge process. Ambiguous window matches leave the result unread. This ownership mapping and IDE navigation currently support Windows only.

The desktop publishes current IDE states in `ide-status.json` for the bridge. This live view stays separate from the native reader's durable recovery cache, so one recovering session cannot leave another chat's focus acknowledgement stale. Read actions compare the completion identity against the current snapshot; opening or viewing an old result cannot acknowledge a newer turn.

`SessionCursor` records the status and ID of the last agent clicked. Selection advances through stable IDs within the highest-priority non-idle status and wraps after the last agent. Priority changes reset the cursor. The desktop queues clicks while an opening is in progress, and the bubble identifies the next click's target. Dragging does not advance selection. An idle pet click plays its configured reaction and shows a locally selected phrase for four seconds without launching an application. The idle message deadline is cleared on dragging or task-status changes. Idle hover has no bubble handlers; the session picker's explicit Open session action remains available.

The extension reports manual focus only for Kiro's custom chat editor tabs. Sidebar chat focus remains unsupported. The desktop continues to monitor both IDE and CLI statuses without the extension.

`CliNavigator` handles Windows CLI opening independently of the IDE bridge. Native snapshots retain process start time and lease acquisition time alongside the PID. The navigator rechecks those identities before and after opening. For regular consoles and Windows Terminal, the navigator checks the backend and its validated ancestors until it finds a visible console. A backend started by Kiro can have its own invisible console, separate from the terminal's console. Windows Terminal tabs use a temporary console-title marker and native UI Automation to select a tab and confirm focus on its sole terminal control. Consoles shared by multiple live CLI sessions are rejected.

`ProcessAncestry` supplies host classification and the shared process validation. Herdr-hosted CLI sessions are cached as excluded host markers and omitted from native snapshots. Their event streams are not read. The session policy and navigator also reject Herdr markers. Kiroling does not invoke Herdr commands, focus its panes, or create tabs there; closed CLI chats reopen as standalone sessions.

A closed CLI result resumes by its exact session ID without submitting a prompt. The navigator waits for a stable new lease before selecting its terminal. Ambiguous panes and unsupported hosts return failure, preserving the unread result. Manual CLI focus detection is not implemented.

Windows overlay controls and sign-in startup are implemented separately from the shared core. Avalonia provides the cross-platform UI foundation; macOS and Linux runtime behavior still require validation.
