# ScrollCapture

A compact, portable Windows utility that captures the **whole content of a scrolling window**
(a long web page, a document, a list, a log) as one tall image.

1. Press **Capture scrolling window**. The program window gets out of the way and the screen
   freezes with a crosshair pointer; the window under the pointer is outlined.
2. Click the window to capture, or drag to select an area of it. Esc or the right button cancels.
3. The program scrolls the window by itself, takes a screenshot after every step, finds how far
   the content moved and joins the screenshots. Fixed parts (toolbars, sticky headers, status
   bars) are detected and kept only once. Press **Esc** at any time to stop; what was captured
   so far is kept.
4. The result is shown in the preview; the save dialog opens (PNG, JPEG, BMP or TIFF) and the
   image can also be copied to the clipboard.

- Single executable, no installation; settings are kept in `ScrollCapture.ini` next to it.
- UI languages: English, Ukrainian.
- Works with multiple monitors and mixed scale factors (per-monitor DPI aware).

## Options

| Option | Meaning |
|---|---|
| **Scrolling** | **Automatic** — mouse wheel; if the window does not move, scroll-bar messages, then the Page Down key. **Mouse wheel** — simulated wheel turns with the pointer resting inside the window near its right edge. **Scroll bar messages** — `WM_VSCROLL` line steps sent to the window; the mouse is not touched. **Page Down key** — for programs that ignore the other two. |
| **Capture** | **From the screen** — copies what is on screen; the window is brought to the front. **Window draws on the program's canvas** — `PrintWindow`: the window renders itself into the program's own bitmap, so it may be covered by other windows. Some programs draw nothing or black this way. |
| **Delay after scrolling** | Wait before the screenshot (smooth scrolling, content loaded on demand). The program also waits until two screenshots in a row are the same. |
| **Scroll to the top first** | Start from the beginning of the content instead of the current position. The program jumps there where the window allows it (the scroll bar's "top" command, Ctrl+Home), then scrolls up with doubling steps until nothing moves any more. |
| **Trim the background at the edges** | Cuts away the plain background around the content, keeping 5 px of it (less where the content is closer to an edge). The background color is detected automatically as the most common color along the image edges. Switching the option re-applies it to the last capture. |

The step size adapts: the first step is small, then the program learns how many pixels one
wheel notch or line moves and aims at about 60% of the visible height per step, so
consecutive screenshots always overlap.

## Command line

Without arguments the window opens. With arguments a capture runs without it:

```
ScrollCapture.exe (--window HWND | --point X,Y) --out FILE
                  [--scroll Auto|MouseWheel|ScrollMessages|PageDownKey]
                  [--capture Screen|PrintWindow] [--delay MS] [--max-height PX]
                  [--no-top] [--trim] [--frames DIR]
```

`HWND` is decimal or `0x` hex, `X,Y` are physical screen pixels, the format follows the
extension of `FILE`. `--max-height` stops the capture at that height (no limit by default).
`--trim` cuts the background at the edges as the option above does.
`--frames DIR` saves every screenshot for diagnostics. Exit code 0 on
success, 1 on a capture or save error, 2 on bad arguments.

## Limitations

- There is no height limit of its own. An image can hold up to 500 megapixels (one .NET array
  of 2 GB; for example 1920 × 260 000 px); the capture stops there, or earlier when memory runs
  out, and keeps what it has. PNG has no further limit (the program has its own encoder), BMP and
  TIFF neither; JPEG cannot exceed 65 500 px on a side (a Windows limit).

- Do not move the mouse or type while the wheel or Page Down method runs: the input goes to
  the window under the pointer or with the focus.
- Windows of programs running as administrator ignore simulated input from a normal program
  (UIPI). Start ScrollCapture as administrator to capture them.
- Only vertical scrolling is supported.
- Content that changes by itself (videos, animations, carousels) can prevent the screenshots from
  being aligned; the image then ends at that point and the status line says so. Selecting an
  area without the moving part usually helps.
- A window with a fixed part beside the scrolling one (a sidebar next to an editor) must be
  captured by selecting the scrolling area only.

## Requirements

- Windows 7 SP1, 8, 8.1, 10 or 11 (x86 or x64).
- .NET Framework 4.8 (preinstalled on Windows 10 1903+ and Windows 11; on Windows 7 SP1 and
  8.x it must be installed once).

## Building

```
dotnet build ScrollCapture.sln -c Release -warnaserror
dotnet test tests/ScrollCapture.Core.Tests -c Release --no-build
```

The executable is `src/ScrollCapture.App/bin/Release/net48/ScrollCapture.exe`. The stitching
logic (`src/ScrollCapture.Core`) has no UI dependencies and is covered by unit tests; the
application compiles its sources directly, so no DLL is shipped. The icon is generated by
`tools/make-icon.ps1`.

## License

GNU General Public License v3.0 — see [LICENSE](LICENSE). The software is provided "as is",
without warranty of any kind.
