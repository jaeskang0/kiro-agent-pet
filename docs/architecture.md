# Application structure

Kiro Pet uses C# and Avalonia. The PowerShell prototype is kept outside this repository as a local reference.

The shared core owns native session discovery, result priority, persisted settings, acknowledgements, the command protocol, and per-user installation. It does not depend on Avalonia. The desktop app owns sprites, frame pacing, speech bubbles, menus, and panels. The console app provides the `kiro-pet` commands and starts or controls the desktop process.

Two executable projects keep console output reliable for commands while avoiding a console window when the companion starts. A single executable with console attachment was considered, but it would put Windows console handling into the desktop startup path.

Native session data is read in the background inside the companion process. No Kiro hooks or extra watcher service are installed. The pet data folder stays separate from program files so updates can preserve imported characters, position, preferences, and per-turn acknowledgements.

Windows overlay controls and sign-in startup are implemented separately from the shared core. Avalonia provides the cross-platform UI foundation; macOS and Linux runtime behavior still require validation.
