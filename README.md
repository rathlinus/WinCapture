<p align="center">
  <img src="src/WinCapture.App/Assets/Logo.png" width="96" alt="The WinCapture logo: four blue tiles with the dashed selection and the scissors of the Snipping Tool logo in white and grey">
</p>

# WinCapture

WinCapture is a screenshot tool for Windows 11 that takes over the Print Screen key. It freezes all monitors at the moment of the key press and lets you select a region on the frozen image. It is built for setups with several monitors of different size and scaling, including arrangements made with LittleBigMouse.

It runs in the notification area as one small native program; the editor for drawing on a screenshot is a WinUI 3 program that is only started when you use it. This is an early version: capturing and drawing on screenshots work, while snapping to screen elements is not built yet.

## Install

You need 64-bit Windows 11.

Download `WinCapture-<version>-x64.zip` from the [releases page](https://github.com/rathlinus/WinCapture/releases), unpack it to a folder where it can stay, and start `WinCapture.exe`. Nothing else has to be installed.

The programs are not signed, so Windows SmartScreen may stop the first start with "Windows protected your PC". Choose **More info**, then **Run anyway**.

WinCapture does not start with Windows by itself yet. To have it do so, press `Win+R`, enter `shell:startup` and put a shortcut to `WinCapture.exe` into the folder that opens.

To remove it, exit WinCapture from its tray menu, run `WinCapture.exe --restore-printscreen` to give Print Screen back to Snipping Tool, and delete the folder.

## Keys

| Key | Action |
|---|---|
| `PrintScreen` | Freeze all screens and select a region. |
| `Alt+PrintScreen` | Capture the window under the cursor. |
| `Ctrl+PrintScreen` | Capture the monitor under the cursor. |
| `Shift+PrintScreen` | Capture the last region again. |

Drag to draw a selection. When you let go of the mouse button it is copied to the clipboard and saved, and the overlay closes. `Esc` or a right-click cancels.

With `captureOnRelease` turned off in the settings, the selection stays after the drag so that it can be adjusted first:

| Key | Action |
|---|---|
| Drag | Draw a new selection. Drag its inside to move it, an edge or corner to resize it. |
| Arrow keys | Move the selection by one pixel, with `Shift` by ten. |
| `Enter` or double-click | Copy to the clipboard and save. |
| `Ctrl+C` | Copy only. |
| `Ctrl+S` | Save only. |
| Right-click | Drop the selection; without one, cancel. |
| `Esc` | Cancel. |

Screenshots are saved as PNG to the `Screenshots` folder in Pictures.

A click on the tray icon also starts a capture. Its menu opens the screenshots folder and exits WinCapture.

## Drawing on a screenshot

After every capture Windows shows a notification with a preview of the screenshot. Click it to open the screenshot in the editor. **Draw on last screenshot** in the tray menu opens the same editor, which helps when Do not disturb hides the notification. Both open the most recent capture.

The toolbar has, from left to right:

| Tool | Key |
|---|---|
| Pen | `P` |
| Highlighter | `H` |
| Line | `L` |
| Arrow | `A` |
| Rectangle | `R` |
| Ellipse | `E` |
| Colour, seven to choose from | |
| Thickness, 1 to 30 pixels | |
| Undo, redo | `Ctrl+Z`, `Ctrl+Y` |
| Copy | `Ctrl+C` |
| Save | `Ctrl+S` |
| Done | `Enter` |

**Done** copies and saves the screenshot with the drawing and closes the editor. Saving writes over the file of the screenshot, so there is one file per screenshot. `Esc` closes the editor and asks first if there is a drawing that was neither copied nor saved.

A screenshot larger than the window is shown smaller. The drawing is still made, and saved, in the screenshot's own pixels.

## LittleBigMouse

Windows arranges monitors by their pixels. A monitor with 1080 rows next to one with 1440 rows ends 360 pixels higher, even when both are equally tall on the desk. [LittleBigMouse](https://github.com/mgth/LittleBigMouse) corrects this for the mouse: it knows the physical size and position of each monitor and moves the pointer straight across.

When LittleBigMouse has a layout for the connected monitors, WinCapture uses it for selections:

- A selection across monitors is a straight rectangle on the desk. It covers the same height on every monitor it touches.
- In the captured image, monitors with fewer pixels per millimetre are scaled up to the densest one, so the parts fit together as they do on the desk. The bezels between monitors are left out.
- A selection on a single monitor is not affected. It is saved in that monitor's own pixels.

The size shown at the selection is the size of the image you get.

**Follow LittleBigMouse layout** in the tray menu turns this off and on; `useLittleBigMouseLayout` in the settings file is the same switch. Without it, or without a matching layout, a selection across monitors is put together pixel for pixel as Windows arranges them. The layout is read from `%LOCALAPPDATA%\Mgth\LittleBigMouse\Current.xml`, which LittleBigMouse 5 writes.

## Print Screen

Windows 11 opens Snipping Tool on Print Screen. On its first start WinCapture turns that off, which is the setting **Use the Print screen key to open screen capture** under Accessibility, Keyboard, and remembers what it was. To put it back:

```powershell
artifacts\app\WinCapture.exe --restore-printscreen
```

While WinCapture is not running, Print Screen then does what it did before Windows 11: it copies the whole desktop to the clipboard.

## Settings

Settings are read at start from `%LOCALAPPDATA%\WinCapture\settings.json`. The file is optional; this is what it holds by default:

```json
{
  "afterCapture": "CopyAndSave",
  "captureOnRelease": true,
  "hotkeys": {
    "region": "PrintScreen",
    "window": "Alt+PrintScreen",
    "monitor": "Ctrl+PrintScreen",
    "repeatRegion": "Shift+PrintScreen"
  },
  "takeOverPrintScreen": true,
  "useLittleBigMouseLayout": true
}
```

`afterCapture` is `CopyAndSave`, `Copy` or `Save` and decides what happens to a selection when the mouse is let go, or on `Enter`. `saveFolder` sets another folder for the files.

## Build from source

You need the .NET 10 SDK and the MSVC build tools, which Native AOT uses for linking.

```powershell
scripts\build.ps1 -Run
```

This runs the tests, writes `artifacts\app\WinCapture.exe` with the editor in `artifacts\app\Editor`, and starts WinCapture. The editor brings .NET and the Windows App SDK with it, which makes its folder about 260 MB.

`scripts\generate-assets.ps1` draws the logo. Its output is checked in; run it only to change the logo. [docs/releasing.md](docs/releasing.md) covers publishing a version.

## When a capture looks wrong

```powershell
artifacts\app\WinCapture.exe --diagnose C:\Temp\wincapture
```

writes the monitor arrangement as WinCapture sees it, the capture times, the LittleBigMouse layout if there is one, and one image per monitor to that folder. Failures and the time each overlay took to open are logged to `%LOCALAPPDATA%\WinCapture\log.txt`.

WinCapture is an independent project and is not affiliated with Microsoft.
