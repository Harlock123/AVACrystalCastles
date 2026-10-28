# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A gem-gathering chase in the style of Atari's 1983 arcade game Crystal Castles, in C# on .NET 10 and Avalonia 12.
One self-contained executable per platform (Windows, macOS and Linux, x64 and ARM). The isometric castles and
everything in them are drawn in code, and the sounds and music are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVACrystalCastles.slnx                 # the SDK version is in global.json (10.0.400 or later)
dotnet test AVACrystalCastles.slnx                  # every test runs headless: no window opens
dotnet run --project src/AVACrystalCastles          # play it; --no-audio for silence
CASTLES_SHOTS=$PWD/Images dotnet test tests/AVACrystalCastles.Tests --filter ScreenshotTests   # refresh the README's pictures
CASTLES_PROBE=1 dotnet test tests/AVACrystalCastles.Tests --filter PilotProbe   # how the computer bear gets on, for tuning
python3 tools/make_sounds.py                        # rebuild src/AVACrystalCastles/Sounds (standard-library Python only)
```

Without `CASTLES_PROBE` the probe test does nothing.

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVACrystalCastles/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, demonstration, play, high scores) and the fixed-step loop.
  - `GameEngine.Play.cs` is the rules within a level: the bear, gems, enemies, bees, the honey pot and the magic
    hat.
  - `Castle.cs` builds each level's castle (squares, heights, stairs, gems, start) from the level number.
  - `Entities.cs` has `Controls`, the grid measures (`Field`), `Square`, `Bear`, `Enemy`, `Swarm` and effects.
  - `BearPilot.cs` is the computer player, used by the demonstration and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVACrystalCastles/Views/`: `GameCanvas` (split over two partial files) draws the fixed 1024x768 screen and
  runs the 60 Hz timer.
- `src/AVACrystalCastles/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVACrystalCastles.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: gameplay tests, screenshot tests and
  the pilot probe. The tests see the engine's internals (`InternalsVisibleTo`), such as `Calm`, which keeps
  enemies out unless a test puts them there.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` runs the game in fixed 1/60 s steps (`Field.Step`), independent of the frame
  rate, so a seed always plays the same game. Draw random numbers from the engine's seeded `_rng`
  (`new Random(seed)`); castles come from `new Random(level * 71 + 3)` so a level is always the same castle. Never
  use `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the game and draws it.
- **Input goes through `InputHandler`.** `Controls` is screen-relative: `Dy` is -1 for up the screen and +1 for
  down, on keys and stick alike (`LeftStickY < -0.4` is up). `GameEngine.Play` turns screen directions into grid
  directions (`WorldWay`). Keep keys and stick agreeing when adding controls.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVACrystalCastles`); tests
  load the table from a fresh folder under the system temp path and run with `Silent = true`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push. It then publishes self-contained single-file builds for
win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
