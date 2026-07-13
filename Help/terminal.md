# Terminal Mode

My News Feeder includes a terminal interface for keyboard-driven navigation and management of your feeds.

## Opening the Terminal

Terminal Mode can be opened from the main window via Help → Terminal (Preview).

The terminal opens as a separate window, and the main GUI is automatically hidden. Close the terminal to restore the GUI.

## Terminal Settings

Open Settings → Terminal (Preview) to configure:

- Whether future app launches start in Terminal Mode
- The default terminal theme
- Whether the boot animation plays when the terminal opens

The `startup`, `theme`, and `bootanim` terminal commands update the same saved settings.

Auto-refresh can also be configured directly in the terminal. These commands update the same saved refresh settings as the GUI.

## Basic Commands

| Command | Description |
|---------|-------------|
| `help` | Show all available commands |
| `browse` | Enter browse mode to navigate feeds and articles |
| `clear` / `cls` | Clear the terminal output |
| `close` / `exit` / `quit` | Close the terminal window |
| `bootanim on` | Enable the animated terminal boot screen |
| `bootanim off` | Disable the animated terminal boot screen |
| `bootanim preview` | Play the boot animation without restarting |
| `startup terminal` | Start future app launches in terminal mode |
| `startup gui` | Start future app launches in the GUI |
| `refresh status` | Show auto-refresh settings |
| `refresh auto on` / `refresh auto off` | Enable or disable auto-refresh |
| `refresh interval <minutes>` | Use interval mode and set the refresh interval |
| `refresh live on` / `refresh live off` | Enable or disable live refresh mode |
| `refresh live interval <10\|30\|60>` | Use live mode and set the refresh interval |
| `?` | Show help |

## Feed Management Commands

| Command | Description |
|---------|-------------|
| `list feeds` | List all available feeds |
| `list articles` | List articles from the current app selection |
| `list unread` | List unread articles from the current app selection |
| `feed <number\|name>` | Select a feed by number or name |
| `refresh` | Refresh all feeds |
| `refresh status` | Show current auto-refresh mode and interval |
| `feed Heise` | Select feed starting with "Heise" (tab-completion works) |

## Article Commands

| Command | Description |
|---------|-------------|
| `open <number>` | Open the article summary in the terminal |
| `read <number>` | Load Reader Mode text for the article |
| `reader <number>` | Same as `read <number>` |
| `unread <number>` | Mark article as unread |
| `search <text>` | Search for articles containing text |

## Browse Mode (Interactive Navigation)

Type `browse` to enter browse mode. Use keyboard shortcuts:

| Key | Action |
|-----|--------|
| `↓` / `↑` | Next / previous article |
| `PageDown` / `PageUp` | Jump through articles |
| `Home` / `End` | First / last article |
| `←` / `→` | Previous / next feed |
| `Ctrl+Home` / `Ctrl+End` | First / last feed |
| `Enter` | Load selected article in reader |
| `O` | Open article in external browser |
| `J` / `K` | Scroll reader text down / up |
| `G` / `Shift+G` | Scroll reader text to top / bottom |
| `Space` | Toggle read/unread |
| `M` | Mark article as read |
| `U` | Mark article as unread |
| `Ctrl+L` | Copy article link |
| `R` | Refresh selected feed |
| `?` or `F1` | Show browse mode help |
| `Escape` / `q` | Exit browse mode (back to command mode) |

## Mouse Support in Browse Mode

- **Left-click on feeds**: Select feed
- **Left-click on articles**: Select article
- **Right-click on articles**: Toggle read/unread
- **Double-click**: Load article in reader
- **Text selection + release**: Automatically copies selected text to clipboard

## Text Selection and Copy

- **Command mode**: Select text in output area, release mouse button to copy
- **Browse mode**: Select text in reader pane, release mouse button to copy
- **Keyboard**: `Ctrl+C` copies selected text or current input
- Selected text is automatically copied to clipboard on mouse release

## Auto-Completion (Tab)

The terminal supports tab-completion for:

- **Commands**: Type part of a command (e.g., `lis`) + Tab → completes to `list`
- **Sub-commands**: Type `list fe` + Tab → completes to `list feeds`
- **Feed names**: Type `feed Ex` + Tab → completes to `feed ExampleRSS`
- **Themes**: Type `theme sol` + Tab → completes to `theme SolarizedDark`

If multiple matches are available, the terminal shows suggestions instead of cycling through them.

## Command History

- **↑ arrow**: Navigate through previous commands
- **↓ arrow**: Navigate through next commands (forward)
- **Ctrl+R**: Search command history using the current input, press again to continue searching

## Themes

Change the terminal appearance with different color themes:

| Command | Theme |
|---------|-------|
| `theme` | Show available themes |
| `theme default` | Classic green terminal |
| `theme crt` | Green phosphor CRT monitor |
| `theme amber` | Amber monochrome monitor |
| `theme dos` | MS-DOS blue terminal |
| `theme matrix` | Black background with Matrix-style green |
| `theme solarizeddark` | Solarized Dark color scheme |
| `theme dracula` | Dracula color scheme |
| `theme paper` | Light reading theme |

Themes are saved between sessions.

## Reader Mode

In reader mode, articles are displayed with metadata (title, feed, date) and full text. The content is cached for offline reading.

## Persistent Cache

The terminal stores local preferences and cache data for:
- Reader content for offline access
- Theme, boot animation, and font size preferences

Cache files are stored in: `%LocalAppData%\MyNewsFeeder\`

## Tips

1. Use **Tab** extensively for faster input
2. **Up/Down arrows** save typing repeated commands
3. **Text selection** automatically copies to clipboard
4. Press `?` or `F1` in browse mode to see all shortcuts
5. Use `feed <partial name>` + Tab for quick feed selection
6. The terminal is perfect for keyboard-only users
