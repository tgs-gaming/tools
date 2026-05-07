# MCP for Unity

This is an adapted version of the CoplayDev 'MCP for Unity' plugin, ported to Unity 2020.3. Original repo: https://github.com/CoplayDev/unity-mcp/tree/main

---

## Quick start
1. Open Window > MCP for Unity > Local Setup Window.
2. Install Python and/or uv/uvx if missing so the server can be managed locally.
3. Open Window > MCP for Unity > Toggle MCP Window.
4. Configure your AI client (Cursor, VS Code, OpenClaw, Claude Code).
5. Click “Start Server”.

---

## MCP Client Configuration
- Select Client: Choose your target MCP client (e.g., Cursor, VS Code, Windsurf, Claude Code).
- Per-client actions:
    - Cursor / VS Code / Windsurf:
        - Auto Configure: Writes/updates your config to launch the server via `uvx` with the current package version:
            - Command: uvx (or your overridden path)
            - Args: --from <git-url> mcp-for-unity
        - Manual Setup: Opens a window with a pre-filled JSON snippet to copy/paste into your client config.
        - Choose UV Install Location: If uv/uvx isn’t on PATH, select the executable.
        - A compact “Config:” line shows the resolved config file name once uv/server are detected.
    - Claude Code:
        - Register with Claude Code / Unregister MCP for Unity with Claude Code.
        - If the CLI isn’t found, click “Choose Claude Install Location”.
        - The window displays the resolved Claude CLI path when detected.
    - OpenClaw:
        - Uses `~/.openclaw/openclaw.json` and the `openclaw-mcp-bridge` plugin.
        - MCP for Unity writes `plugins.entries.openclaw-mcp-bridge.config.servers.unityMCP`.
        - OpenClaw follows the currently selected MCP for Unity transport (`HTTP` or `stdio`).
        - The bridge exposes a proxy tool such as `unityMCP__call`.

Notes:
- The UI shows a status dot and a short status text (e.g., “Configured”, “uv Not Found”, “Claude Not Found”).
- Use “Auto Configure” for one-click setup; use “Manual Setup” when you prefer to review/copy config.

---

## Script Validation
- Validation Level options:
    - Basic — Only syntax checks
    - Standard — Syntax + Unity practices
    - Comprehensive — All checks + semantic analysis
    - Strict — Full semantic validation (requires Roslyn)
- Pick a level based on your project’s needs. A description is shown under the dropdown.

---

## Troubleshooting
- Python or `uv` not found:
    - Help: [Fix MCP for Unity with Cursor, VS Code & Windsurf](https://github.com/CoplayDev/unity-mcp/wiki/1.-Fix-Unity-MCP-and-Cursor,-VSCode-&-Windsurf)
- Claude CLI not found:
    - Help: [Fix MCP for Unity with Claude Code](https://github.com/CoplayDev/unity-mcp/wiki/2.-Fix-Unity-MCP-and-Claude-Code)

---

## Tips
- Use Cmd+Shift+M (macOS) / Ctrl+Shift+M (Windows, Linux) to toggle the MCP for Unity window.
- Enable “Show Debug Logs” in the header for more details in the Console when diagnosing issues.

---
