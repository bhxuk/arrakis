# What's new

## 1.4.2 (13 October 2026)

- **Glass window**: on Windows 11 the main Arrakis window now has a frosted-glass background, with your wallpaper
  showing softly through the sidebar and cards. Turn it off in Arrakis > Settings > Appearance.
- **New scroll bars** everywhere: slim, rounded and see-through, matching the rest of the app. They widen slightly
  when you hover them, and you can still click above or below to jump a page.
- **Smooth scrolling** in the main window: the mouse wheel glides to the new position instead of jumping. Galleries and
  lists inside a page scroll on their own first.

## 1.4.1 (13 October 2026)

An important fix if you hide the taskbar. Please update.

- **Fixed: the PC freezing and Explorer restarting with "Hide the Windows taskbar" on.** Arrakis 1.4.0 kept resetting
  each screen's space every two seconds, which Explorer kept undoing. The two fought, flooding Windows with change
  messages. Arrakis now switches on Windows' own *Automatically hide the taskbar* setting once and never touches screen
  space directly. Your original setting is put back when Arrakis exits.
- **Fixed: the taskbar on other screens not going away.** It's now hidden on screens with a dock and auto-hidden on the
  others, so every screen gets its full space. Turn on *A dock on every screen* to hide it everywhere.
- **Fixed: the taskbar flickering back** when screens changed. Arrakis now waits for screen changes to settle and leaves
  the taskbar alone while it rebuilds the docks.
- If Windows keeps bringing the taskbar back, Arrakis stops trying after a few attempts and tells you, rather than fight it.
- **Keep windows clear of the dock** now registers the dock with Windows as an app bar, the same way the taskbar
  reserves its space, so Windows manages it.
- Start > Arrakis > **Restore Windows taskbar** now also puts the auto-hide setting back and makes Windows recalculate
  every screen, which clears anything 1.4.0 left behind.

## 1.4.0 (13 October 2026)

**Dock**
- Much smoother: icons ease in and out every frame and grow on the graphics card, without the whole dock being redrawn.
  The dock window is now only as big as it needs to be, and it no longer scans for windows while the mouse is on it.
- Four hover effects: Magnify, Zoom, Lift or None, with a slider for how much.
- **Smooth animations** switch: turn it off and nothing moves at all.
- Five styles: Floating, Minimal pill, Islands, Edge bar and Compact bar.
- Six themes: Match Arrakis, Dark glass, Light glass, Solid, Clear and Accent, plus a see-through slider.
- Put the dock at the bottom, left or right of the screen.
- Optional dividers between pinned apps, open apps and folders.
- **Keep windows clear of the dock**: maximised windows stop at the dock.
- Previews, folder stacks and menus open beside the dock when it's on the left or right.

**Taskbar and multiple screens**
- Hiding the taskbar on every screen (this caused freezing on some PCs and was replaced in 1.4.1).
- New **Put the taskbar back** button in Arrakis > Dock. Restore Windows taskbar in the Start menu now fixes every screen too.

**Wallpaper**
- Browse thousands of wallpapers from Wallhaven: Top, Hot, Latest, Random and search, with categories, time ranges and
  a "Fits my screen" filter. Scroll to load more.
- Click one for a big preview, then set it on every screen or just one, save a copy to Pictures, or open it on Wallhaven.
- New mode: a popular Wallhaven wallpaper each day.
- Optional Wallhaven API key, stored encrypted for your Windows account.

**Quick notes**
- Notes are saved as .txt files in a folder you choose (Documents\Arrakis Notes to start with), kept up to date as you type.
- Folder button in the notes window and on the Quick notes page.
- Text files added to the folder become notes, and edits made in other apps come back in.
- Deleted notes go to the Recycle Bin.

**Network**
- The speed test now shows what the result means in transfer terms: MB per second, GB per minute and how long a 1 GB
  file takes to download and upload.

## 1.3.0 (13 October 2026)

Three new modules, so Arrakis now has ten.

**Quick controls** (Ctrl+Alt+Q)
- One panel with switches for Wi-Fi, Bluetooth, Do not disturb, dark mode, Night light and the microphone.
- Brightness sliders for your laptop screen and external monitors that support DDC/CI.
- Master volume, the output device (speakers, headphones and so on) and a volume slider and mute button for each app.
- Power mode chips: Battery saver, Balanced or Best performance.

**Quick notes** (Ctrl+Alt+N)
- A fresh note from anywhere with one shortcut. Notes save as you type.
- Search every note, pick from six colours, copy or delete.
- Pin any note to the desktop as a sticky you can edit in place.
- Notes show up in the command bar and are included in backups.

**Network**
- Speed test for download, upload, ping and jitter, with your recent results.
- A live ping graph with typical ping, jitter and packet loss, and a new Ping widget.
- Tips on what's slowing your connection: signal, band, channel congestion, link speed, cables, ping and packet loss.
- A scan of the devices on your network.
- Notifications when the internet drops and when it's back.

**Also**
- New command bar actions: Quick controls, New note, Run a speed test, dark mode, Do not disturb and mute.
- The tray menu has Quick controls, New note and Run a speed test.

## 1.2.1 (13 October 2026)

- **One-click updates everywhere**: the update button on the Home page now downloads and installs the new version
  itself instead of opening the download page. Arrakis closes, updates and reopens on its own.
- If automatic updates are off, the update notification installs it when you click it, and the tray menu
  gets an **Install update** item.
- Arrakis now checks for updates every 6 hours, not just when it starts, so PCs that stay on for days still get them.
- Fixed the Install button in Settings > Updates checking again instead of installing.

## 1.2.0 (13 October 2026)

Something new in every module, and a few things that work across all of them.

**Everywhere**
- **Command bar**: press Alt+Space to find apps, files, Windows settings and clipboard items, work out sums,
  start a focus session, switch power modes, load a widget layout or search the web.
- **Light theme** and **six accent colours**, or match Windows.
- **Automatic updates**: new versions install quietly and Arrakis reopens. Never during a focus session.
- **Backup**: export everything to one file and import it on another PC.

**Dock**
- Live window previews when you rest the mouse on an app, with a close button on each.
- Window counts under apps with more than one window open.
- Folder stacks, starting with Downloads. Drop any folder on the dock to add it.
- Drag an app, shortcut or file on to the dock to pin it.
- Optionally, a dock on every screen showing that screen's windows.

**Widgets**
- Seven new widgets: agenda, to-do list, quick launch, world clocks, photo frame, stocks and headlines from any news feed.
- Connect your calendar with an .ics link. Busy days are marked, and you get a heads-up 10 minutes before each event.
- Small, medium and large sizes, snap to grid, and saved layouts you can switch between.

**Wallpaper**
- Clouds, rain, snow, fog and storms appear when they're happening outside.
- The sun and moon move across the sky through the day.
- Your own photos, as a slideshow from a folder or one for each time of day.
- Each screen gets a picture drawn at its own size.

**Clipboard**
- Snippets with shortcodes that expand as you type in any app, with {date}, {time} and {clipboard}.
- Paste as UPPERCASE, lowercase, Title Case, Sentence case, one line, list to commas and back, sorted, without duplicates,
  or with link tracking removed.
- Collections, to group items into your own lists.
- Card numbers, bank details and passwords are hidden and deleted after a minute.
- Encrypted sync between PCs through a shared folder.

**Focus**
- Keep distracting apps away with a nudge, or minimise them straight away.
- Add what you're working on, shown on the pill and saved with each session.
- Open a playlist, page or document when a session starts.
- Start sessions from calendar events with a word like "Focus" in the title.
- A weekly report with trends, your best day, longest streak, eight weeks of history and top tasks.

**Battery**
- Tips based on how you charge.
- A low-battery alert that names the apps using the most power.
- Switch power modes: Battery saver, Balanced or Best performance.
- A one-page health report to print or save as a PDF.

## 1.1.0 (13 October 2026)

A much bigger screenshot tool.

- **New ways to capture**: window (Ctrl+Alt+D), full screen (Ctrl+Alt+A), all screens, last area again and a 5-second delay.
  Optional Print Screen keys too: on its own for an area, with Alt, Ctrl or Shift for the other modes.
- **Better picking**: a magnifier with exact pixel position and colour, windows outlined as you hover, Space to switch modes,
  arrow keys to nudge, C to copy a colour. Optionally include the mouse pointer.
- **New editor tools**: select and move, ellipse, line, freehand pen, speech-bubble callouts, soft blur, crop and a colour picker,
  alongside the box, arrow, text, steps, highlight and pixelate tools.
- **Edit what you've drawn**: recolour, move, nudge and delete anything, with full undo and redo.
- **Style**: nine colours, fill options, line thickness, text size and shadows on shapes.
- **Image effects**: rotate, flip, resize, greyscale, invert, border, drop shadow and torn edges, plus a new Dusk frame.
- **Copy text** from any screenshot, using Windows' own text recognition.
- **More ways to share**: quick save with your own file name pattern, Save as PNG, JPG, BMP, GIF or TIFF, print, open in Paint or another app.
- **Skip the editor** if you like: copy or save straight after capturing.
- **Recent screenshots** on the Capture page, and edit any image file or the clipboard image.
- Zoom in the editor with Ctrl and the mouse wheel.

## 1.0.0 (13 October 2026)

The first release, with seven modules:

- **Dock**: pinned and open apps, magnify on hover, three styles, live music and focus cards, optional taskbar replacement.
- **Widgets**: clock, weather, calendar, system rings, battery, now playing, notes and countdowns, in Glass, Solid or Outline.
- **Wallpaper**: dawn, day, sunset and night scenes that follow real sunrise and sunset, or Bing's photo of the day.
- **Clipboard**: searchable history with pins, filters, a paste stack and plain-text paste.
- **Capture**: screenshot editor with arrows, boxes, text, highlights, blur, numbered steps, frames and automatic email blurring.
- **Focus**: timer pill, notification silencing, ambient sounds and a 7-day chart.
- **Battery**: health, capacity, cycles, charge history and an unplug reminder.
