# Brick-SDR for Linux and macOS: plan

Status: plan, nothing built yet. Decisions taken by the project owner on 2026-11-03: no Wine / CrossOver stop-gap; build a portable core on WDSP and the deskHPSDR engine (already adapted to Linux, macOS and the Brick boards), then draw a new interface.

## 1. What the ground looks like

- **Brick-SDR today** (Windows only): Thetis (C#, WinForms, SharpDX) + ChannelMaster (C, Winsock, Windows threads) + WDSP 2.11. `console.cs` is 55,000 lines that mix interface and radio logic, so it can not be lifted to other systems.
- **deskHPSDR** (github.com/dl1bz/deskhpsdr, GPL-3.0, C, GTK3, miniaudio, RtMidi, WDSP 2.11, OpenHPSDR protocol 1 and 2, Hermes Lite 2, Brick2 / Brick3 support with PA calibration files, macOS / Linux / Windows). Its README credits the Brick developer for the Brick technical documents. It is the right source for the portable engine. Its own interface is GTK3.
- **Our own assets that are not tied to Thetis and are worth keeping**: the digital modes (FT8 / FT4 through the mfsk core, RTTY, three CW decoders including DeepCW on ONNX Runtime, FreeDV / RADE), call sign look-up, online logbooks, PSK Reporter, the log bar, the visual design of the Brick interface, the Brick profiles. About 10 thousand lines of C# that need only a thin radio interface, not Thetis.

## 2. Decision: language and interface framework

These three are different layers: C and C# are languages, Qt6 is a (C++) framework.

| Option | For | Against |
| --- | --- | --- |
| **C core + C# interface (Avalonia UI, SkiaSharp)** | one interface code base for Windows, Linux and macOS; all our C# digital modes and tools are reused; XAML styling fits the Brick dark design; GPU drawn spectrum / waterfall; fast to iterate | needs the .NET runtime (self-contained package, about 70 MB); real-time work must stay in the C core |
| Qt6 (C++) | best native feel, mature on all systems, QRhi / OpenGL for the waterfall | all C# digital code would have to be rewritten or hosted; heavier toolchain; one more language to maintain |
| GTK3 + C (as deskHPSDR) | fastest to a working radio | GTK on macOS is weak, our C# code is not reused, the Brick look is hard in Cairo |

**Recommendation: C core + C# / Avalonia interface.** The engine (network, WDSP, audio, TX chain, PureSignal) stays in C where real-time matters and where deskHPSDR already works; everything that a user sees and all the operating features are C#.

## 3. Architecture

1. `brick-core` (C, GPL-3.0): taken from deskHPSDR and WDSP 2.11 without the GTK parts. Discovery, protocol 1 and 2 network I/O, DDC / DUC, WDSP channels (RX1, RX2, TX, PureSignal), audio backend (miniaudio: CoreAudio, ALSA, PulseAudio / PipeWire, WASAPI), meters, spectrum frames. A small C API: create / start / stop radio, set frequency, mode, filter, AGC, noise reduction, TX, get spectrum and waterfall lines, meters, decoder audio tap.
2. `Brick.Core` (C#): P/Invoke bindings and the radio state model (VFOs, band stacks, memories, per-band settings, Brick PA / S-meter profiles), settings in JSON or SQLite.
3. `Brick.Digital` (C#): our digital modes ported from `BrickFt`, `BrickDigital`, `BrickMfsk`, `BrickDeepCw`, `BrickGgMorse`, `BrickRade*`, `BrickPskReporter`, `BrickOnlineLogs`, `BrickCallLookup`, with the dependency on `Console` replaced by a few interfaces (audio tap, radio state, PTT).
4. `Brick.App` (C#, Avalonia): the interface. Spectrum, waterfall and 3D as custom SkiaSharp / OpenGL controls, VFO boxes, S-meter and power bar, band / mode / filter column, setup pages, DIGI windows, log bar.
5. Native libraries per platform: FFTW3 (static), WDSP, miniaudio, ONNX Runtime, mfsk-core (Rust, built as a shared library per target), RADE libraries (CMake).

Network detail from the Brick-SDR support reports: deskHPSDR ships `Brick-Tools\brick-fix-arp.sh` that sets a static ARP entry for a Brick. That is the first thing to test for the "PC stops sending packets to the radio" freeze that Brick3 users report (see section 7).

## 4. Phases

Rough size is a guess in working time for one developer; the real limits are test hardware and system access.

| Phase | Deliverable | Size |
| --- | --- | --- |
| 0. Preparation | licence check (GPL-3.0 core with GPL-2.0-or-later Thetis parts, `LICENSE-DUAL-LICENSING` of Thetis), contact with the deskHPSDR author, test machines (a Linux PC or VM, a Mac with Apple Silicon), repository layout, CI skeleton | 1 to 2 weeks |
| 1. Core | `brick-core` builds with CMake on Windows, Linux and macOS; a command line tool discovers a Brick, receives I/Q, demodulates SSB / CW / AM / FM to the sound card; tested first on Windows with the real Brick3 | 3 to 5 weeks |
| 2. Interface skeleton | Avalonia window with spectrum, waterfall, VFO A / B, band / mode / filter, S-meter, audio out; same code on all three systems | 3 to 4 weeks |
| 3. Transmit and radio features | TX chain (SSB, CW, digital), mic input, PureSignal, power / SWR meters, band stacks, memories, Brick profiles and PA calibration, RX2, noise reduction, Setup pages | 5 to 8 weeks |
| 4. Digital modes | `Brick.Digital` on the new core: FT8 / FT4, RTTY, CW decoders, FreeDV, log bar, call look-up, online logs, PSK Reporter | 4 to 6 weeks |
| 5. Parity and polish | TCI / CAT, DX cluster, VST3 (see below), accessibility of layouts, translations | open ended |
| 6. Release | CI matrix (Windows x64, Linux x64 / arm64, macOS arm64), AppImage and deb, macOS `.app` / `.dmg` signed and notarized, MSI for Windows, documentation | 2 to 3 weeks |

First usable version (receive and SSB transmit on all three systems) is the end of phase 3; feature parity with the Windows program is phase 5 and later.

## 5. Risks

- **Audio**: latency and under-runs differ per system; needs an audio loop-back test per platform.
- **Real-time threads**: priorities (MMCSS on Windows, thread policies on Linux and macOS) are different; the network and DSP threads must not live in .NET.
- **Rendering speed**: waterfall at 30 to 60 frames per second in Avalonia needs a custom GPU path, not per-pixel drawing.
- **macOS distribution**: a paid Apple Developer ID (about 99 USD per year) is needed for notarization, otherwise users get Gatekeeper warnings.
- **Two code bases**: until the new program replaces the Windows one, fixes have to be kept in step. The plan is to move Windows to the new program too (one interface for all three systems).
- **Licensing**: the new core is GPL-3.0 because of deskHPSDR; Thetis-derived files are "GPL v2 or later", so the combination is fine, but the dual-licensing notes in Thetis must be understood first.
- **Hardware testing**: nothing can be verified without radios on each system; the Windows PC with the Brick3 is the first test bed.

## 6. Decisions of the owner (2026-11-03)

1. Windows stays first and, for now, the only supported system. Nothing in the existing Windows program is touched for the sake of other systems; the cross-platform work runs in parallel, in its own repository / folder.
2. Linux is second. Test machine: a Linux Mint virtual machine under VMware on the development PC (no USB audio or radio passthrough needed for the first phases: network to the radio works through a bridged adapter).
3. macOS and the licence questions (GPL-3.0 core, Thetis dual-licensing notes) are decided later, before any macOS or release work.
4. Contact with the deskHPSDR author: not now; after there is something concrete to show (see the note in CLAUDE.md).

## 7. Related open items (not part of this plan)

- VST3 plugin host (nubbyless/SDR-VST3): postponed, notes are kept in CLAUDE.md; it would be a phase 5 item here.
- Freeze with two receivers at 1.536 MHz on a gigabit link (reported for Thetis and Brick-SDR): first test is a static ARP entry for the Brick on the PC.
