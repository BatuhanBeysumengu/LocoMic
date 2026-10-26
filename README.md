# Equalizer Helper

A Windows desktop app that cleans up (or messes with) your microphone in real time. It captures your physical mic, runs it through a 6-band EQ plus optional pitch shift and "mic crunch" effects, and sends the result to a virtual audio cable so Discord, OBS, Zoom or any other app can use the processed voice as its microphone.

## Features

- **6-band voice EQ** – fixed bands tuned for speech, each with adjustable gain, Q and on/off:

  | Band      | Frequency | Type       |
  |-----------|-----------|------------|
  | Rumble    | 100 Hz    | Low shelf  |
  | Mud       | 250 Hz    | Peaking    |
  | Body      | 1 kHz     | Peaking    |
  | Presence  | 3 kHz     | Peaking    |
  | Sibilance | 6 kHz     | Peaking    |
  | Air       | 10 kHz    | High shelf |

- **Auto-tune** – listens to your voice for a few seconds, measures each band and nudges the gains toward a natural voice curve. Afterwards you can **Keep** the result (and optionally save it as a preset) or **Revert** it.
- **AI noise removal** – removes background noise (fans, keyboard, traffic, hum) even while you talk, using the [RNNoise](https://github.com/xiph/rnnoise) neural network. Runs on the CPU, no special GPU needed. A **Strength** slider blends between the cleaned and original voice.
- **Dynamics** – three simple sliders:
  - **Gain** – boost or lower a quiet/loud mic before processing.
  - **Gate** – turns the mic down when you're not talking, hiding keyboard, fan and room noise.
  - **Loudness** – evens out your volume and makes your voice fuller (compressor), with an always-on limiter so the output never clips.
- **Pitch shift** – raise or lower your voice in semitones.
- **Mic Crunch** – distortion + compression for an overdriven-mic effect.
- **Presets** – built-in presets (Flat, Voice Clarity, De-Boom, De-Harsh Sibilance, Deep Bass (Fun)), plus as many presets of your own as you like. Every preset (built-in ones too) can be tweaked and saved, and each one stores the EQ, noise removal, dynamics, pitch and Mic Crunch.
- **Share presets** – export any preset to a `.eqpreset` file and import presets other people send you.
- **Bypass** – instantly compare processed vs. original sound.
- **Mute** – silence your mic instantly (also from the tray menu).
- **Global hotkeys** – work everywhere, even in-game or with the app in the tray (customizable under **☰ → Settings**):

  | Action                  | Default            |
  |-------------------------|--------------------|
  | Toggle Bypass           | `Ctrl+Alt+B`       |
  | Toggle Mute             | `Ctrl+Alt+M`       |
  | Next / previous preset  | `Ctrl+Alt+→` / `Ctrl+Alt+←` |
  | Jump to preset 1–9      | `Ctrl+Alt+1` … `Ctrl+Alt+9` |

- **Monitoring** – hear yourself through headphones while tuning.
- **Live level meters** for input and output.
- **System tray** – keep it running in the background; start/stop from the tray icon.
- **Start with Windows** – optional, per-user (no admin rights needed).

## Requirements

- Windows 10 or 11
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (to build) or .NET 9 Desktop Runtime (to run a build)
- [VB-Audio Virtual Cable](https://vb-audio.com/Cable/) (free) – needed so other apps can receive the processed mic. The app detects whether it's installed and links to the download if not.

## Getting started

```bash
git clone https://github.com/BatuhanBeysumengu/Equalizer.git
cd Equalizer
dotnet run --project src/EqualizerHelper.App
```

Or open `EqualizerHelper.sln` in Visual Studio 2022 and press F5.

## How to use

Everything you use day to day is on the main screen. Devices, hotkeys, startup and preset files are under **☰ → Settings** (top-left corner). Hover over any button, checkbox or slider for a short explanation.

1. **Install VB-Audio Virtual Cable** and restart if prompted.
2. **☰ → Settings** – pick your devices:
   - **Microphone:** your real mic.
   - **Output:** `CABLE Input (VB-Audio Virtual Cable)`.
3. Back on the main screen, tick **Hear yourself via** and choose your headphones if you want to listen while tuning, then press **Start** and check that the *Input* meter at the bottom moves when you talk.
4. **Shape your sound:**
   - **Clean-up:** tick **AI noise removal** if there's background noise. Raise **Gain** if the *Input* meter barely moves, raise **Gate** until noise disappears between words, and add **Loudness** for a fuller, more even voice.
   - **Voice:** change **Pitch** or add **Crunch** for fun.
   - **Equalizer:** drag the bands to fine-tune your sound, or press **Auto-Tune Voice**, talk for a few seconds, then click **Keep** or **Revert** at the bottom.
5. **Presets** (top-right of the main screen):
   - **+ New preset...** (top of the dropdown) – creates a new preset from your current settings under a name of your choice.
   - **Save** – stores your changes into the selected preset.
   - **Delete** – removes one of your own presets. For a built-in preset you've changed, this button becomes **Reset** and restores its original settings.
   - To share presets, use **Import...** / **Export current...** under **☰ → Settings**. Imports never overwrite: a name that's already taken gets a number added (e.g. "My Voice (2)").
6. **In Discord / OBS / Zoom**, set the input/microphone device to `CABLE Output (VB-Audio Virtual Cable)`.

Closing the window keeps the app in the system tray; use the tray menu to show it, start/stop processing, or exit.

Your presets and settings are saved in `%APPDATA%\EqualizerHelper\` (`presets.json`, `settings.json`).

## Tech stack

| Area          | Technology |
|---------------|------------|
| Language      | C# (.NET 9) |
| UI            | WPF with a custom dark theme, MVVM via [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) |
| Tray icon     | Windows Forms `NotifyIcon` |
| Audio I/O     | [NAudio](https://github.com/naudio/NAudio) (WASAPI capture/playback, Media Foundation resampling) |
| DSP           | Hand-written: biquad filters (RBJ cookbook), compressor, limiter, noise gate, distortion, pitch shifter, band analyzer |
| Noise removal | [RNNoise](https://github.com/xiph/rnnoise) via [YellowDogMan.RRNoise.NET](https://github.com/Yellow-Dog-Man/RNNoise.Net) |
| Tests         | xUnit |

## Project structure

```
EqualizerHelper.sln
src/
  EqualizerHelper.Dsp/     Pure DSP (no dependencies): biquad filters, EQ chain,
                           compressor, limiter, noise gate, distortion,
                           pitch shifter, band analyzer
  EqualizerHelper.Audio/   NAudio integration: device enumeration, the real-time
                           processing engine and sample providers, RNNoise
                           noise removal, level meters
  EqualizerHelper.App/     WPF app: main window, view models, presets, settings,
                           auto-tune, tray icon, startup registration
tests/
  EqualizerHelper.Dsp.Tests/   Unit tests for the DSP library, noise removal and preset files
```

Signal chain: `Mic → Noise removal → Gain → Gate → EQ → Pitch shift → Mic Crunch → Loudness → Limiter → Output (virtual cable)`, with an optional parallel copy to your headphones for monitoring.

## Running the tests

```bash
dotnet test
```
