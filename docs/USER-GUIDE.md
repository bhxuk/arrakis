# Arrakis user guide

Everything Arrakis can do, and how to do it. If something isn't covered here,
[open an issue](https://github.com/bhxuk/arrakis/issues) and say what you were trying to do.

- [Getting started](#getting-started)
- [The hub](#the-hub)
- [Command bar](#command-bar)
- [Dock](#dock)
- [Widgets](#widgets)
- [Wallpaper](#wallpaper)
- [Clipboard](#clipboard)
- [Capture](#capture)
- [Focus](#focus)
- [Battery](#battery)
- [Updates and moving to a new PC](#updates-and-moving-to-a-new-pc)
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
- **Right-click** it for quick actions: command bar, clipboard, screenshots, focus, edit widgets, show the Windows taskbar, exit.

Closing the hub window doesn't stop Arrakis; it carries on in the background. To stop it completely,
right-click the icon and choose **Exit Arrakis**.

## The hub

<img src="images/hub.png" width="760" alt="The hub">

The **Home** page has a card for each module. The switch in the corner turns it on or off straight away.
Click anywhere else on a card to open that module's settings. The **Arrakis itself** card shows how much memory
Arrakis is using and lets you check for updates.

**Settings** (bottom of the sidebar) has:

- **Start with Windows**
- **Appearance**: *Dark*, *Light* or *Match Windows*, and an accent colour (amber, teal, blue, pink, green or purple)
  for buttons, switches and highlights. Click **Restart Arrakis now** to see the change everywhere.
- **Command bar**: on or off, and its shortcut. See [Command bar](#command-bar).
- **Updates**: check for updates, install them automatically, or update now. See [Updates](#updates-and-moving-to-a-new-pc).
- **Backup**: export and import everything.
- **Show the Windows taskbar now**: in case it's hidden and you want it back.
- **Open settings folder**: where your settings, clipboard history and stats live (`%AppData%\Arrakis`).
- **Exit Arrakis**

<img src="images/light.png" width="760" alt="The hub in the light theme">

## Command bar

<img src="images/commandbar.png" width="760" alt="The command bar">

Press **Alt+Space** anywhere and start typing. Use **Up** and **Down** to move, **Enter** to open, **Esc** to close.

| Type | And you get |
|---|---|
| An app's name | The app, from your Start menu and installed apps |
| A file name | Files from your Desktop, Documents, Downloads, Pictures, Music, Videos and recent files |
| A setting, like *display*, *bluetooth* or *sound* | That page of Windows Settings |
| A sum, like `999 * 1.25` or `(40 + 2) / 3` | The answer. Press Enter to copy it |
| Words you copied earlier | Matching clipboard items. Enter copies it again |
| *screenshot*, *focus*, *clipboard*, *widgets* | Arrakis actions, like **Focus for 50 minutes** or **Screenshot a window** |
| *power* | **Power mode: Battery saver**, **Balanced** or **Best performance** |
| *layout* | Your saved widget layouts |
| Anything else | **Search the web** for it |

If Alt+Space is already used by another app, pick a different shortcut in Arrakis > Settings > Command bar.

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
| Rest the mouse on an open app | Live previews of its windows. Click one to switch, or the cross to close it |
| Right-click an app | Pick a window, open a new one, pin or unpin, or close it |
| Drag an app, shortcut or file on to the dock | It's pinned |
| Drag a folder on to the dock | It's added as a folder stack |
| Click a folder stack | Its newest files. Click one to open it, or **Open folder** |

Apps with more than one window open show a small bar with the count under the icon instead of a dot.
**Downloads** is added as a folder stack the first time you run Arrakis. Right-click a stack to open or unpin it.

**Settings (Arrakis > Dock)**

- **Style**: *Floating glass* (magnifies on hover), *Minimal pill* (no background until you hover) or *Edge bar*
  (full width, like the classic taskbar).
- **Hide the Windows taskbar**: use the dock instead. The taskbar always comes back when Arrakis exits.
- **Get out of the way**: the dock slides down when a window is maximised or full screen. Push the mouse to the
  very bottom of the screen to bring it back.
- **Magnify on hover**, **Live activities** and **Icon size**.
- **Window previews** and **Window counts**: both on to start with.
- **A dock on every screen**: each monitor gets its own dock, showing the windows that are on that screen.
- **Pinned apps** and **Folders**: remove them, or **Pin a program...** and **Add a folder...**.

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
- **Resize**: right-click > Size > *Small*, *Medium* or *Large*.
- **Line them up**: **Snap to grid** (Arrakis > Widgets) neatens their position when you let go.
- **Each widget's own options**: right-click it. For example *Choose cities...* on world clocks or *Edit symbols...* on stocks.

**Layouts**: set your widgets up the way you like, then in Arrakis > Widgets type a name like *Work* and click
**Save current layout**. Switch between layouts there, or type *layout* in the command bar.

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
| Agenda | Your next events from your calendar (see below) |
| To-do list | Type and press Enter to add. Tick to finish. Right-click > Clear finished items |
| Quick launch | Drag apps, files or folders on to it, or right-click > Add an app or file... |
| World clocks | Right-click > Choose cities... Shows the time difference and whether it's tomorrow there |
| Photo frame | Right-click > Choose folder... It shows your Pictures folder to start with and changes every few minutes |
| Stocks | Right-click > Edit symbols... Prices come from Stooq, so use codes like AAPL.US, VOD.UK or ^UKX |
| Headlines | Right-click > Change feed... and paste any RSS or Atom news feed. Click a headline to read it |

**Connect your calendar**: in Arrakis > Widgets, paste your calendar's **.ics link** under *Calendar link*.

- **Google Calendar**: Settings > click your calendar > *Secret address in iCal format*.
- **Outlook.com**: Settings > Calendar > Shared calendars > *Publish a calendar* > copy the ICS link.
- **iCloud**: share the calendar as a *Public Calendar* and copy the link.

Then the calendar widget marks days with events (hover to see them), the agenda widget lists what's next, and
**Meeting alerts** give you a notification 10 minutes before each event. Focus can use it too.

## Wallpaper

<img src="images/wallpaper.png" width="760" alt="Wallpaper">

Wallpaper is **off** until you switch it on, so Arrakis never changes your desktop without asking.

Pick a **Mode**:

- **Follow the sun**: dawn, day, sunset and night scenes change with the real sunrise and sunset where you are.
  Arrakis uses the city you set for the weather widget (or Birmingham, UK until you set one).
- **Dawn**, **Day**, **Sunset** or **Night**: keep one scene.
- **Your photos (slideshow)**: choose a folder (subfolders are included) and how often to change.
- **Your photos for each time of day**: choose a photo for each of dawn, day, sunset and night, and they change with the sun.
- **Bing photo of the day**: a new photo each morning.

With the scenes you can also switch on:

- **Match the weather**: clouds, rain, snow, fog and storms appear when they're happening outside.
- **Moving sun**: the sun travels across the sky through the day, and the moon at night.

Every screen gets a picture drawn at its own size, and with your photos each screen gets a different one.
When you switch Wallpaper off (or click **Put my old wallpaper back**), your original wallpaper comes back.

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

Right-click an item for **Paste**, **Paste as plain text**, **Paste as**, **Copy only**, **Add to collection**,
**Open link** and **Remove from history**.

**Paste as** changes the text as it goes in: *UPPERCASE*, *lowercase*, *Title Case*, *Sentence case*, *One line*,
*List to commas*, *Commas to list*, *Remove link tracking* (takes `utm_` and similar off links), *Sort lines*
and *Remove duplicate lines*.

**Collections**: right-click > Add to collection > **New collection...** to make one, like *Work* or *Recipes*.
Each collection gets its own filter at the top of the panel.

**Snippets** are bits of text you type often, like your address or a sign-off. Add them in Arrakis > Clipboard >
Snippets, each with a **shortcode**. Type the shortcode in any app (for example `;sig`) and it's replaced with the
snippet. In a snippet, `{date}`, `{time}` and `{clipboard}` are filled in for you. They're also in the panel under
the **Snippets** filter. A few examples are added to start you off.

**Private items**: anything that looks like a card number, bank account (IBAN) or password is shown as dots, kept
out of search and sync, and deleted after a minute (you can change how long). Right-click > **Keep it** if you need it.

**Sync between PCs**: in Arrakis > Clipboard > Sync, choose a folder both PCs can see (OneDrive, Dropbox, Google Drive
or a network drive) and set a password. Do the same on the other PC with the same password. History is encrypted
before it's written to the folder and syncs every 3 minutes. Pinned items, snippets and collections come across too;
private items never do.

**Privacy**: copies that password managers mark as private are never saved. History stays on your PC unless you
turn on sync. Change the size, turn off image saving or clear everything in Arrakis > Clipboard.

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

**What are you working on?** Type a task before you start. It shows on the pill and is saved with the session,
so the report can show where your time went.

**Distracting apps**: list programs like `steam.exe` or `discord.exe` (or click to pick one that's running), then choose
what happens when one opens during focus time: **Nudge** (a reminder) or **Minimise** (straight out of the way).
Breaks are left alone.

**Open on start**: paste a link, like a Spotify playlist, a YouTube lo-fi stream or a document, and it opens each time a session starts.

**Start from your calendar**: once your calendar is connected (Arrakis > Widgets), any event with *Focus* in its
title (or a word you choose) starts a session that runs until the event ends.

**Your week**: this week against last, the last 30 days, your best day, your longest streak, eight weeks of
history, your top tasks and recent sessions.

<img src="images/focus-report.png" width="760" alt="The focus report">

## Battery

<img src="images/battery.png" width="760" alt="Battery">

For laptops and tablets. On a desktop PC it says there's no battery, but you can still switch **power modes** there.

- **Health**: how much your battery holds now compared with when it was new.
- **Holds when full**: the actual capacity in watt-hours.
- **Charge cycles**: not every laptop reports this.
- **Charge level, last 7 days**: Arrakis checks every 10 minutes, so this fills in over a few days.
- **Unplug reminder**: a notification when charging reaches your chosen level (80% by default). Keeping a battery
  below 100% most of the time helps it last longer.
- **Health over time**: recorded once a month.
- **Tips for your battery**: after a day or two, Arrakis looks at how you charge and suggests changes, like
  unplugging before bed if it sits at 100% overnight.
- **Low battery alert**: a notification at the level you choose (20% by default) with the apps using the most power right then.
- **Power mode**: *Battery saver*, *Balanced* or *Best performance*, the same as Windows' own power mode setting.
- **Health report**: **Save a report** makes a one-page web page with health, capacity, cycles and a chart, ready to
  print or save as a PDF. Handy when selling a laptop or making a warranty claim.

## Updates and moving to a new PC

**Updates**: Arrakis checks GitHub for a newer version when it starts. With **Install updates automatically** on
(the default), it downloads and installs it quietly, then reopens. It never does this during a focus session.
It checks again every 6 hours, so a PC that stays on for days still gets updates.

With automatic updates off, you'll get a notification instead: **click it** to install. You can also right-click
the tray icon > **Install update**, or click **Check for updates** on the Home page or **Check now** in
Arrakis > Settings, then **Install**. Every one of these downloads and installs the update for you; there's
nothing to download yourself.

**Moving to a new PC**: in Arrakis > Settings > Backup, click **Export...** to save one file with your settings,
widgets and layouts, clipboard history, snippets, focus history and battery history. Install Arrakis on the other
PC and click **Import...**. Arrakis restarts with everything in place.

## Shortcuts at a glance

| Shortcut | Does |
|---|---|
| Alt+Space | Open the command bar |
| Ctrl+Alt+V | Open the clipboard |
| Ctrl+Alt+S | Screenshot of an area |
| Ctrl+Alt+D | Screenshot of a window |
| Ctrl+Alt+A | Screenshot of the full screen |
| Ctrl+Alt+F | Start, pause or resume focus |
| Ctrl+Alt+W | Show or hide widget remove buttons |

You can change the command bar, clipboard and screenshot shortcuts in their settings pages. If a shortcut doesn't work,
another app is probably using it; Arrakis will tell you and you can pick a different one.

## Troubleshooting

**The taskbar has gone and I want it back.**
Open Start > Arrakis > **Restore Windows taskbar**. Or right-click the Arrakis icon > **Show Windows taskbar**.

**Windows says "Windows protected your PC".**
Click **More info** > **Run anyway**. It appears for apps that aren't code-signed.

**A shortcut does nothing.**
Another app may already be using it. Go to that module's page in Arrakis and choose a different one.

**My calendar isn't showing.**
Check the link ends in `.ics` and is the *secret* or *published* address, not the normal web page. Then right-click
the agenda widget > **Refresh calendar**.

**Snippets don't expand in one app.**
Some apps running as administrator don't accept typing from other apps. Run Arrakis the same way, or paste the
snippet from the clipboard panel instead.

**Clipboard sync isn't picking things up.**
Both PCs need the same folder and the same password. The sync status under Arrakis > Clipboard > Sync says when it last ran.

**The weather says "Set your city".**
Arrakis > Widgets > Weather city. Type a town and click **Set city**.

**Something else.**
Open Arrakis > Settings > **Open settings folder** and find `arrakis.log`. Attach it to an
[issue](https://github.com/bhxuk/arrakis/issues) along with what happened.
