# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

Brick-SDR is a restyled fork of Thetis 2.10.3.x (WinForms, .NET Framework 4.8, x64) for HPSDR/ANAN radios, with official WDSP 2.11. There are no automated tests or linters; verification is building and running against a real radio (Hermes / ANAN-100D) or by screenshots of the running app.

GPL v2 (`LICENSE`); based on Thetis (MW0LGE) / PowerSDR, WDSP by NR0V. `README.md` is a short Brick-SDR intro (install, build); the upstream Thetis ReadMe was replaced.

## Git / releases

- Remote: `https://github.com/linoobs/Brick-SDR-Software` (public, branch `main`; owner account `linoobs`). Repo root is this folder (the one holding `Installer`, `Project Files`).
- GitHub CLI is not on PATH: `/c/Users/linoobs/tools/gh/bin/gh.exe` (already authenticated, `gh auth setup-git` done). No `winget` on this machine.
- `.gitignore` excludes `bin/obj/x64/Release`, `Project Files/build` and `BrickSDR-Setup*.exe/zip`: the installer is published only as a release asset (`gh release create vX.Y BrickSDR-Setup.exe --repo linoobs/Brick-SDR-Software`), never committed (repo file limit 100 MB, the installer is ~85 MB). v1.1 is released.
- Publishing (push, release) is outward-facing: confirm with the user first. Commits end with the Co-Authored-By line from the session attribution.

## Build / run

- Build (from `Project Files\Source`): `MSBuild.exe Console\BrickSDR.csproj -p:Configuration=Release -p:Platform=x64 -v:m -nologo` (VS 2026 MSBuild, `C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin`). Output goes straight to `Project Files\bin\x64\Release\BrickSDR.exe`; the firewall rule on the dev machine only allows that path, so always run it from there.
- `BrickSDR.sln` also holds the native projects (`wdsp`, `ChannelMaster`, ...); they only need rebuilding when C sources change (WDSP shims live in `Source\wdsp\brick_compat.c`).
- Close a running instance before building (exe is locked): `CloseMainWindow()` for a graceful exit so state is saved.
- Installer: from the repo root run `powershell -NoProfile -ExecutionPolicy Bypass -File Installer/build-installer.ps1`. It zips `bin\x64\Release` (minus pdb) into a csc-compiled WinForms exe (`Installer\Installer.cs`, requires admin) and writes `BrickSDR-Setup.exe`. The version string is hard-coded in the script and in `BrickBranding.cs` (`Version`). Don't run the installer itself without need (UAC, firewall/registry changes).
- User data lives in `%APPDATA%\OpenHPSDR\Brick-SDR-x64` (settings DB, `rx2_band_memory.txt`, `vac2_state.txt`).

## Hard constraints from the user

- Nothing from an `OdinSDR` folder may be used in the product (it may only be launched to look at styles). WDSP must be the official TAPR 2.11 sources, not Odin files.
- Splash/installer/UI text is English.
- Real-hardware testing: TX only at about 10 % power for a few seconds; leave RX2 / SUB-RX off and close the app gracefully at the end of a test session.

## Architecture

The code is Thetis (`Source\Console\console.cs` is a ~50k-line partial class `Console`) plus a "Brick" layer that rebuilds the UI at runtime instead of editing the Designer:

- `BrickLayout.cs` - `brickBuildLayout()` (called once the console is shown) re-parents the legacy controls into new cards (top bar, left column, right band/mode/filter column) and owns theme colours, the S-meter, popups and the 200 ms `brickTick` timer. The `brickActive` flag makes the old resize/layout paths (`ResizeConsole`, `Console_Resize`, `moveModeSpecificPanels`, ...) early-return.
- **Legacy controls keep doing the work.** New owner-drawn controls (`BrickControls.cs`: cards, buttons, drop-downs, steppers, AGC buttons, VFO boxes) are views: the original combo/numeric/checkbox is moved off-screen (x = -3000) or hidden, and the Brick control forwards events to it and mirrors its state via the `brickAgcSyncs` list (Actions run by `brickTick`). To change behaviour, hook the legacy control / `console.cs` handler, not the visual.
- `BrickModePanels.cs` (SSB/CW/FM/digital panels), `BrickSetupTweaks.cs` (edits to the Setup form, e.g. NNR page, display options), `BrickHardware.cs` (MAC-based Brick2/Brick3 detection and title), `BrickBranding.cs`, `BrickWisdom.cs` (FFTW wisdom generation shown on the splash) are the other Brick files. Edits to upstream files (`console.cs`, `display.cs`, `setup.cs`, `radio.cs`, `splash.cs`, ...) are small hooks and are commented/named `brick*`.
- Added controls must be **named `brick*` or given names**; `setup.cs` `getControlList` skips unnamed/`brick*` controls (duplicate keys crashed Setup save otherwise).
- State persistence: `SaveState()` -> `GetStateList()` (flattened through `brickCollectStateControls`, so nested controls such as RX2 mode/band are saved; `chkRX2`, `chkVFOLock`, `chkVFOBLock`, `chkVFOSync`, `chkEnableMultiRX` are always-saved). `GetState()` restores before the layout is built, and `chkRX2` is read early in init (`local_rx2Enabled`). Anything that depends on the built layout (e.g. `chkSplitDisplay.Checked = RX2Enabled`) must be applied after `brickBuildLayout()`.
- Display (`display.cs`, DirectX2D/SharpDX) splits RX1/RX2 via `Display.SplitY` / `RX2SplitRatio`. Mouse handling for the spectrum/waterfall is in `console.cs` (`pnlDisplay_MouseDown/Move`, `brickOverVisor`, `brickVisorDrag`, cursor override at the end of `pnlDisplay_MouseMove` - it must keep the hand cursor for the dB scale grid adjust and AGC handles).
- Per-band memories: RX1 uses Thetis band stacks; RX2 has its own (`SetupRX2Band`, `brickRx2Memo`, file `rx2_band_memory.txt`). TX attenuation per band is `tx_step_attenuator_by_band`; `brickTick` re-applies it to the Setup/UI when the TX band changes.
- Audio levels: Master AF (`ptbAF`) sets the mixer volume (`Audio.MonitorVolume`), RX1/RX2 AF set WDSP panel gain (`RXOutputGain`), MUTE (`chkMUT`/`chkRX2Mute`) only mutes VAC when `MuteWillMuteVAC1/2` is set. Right-click on MUTE = "mute all" (`brickSetMuteAll`, button red).
- WDSP: `Source\wdsp` is official WDSP 2.11 plus `brick_compat.c` stubs; C# bindings are in `dsp.cs`/`cmaster.cs`. NR3/NR4 are the new NNR; `GetPSDisp` has the 2.11 signature (used by `AmpView.cs`/`PSForm.cs`, with NaN sanitising). The wisdom file is `wdspWisdom01`, copied next to the exe.

## Gotchas when editing

- Source files are UTF-8 with BOM and **LF** line endings; when patching with PowerShell, write with `UTF8Encoding($true)` and match `\n`. In PowerShell `R` is an alias of `Invoke-History` - don't name helper functions `R`. Escape sequences like `\u25C4` get mangled by `sed`; use the Edit tool.
- Test by screenshots and synthetic mouse (coordinates are in real 2560x1440 pixels); audio can be verified by reading the default render device's peak meter (IAudioMeterInformation).
