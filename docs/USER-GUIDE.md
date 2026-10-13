# Arrakis user guide

Everything Arrakis can do, and how to do it. If something isn't covered here,
[open an issue](https://github.com/bhxuk/arrakis/issues) and say what you were trying to do.

- [Getting started](#getting-started)
- [The hub](#the-hub)
- [Dock](#dock)
- [Widgets](#widgets)
- [Wallpaper](#wallpaper)
- [Clipboard](#clipboard)
- [Capture](#capture)
- [Focus](#focus)
- [Battery](#battery)
- [Shortcuts at a glance](#shortcuts-at-a-glance)
- [Troubleshooting](#troubleshooting)

---

## Getting started

1. Download **[Arrakis-Setup.exe](https://github.com/bhxuk/arrakis/releases/latest/download/Arrakis-Setup.exe)** and double-click it.
2. If Windows shows **"Windows protected your PC"**, click **More info**, then **Run anyway**. This appears because
   the installer isn't code-signed, not because anything is wrong.
3. Leave **Start Arrakis when I sign in** ticked if you want it ready every time you log in.
4. Arrakis opens on the hub. Everything except Wallpaper starts switched on; switch off anything you don't want.

Arrakis lives in the **notification area** by the clock (you may need to click the **^** arrow to see it).

- **Left-click** the icon to open the hub.
- **Right-click** it for quick actions: clipboard, screenshot, focus, edit widgets, show the Windows taskbar, exit.

Closing the hub window doesn't stop Arrakis; it carries on in the background. To stop it completely,
right-click the icon and choose **Exit Arrakis**.

## The hub

<img src="images/hub.png" width="760" alt="The hub">

The **Home** page has a card for each module. The switch in the corner turns it on or off straight away.
Click anywhere else on a card to open that module's settings. The **Arrakis itself** card shows how much memory
Arrakis is using and lets you check for updates.

**Settings** (bottom of the sidebar) has:

- **Start with Windows**
- **Check for updates**: looks on GitHub for a newer version once a day.
- **Show the Windows taskbar now**: in case it's hidden and you want it back.
- **Open settings folder**: where your settings, clipboard history and stats live (`%AppData%\Arrakis`).
- **Exit Arrakis**

## Dock

<img src="images/dock.png" width="760" alt="The dock">

The dock sits at the bottom of the screen and shows:

- the **Arrakis button** on the left: click it to open the Start menu, right-click it to open the hub;
- your **pinned apps**;
- every **open app**, with a dot underneath;
- **live activities** to the right: what's playing (click to play or pause) and your focus timer (click to pause).

**Using it**

| Do this | And this happens |
|---|---|
| Click an app that isn't open | It opens |
| Click an open app | It comes to the front. Click again to minimise it |
| Click an app with several windows | Each click moves to the next window |
| Right-click an app | Pick a window, open a new one, pin or unpin, or close it |

**Settings (Arrakis > Dock)**

- **Style**: *Floating glass* (magnifies on hover), *Minimal pill* (no background until you hover) or *Edge bar*
  (full width, like the classic taskbar).
- **Hide the Windows taskbar**: use the dock instead. The taskbar always comes back when Arrakis exits.
- **Get out of the way**: the dock slides down when a window is maximised or full screen. Push the mouse to the
  very bottom of the screen to bring it back.
- **Magnify on hover**, **Live activities** and **Icon size**.
- **Pinned apps**: remove pins, or **Pin a program...** to choose any `.exe`.

## Widgets

<img src="images/widgets.png" width="760" alt="Widgets">

Widgets sit on your desktop, behind your windows. Out of the box you get a clock, weather, calendar,
system rings, now playing and a note.

- **Move**: drag a widget anywhere. It remembers where you put it.
- **Change style**: right-click any widget > Style > *Glass*, *Solid* or *Outline*.
- **Remove**: right-click > Remove this widget, or press **Ctrl+Alt+W** to show red remove buttons on every widget
  (press it again to hide them).
- **Stop accidental moves**: right-click > Lock widgets.
- **Add more**: Arrakis > Widgets, then click a tile.

| Widget | Notes |
|---|---|
| Clock | Time and date |
| Weather | Set your city in Arrakis > Widgets first. Uses Open-Meteo (free, no account). Updates every 30 minutes |
| Calendar | This month, today highlighted. The week starts on your Windows setting |
| System rings | CPU, memory, and battery (or disk space on a desktop PC) |
| Battery | Charge and time left |
| Now playing | Works with Spotify, browsers, Media Player and anything else that shows in Windows' media controls |
| Notes | **Double-click** to type. Click away to save |
| Countdown | Right-click > Edit countdown to set the name and date |

## Wallpaper

<img src="images/wallpaper.png" width="760" alt="Wallpaper">

Wallpaper is **off** until you switch it on, so Arrakis never changes your desktop without asking.

- **Follow the sun**: dawn, day, sunset and night scenes change with the real sunrise and sunset where you are.
  Arrakis uses the city you set for the weather widget (or Birmingham, UK until you set one).
- **Keep one scene**: click a scene, or pick it under **Mode**.
- **Bing photo of the day**: a new photo each morning instead.

The scenes are drawn at your screen's exact resolution. When you switch Wallpaper off (or click
**Put my old wallpaper back**), your original wallpaper comes back.

## Clipboard

<img src="images/clipboard.png" width="760" alt="Clipboard">

Arrakis keeps a history of everything you copy: text, links, colours and images.
Windows' own **Win+V** still works; Arrakis keeps a longer, searchable history alongside it.

Press **Ctrl+Alt+V** anywhere to open it. Start typing to search.

| Key | What it does |
|---|---|
| Up / Down | Move through the list |
| Enter | Paste into the app you were using |
| Shift+Enter | Paste as plain text (no fonts or formatting) |
| Ctrl+P | Pin or unpin. Pinned items stay at the top and are never deleted |
| Ctrl+S | Add to the **stack** |
| Ctrl+Shift+V | Paste the whole stack, one item per line |
| Delete | Remove from history |
| Esc | Close |

You can also click an item to paste it, or right-click it for more options (copy only, open link, and so on).

**The stack** is for when you need several things in one go, for example a name, an address and a phone number.
Add each one with Ctrl+S, then paste them all at once.

**Privacy**: copies that password managers mark as private are never saved. History stays on your PC.
Change the size, turn off image saving or clear everything in Arrakis > Clipboard.

## Capture

<img src="images/capture.png" width="760" alt="Screenshot editor">

### Taking a screenshot

| Shortcut | Captures |
|---|---|
| Ctrl+Alt+S | An area you drag, or a window you click |
| Ctrl+Alt+D | A window (hover to see it outlined, click to take it) |
| Ctrl+Alt+A | The whole screen the mouse is on |

The tray menu (right-click the Arrakis icon > **Screenshot**) and Arrakis > Capture also have **All screens**,
**Last area again** and **Area in 5 seconds**, which gives you time to open a menu first.

**Using the Print Screen key**: switch on *Use the Print Screen key* in Arrakis > Capture to use these keys:
**Print Screen** for an area, **Alt+Print Screen** for a window, **Ctrl+Print Screen** for the full screen and
**Shift+Print Screen** for the last area again. Windows may already use Print Screen for Snipping Tool; turn that off in
Windows Settings > Accessibility > Keyboard first.

<img src="images/capture-overlay.png" width="760" alt="Picking an area with the magnifier">

**While picking an area**

| Do this | And this happens |
|---|---|
| Drag | Capture that area |
| Click | Capture the window under the mouse |
| Space | Switch between area and window mode |
| Arrow keys | Move the mouse one pixel at a time |
| C | Copy the colour under the mouse (for example `#E2585F`) |
| Enter | Capture the whole screen |
| Esc or right-click | Cancel |

The magnifier shows the pixels around the mouse, the exact position and the colour. Switch it off in Arrakis > Capture
if you don't want it. You can also choose to include the mouse pointer in screenshots.

### Editing

**Tools** (top bar). Each has a one-letter shortcut:

| Tool | Key | How to use it |
|---|---|---|
| Select | V | Click something you've added to select it. Drag to move, arrow keys to nudge (Shift for bigger steps), Delete to remove, click a colour to recolour. Double-click text to edit it |
| Box | R | Drag. Shift for a square |
| Ellipse | E | Drag. Shift for a circle |
| Line | L | Drag. Shift snaps to 45° |
| Arrow | A | Drag from where it starts to where it points. Shift snaps to 45° |
| Pen | F | Draw freehand |
| Text | T | Click and type. Enter to finish, Shift+Enter for a new line |
| Callout | S | Click to add a speech bubble and type |
| Steps | N | Click to drop numbered circles: 1, 2, 3... |
| Highlight | H | Drag over something to highlight it |
| Pixelate | P | Drag over anything private |
| Blur | B | Drag over anything private for a soft blur |
| Crop | C | Drag the area you want to keep |
| Colour | I | Click the picture to use that colour (the hex code is copied too) |

**Style** (right side): colour, fill for boxes and ellipses (*None*, *Soft* or *Solid*), line thickness, text size and
a soft shadow on shapes.

**Image**: rotate left or right, flip, resize, greyscale, invert, border (in the current colour), drop shadow and torn edges.
These apply to the picture with everything you've drawn on it, and can be undone.

**Frame**: put the screenshot on a background (*Sunset*, *Ocean*, *Meadow*, *Dusk*, *Light*, *Dark* or none), with padding,
rounded corners and a shadow.

**Text in the picture**: *Blur emails* finds email addresses and pixelates them (this also happens automatically when the
editor opens; switch it off in Arrakis > Capture). *Copy text* reads all the words in the picture and copies them.
Both use Windows' built-in text recognition, so nothing leaves your PC.

**Undo and redo**: Ctrl+Z and Ctrl+Y, or the buttons at the top right. **Zoom**: Ctrl and the mouse wheel, Ctrl+0 to fit,
Ctrl+1 for actual size.

### Sharing it

| Button | Shortcut | Does |
|---|---|---|
| Copy | Ctrl+C | Copies the finished picture |
| Save | Ctrl+S | Saves straight to your screenshot folder using your file name pattern. Click the message at the bottom to show the file |
| Save as... | Ctrl+Shift+S | Choose where, and PNG, JPG, BMP, GIF or TIFF |
| Print | Ctrl+P | Prints it, fitted to the page |
| Paint / Open with... | | Opens it in Paint or any other app |
| Pin | | Floats it above everything. Drag to move, scroll to resize, double-click to close, right-click to copy or edit |

### Capture settings (Arrakis > Capture)

- **After a capture**: open the editor (default), or skip it and copy, save, or copy and save straight away.
- **Folder, file name and format**: the file name can use dates like `{yyyy-MM-dd HH.mm.ss}` and `{title}` for the window's name.
- **Recent**: your latest saved screenshots. Click one to edit it again.
- **Edit an image file...** and **Edit image on clipboard** open any picture in the editor.

## Focus

<img src="images/focus.png" width="760" alt="Focus">

Press **Ctrl+Alt+F** (or click **Start focus** in Arrakis > Focus).

- A pill appears at the top of the screen with the time left. **Click it** for Pause, +5, Skip and End.
- While you focus: **notifications are silenced**, the **dock hides** and **widgets dim**. Each can be switched off.
- When the time's up you'll hear a chime and a short break starts. After the last session of the round you get a
  longer break.
- **Ctrl+Alt+F** again pauses and resumes.

**Ambient sound**: *Rain*, *Ocean*, *Brown noise* or *White noise*, with a volume slider. They play during focus time
and stop for breaks.

**Settings**: session name, focus length, short and long breaks, and sessions per round.

The **Last 7 days** chart shows how long you've focused each day, and your streak of days in a row.

## Battery

<img src="images/battery.png" width="760" alt="Battery">

For laptops and tablets. On a desktop PC it just says there's no battery; switch it off on the Home page.

- **Health**: how much your battery holds now compared with when it was new.
- **Holds when full**: the actual capacity in watt-hours.
- **Charge cycles**: not every laptop reports this.
- **Charge level, last 7 days**: Arrakis checks every 10 minutes, so this fills in over a few days.
- **Unplug reminder**: a notification when charging reaches your chosen level (80% by default). Keeping a battery
  below 100% most of the time helps it last longer.
- **Health over time**: recorded once a month.

## Shortcuts at a glance

| Shortcut | Does |
|---|---|
| Ctrl+Alt+V | Open the clipboard |
| Ctrl+Alt+S | Screenshot of an area |
| Ctrl+Alt+D | Screenshot of a window |
| Ctrl+Alt+A | Screenshot of the full screen |
| Ctrl+Alt+F | Start, pause or resume focus |
| Ctrl+Alt+W | Show or hide widget remove buttons |

You can change the clipboard and screenshot shortcuts in their settings pages. If a shortcut doesn't work,
another app is probably using it; Arrakis will tell you and you can pick a different one.

## Troubleshooting

**The taskbar has gone and I want it back.**
Open Start > Arrakis > **Restore Windows taskbar**. Or right-click the Arrakis icon > **Show Windows taskbar**.

**Windows says "Windows protected your PC".**
Click **More info** > **Run anyway**. It appears for apps that aren't code-signed.

**A shortcut does nothing.**
Another app may already be using it. Go to that module's page in Arrakis and choose a different one.

**The weather says "Set your city".**
Arrakis > Widgets > Weather city. Type a town and click **Set city**.

**Something else.**
Open Arrakis > Settings > **Open settings folder** and find `arrakis.log`. Attach it to an
[issue](https://github.com/bhxuk/arrakis/issues) along with what happened.
