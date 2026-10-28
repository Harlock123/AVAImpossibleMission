# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A search-and-somersault action game in the style of Dennis Caswell's 1984 Commodore 64 game Impossible Mission, in
C# on .NET 10 and Avalonia 12. One self-contained executable per platform (Windows, macOS and Linux, x64 and ARM).
The graphics are drawn in code, and the sounds and the spoken lines are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVAImpossibleMission.slnx                 # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAImpossibleMission.slnx                  # every test runs headless: no window opens
dotnet run --project src/AVAImpossibleMission          # play it; --no-audio for silence
IMPOSSIBLE_SHOTS=$PWD/Images dotnet test tests/AVAImpossibleMission.Tests --filter ScreenshotTests   # refresh the README's pictures
IMPOSSIBLE_PROBE=1 dotnet test tests/AVAImpossibleMission.Tests --filter PilotProbe   # how the computer agent gets on, for tuning
python3 tools/make_sounds.py                           # rebuild src/AVAImpossibleMission/Sounds (standard-library Python only)
```

`IMPOSSIBLE_PROBE=lazy` runs an agent who never touches the controls. Without `IMPOSSIBLE_PROBE` the probe test does
nothing.

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVAImpossibleMission/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, demonstration, play, pause, game over, high scores), the mission's
    phases (briefing, search, won, time up) and the fixed-step loop.
  - `GameEngine.Play.cs` is the rules: the agent, rooms and shafts, searching furniture, robots, the orb, and the
    pocket computer.
  - `Complex.cs` builds the rooms and shafts (`Complex`, `Room`) and has the `Planner` that finds paths in a room.
  - `Puzzle.cs` is the punch cards and their torn pieces.
  - `Entities.cs` has `Controls`, the measures (`Field`), furniture, lifts, robots, bolts, the orb and the player.
  - `Pilot.cs` is the computer agent, used by the demonstration and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVAImpossibleMission/Views/`: `GameCanvas` (split over two partial files) draws the fixed 1024x768 screen and
  runs the 60 Hz timer.
- `src/AVAImpossibleMission/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVAImpossibleMission.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: gameplay, complex, puzzle, whole
  mission, shift-release and screenshot tests, and the pilot probe.
- `tools/`: `make_sounds.py` and the formant voice `speech.py` it uses.

## Rules that keep it working

- **Fixed steps.** `GameEngine.Update` runs the game in fixed 1/60 s steps (`Field.Step`), independent of the frame
  rate. Randomness comes from `new Random(seed)`: `Complex.Build` uses a fixed default seed, and `NewGame` deals the
  cards and hiding places from the engine's seed. A `GameEngine` built with a seed always deals the same; without
  one, a new mission is dealt from `Environment.TickCount`. Tests pass a seed. Never use `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the game and draws it.
- **Input goes through `InputHandler`.** `Controls` has `dy` -1 for up the screen and +1 for down, on keys and stick
  alike (`LeftStickY < -0.4` is up). Test new controls through `InputHandler`, not by setting `Controls` directly.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock. `ShiftReleaseTests` covers this.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVAImpossibleMission`); tests
  load the table from a fresh folder under the system temp path and run with `Silent = true`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds and spoken lines go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push. It then publishes self-contained single-file builds for
win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as workflow artifacts.
Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a proper release.
