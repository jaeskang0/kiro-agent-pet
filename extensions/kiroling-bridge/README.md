# Kiro Agent Pet IDE Bridge

This companion extension lets the Kiro Agent Pet desktop pet open an exact Kiro IDE chat.

The desktop pet installs or updates this extension automatically when it starts. If Kiro is already running when the extension changes, reload its window once using **Ctrl+Shift+P → Developer: Reload Window**.

Clicking the pet or its bubble opens the selected IDE chat in an editor tab. Kiro Agent Pet marks that result read only after the requested chat tab is active in a focused Kiro window.

Manually focusing a Kiro chat editor tab also marks its completed result read. Sidebar chats do not expose the same reliable focus signal and are not automatically checked.

The extension does not send prompts or use Kiro command hooks. Existing IDE and CLI status tracking continues in the desktop app. CLI terminal navigation is not provided by this extension.

The integration uses Kiro's internal chat commands and was developed against Kiro 1.5.11. Unsupported command responses leave results unread.

Kiro Agent Pet is an independent project and is not affiliated with Kiro or AWS.
