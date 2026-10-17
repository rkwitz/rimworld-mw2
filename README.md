<img width="2559" height="1439" alt="rimworld_thumb" src="https://github.com/user-attachments/assets/84606393-3be5-4eb6-b93c-924e8735cd58" />

# RimWorld x MW2

Play RimWorld fights in first person, in Call of Duty: Modern Warfare 2 (2009).

RimWorld runs the game: its map, its pawns, their health, AI, guns and items. A RimWorld mod
reports what happens to a patched build of [IW4L](https://github.com/vladtrc/iw4L), an
open-source MW2 engine written in Rust. IW4L draws the same scene in 3D, and the two talk
over a local connection on your PC. Draft a colonist, press **Control in MW2**, and you
play that colonist in first person. Where you walk, they walk, and when you shoot, they
shoot in RimWorld.

What works today:

- The RimWorld map is rebuilt in MW2: ground, floors, walls, doors, roofs, mountains, caves,
  trees, grass, crops, furniture and items, using RimWorld's own pictures and MW2 models.
- Every pawn is there as RimWorld's own picture of them: colonists, raiders, animals and
  mechs.
- Drafted colonists and raiders hold their actual weapon, RimWorld's picture of it, aimed at
  what they shoot at. Melee attackers swing their weapon and lunge.
- You use your colonist's own gun, with RimWorld's burst, warm-up and cooldown. Your hits
  wound the body part you hit in RimWorld. RimWorld's bullets fly at you and can be dodged.
- You can throw your colonist's grenades. Explosions, fire, smoke, gas, rain, snow, fog,
  blood, firefoam and RimWorld's small effects all show in MW2.
- **E** opens doors, flicks switches and carries downed pawns. **F** opens your
  inventory. **Tab** brings RimWorld up, and RimWorld's own window is the minimap in the
  top-left corner.
- You move as fast as your colonist does, slowed by injuries, trees and terrain.
- Works with [Combat Extended](https://github.com/CombatExtended-Continued/CombatExtended):
  CE's bullets, shells and explosions show in MW2, your shots use your colonist's CE ammo,
  and MW2's clip, reserve and reload match their magazine and the ammo they carry.

Progress and plans are in [docs/ROADMAP.md](docs/ROADMAP.md).

> **You need your own copies of both games.** This repo contains no game code or assets.
> Setup reads your installs and writes everything it makes into folders that are never
> committed: `engine/`, `../reference/` and the textures folder. Please do not share them.
> Play offline only and never connect modded clients to online services.

## Requirements

**Windows 10 or 11.** The one-command run (`play.bat`) and window handling are
Windows-only. The engine also builds on Linux and macOS, but running both games together
there has not been tried.

Games (Steam):

- **RimWorld 1.6**, with **[Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2009463077)**
  from the Steam Workshop (click Subscribe).
  [Combat Extended](https://github.com/CombatExtended-Continued/CombatExtended) is optional
  and works too: put the bridge below it in the mod list.
- **Call of Duty: Modern Warfare 2 (2009)**, the multiplayer files that Steam installs with
  it. No DLC is needed.

Tools (install them in this order, then open a new terminal):

| Tool | Why | Get it |
|---|---|---|
| Git | Downloads this repo and the engine | https://git-scm.com |
| Python 3.10 or newer | Runs the setup script | https://www.python.org (tick **Add python.exe to PATH**) |
| .NET SDK 10 | Builds the RimWorld mod | https://dotnet.microsoft.com/download/dotnet/10.0 |
| Visual Studio Build Tools, **Desktop development with C++** | The C/C++ linker Rust needs on Windows | https://visualstudio.microsoft.com/downloads/ (Build Tools or Community) |
| Rust, through rustup | Builds the engine | https://rustup.rs (accept the defaults) |

Check them in a new PowerShell window. Each command should print a version:

```powershell
git --version
python --version
dotnet --version
cargo --version
```

Disk and time: the engine's first build needs about 15 GB free and takes 15 to 40 minutes,
depending on your CPU. Later runs only rebuild what changed.

## Install

### 1. Get the code

Make a folder for modding projects and clone this repo into it:

```powershell
mkdir $HOME\Documents\game_modding
cd $HOME\Documents\game_modding
git clone https://github.com/rkwitz/rimworld-mw2
cd rimworld-mw2
```

Setup puts the engine in `rimworld-mw2\engine\` and optional reference material next to the
repo in `game_modding\reference\`.

### 2. Check your setup

```powershell
python scripts\setup.py doctor
```

Git, dotnet, cargo, Python, RimWorld and MW2 should all say `[ok]`. The other warnings are
fine at this point. `ilspycmd` is only needed for development (see [Development](#development)),
and the folders under *Workspace* get made by the next steps. If a game isn't found, copy
`.env.example` to `.env` and fill in its folder:

- `RIMWORLD_DIR` is the folder that holds RimWorld's `Version.txt`.
- `IW4L_GAMES` is the folder that *contains* the `Call of Duty Modern Warfare 2` folder,
  usually `C:\Program Files (x86)\Steam\steamapps\common`.

Then run `doctor` again.

### 3. Download and patch the engine

```powershell
python scripts\setup.py engine
```

This downloads the pinned engine (chasm's
[2010 Rust rewrite mashup](https://github.com/chasmlol/2010-rust-rewrite-mashup), built on
IW4L) into `engine\` and applies every patch in `engine-patches\`. The last lines should be
`applied 0039-...patch`, then `wrote IW4L_GAMES, IW4L_SKATE=off to ...\engine\target\play\.env`.
That file is where the engine keeps its settings. It points the engine at your MW2 folder
and turns off Skate 3 mode, which this project doesn't use.

### 4. Build the RimWorld mod and add it to RimWorld

```powershell
python scripts\setup.py build-mod
python scripts\setup.py link-mod
```

`build-mod` writes `rimworld-mod\1.6\Assemblies\RimWorldMW2.dll`. `link-mod` links the
`rimworld-mod` folder into RimWorld's `Mods` folder as `RimWorldMW2`, so later builds take
effect without copying anything.

Start RimWorld once, open **Mods**, and enable **Harmony** and then **RimWorld x MW2 Bridge**,
in that order with the bridge below Harmony. With Combat Extended, the order is Harmony,
Combat Extended, then the bridge. RimWorld restarts. Then close RimWorld.

## Play

From the `rimworld-mw2` folder, run:

```powershell
.\play.bat
```

`play.bat` (the same as `python scripts\setup.py play`) does everything in order:

1. It builds the mod if RimWorld is closed. While RimWorld is open the DLL is locked, so it
   says so and skips the build.
2. It applies any new engine patches.
3. It starts RimWorld through Steam unless RimWorld is already open. Add `--no-rimworld` to
   skip this.
4. It builds and starts the engine on the `rimworld:flat` map. The first time, this is the
   long build mentioned above.

The terminal shows the engine's output. Leave it open. Then:

1. **Load a RimWorld save** (any colony). The mod turns on RimWorld's *Run in background*
   option so RimWorld keeps running while MW2 is in front.
2. **Draft a colonist** and click **Control in MW2** at the bottom of the screen. MW2's
   window comes up in borderless fullscreen and you are that colonist. RimWorld's window
   becomes the minimap in the top-left corner.
3. To give the colonist back, press **Tab** to bring up RimWorld and click
   **Stop controlling**, or undraft them. MW2 hides again until you control someone else.

When you close RimWorld, MW2 closes too and `play.bat` ends.

The first time MW2 starts, its volume is 30%. After that it keeps whatever you set in its
options.

### Controls in MW2

| Key | What it does |
|---|---|
| WASD, mouse | Walk and look. Your colonist walks with you in RimWorld |
| Shift | Sprint, as fast as your colonist can run |
| Left mouse | Fire your colonist's gun. If they hold grenades, throw one where you aim |
| G | Throw a grenade |
| R | Reload. The clip is your gun's burst, and reloading takes its cooldown |
| E | Open or shut the door in front of you, flick a switch, or pick up or put down a downed pawn |
| F | Your colonist's inventory: take items from the ground and drop your own |
| Tab | Bring up RimWorld, fully playable. Tab in RimWorld goes back to MW2 |
| Escape | Menu: Resume, Options, Quit |

While RimWorld is paused, you can look around but not move or shoot.

### Settings

These are environment variables. Set them in PowerShell before running `play.bat`, for example
`$env:RWMW2_FULLSCREEN = "0"`.

| Variable | Default | What it does |
|---|---|---|
| `RWMW2_ONE_WINDOW` | `1` | `0` keeps MW2's window open all the time (and open after RimWorld closes) |
| `RWMW2_FULLSCREEN` | `1` | `0` leaves MW2 windowed while you control a colonist |
| `RWMW2_RIMWORLD_PLANTS` | `1` | `0` uses MW2 tree models instead of RimWorld's plant pictures |
| `RWMW2_STANDIN_MAP` | `mp_estate` | The MW2 map whose models and textures are borrowed |
| `RWMW2_BRIDGE_ADDR` | `127.0.0.1:47820` | Where the engine listens for RimWorld. The port can also be changed in the mod's settings |

## Troubleshooting

- **Logs.** Each `play.bat` run writes the engine's output to `logs\engine.log`. RimWorld's
  log is `%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Player.log`.
  Lines from the mod start with `[RimWorldMW2]` and lines from the engine with `[rimworld]`.
- **No "Control in MW2" button.** The colonist must be drafted, and the mod must be enabled
  below Harmony. Check Player.log for `[RimWorldMW2] bridge connected`. If it is missing, the
  engine isn't running or the port is wrong.
- **The engine asks for a Skate 3 file, or stops with `iw4l-skate-convert.exe is missing`.**
  Run `python scripts\setup.py engine` again, or add the line `IW4L_SKATE=off` to
  `engine\target\play\.env`.
- **The engine can't find MW2.** Set `IW4L_GAMES` in `engine\target\play\.env` to your
  `Call of Duty Modern Warfare 2` folder, the one with `iw4mp.exe` and `zone\`.
- **The mod didn't rebuild.** RimWorld locks the mod's DLL while it is open. Close RimWorld
  and run `play.bat` again.
- **A patch fails to apply.** `engine\` must be exactly at the commit pinned in
  `versions.json`. Run `python scripts\setup.py engine --force` to download it fresh.
  `--force` deletes `engine\`, including the build and your MW2 settings in
  `engine\target\play\iw4l-artifacts`.

### Checking the connection alone

To test the RimWorld side without the engine:

```powershell
cd bridge
cargo run -p bridge-echo
```

Load a save in RimWorld. The terminal shows `hello from Rimworld` and then a tick count every
second. Stop it with Ctrl+C before running `play.bat`.

## Development

How it fits together:

```
game_modding\                 your workspace folder, may hold other projects too
├ reference\                  optional, made by setup, never committed
│ ├ repos\                    pinned reference repos
│ └ rimworld\                 decompiled\ and defs\ from your own RimWorld
└ rimworld-mw2\               this repo
  ├ rimworld-mod\             the RimWorld mod (C#, Harmony)
  ├ bridge\                   the message types (Rust) and a test echo server
  ├ protocol\                 the wire format: PROTOCOL.md and examples
  ├ engine-patches\           our changes to the engine, as patches
  ├ scripts\setup.py          setup, build and play
  └ engine\                   the patched engine, made by setup, never committed
```

- Architecture: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). Messages:
  [protocol/PROTOCOL.md](protocol/PROTOCOL.md). Engine patches:
  [engine-patches/README.md](engine-patches/README.md).
- To read RimWorld's code and XML while working on the mod, install `ilspycmd`
  (`dotnet tool install --global ilspycmd --version 11.1.0.9782`), then run
  `python scripts\setup.py all`. It downloads the reference repos and decompiles **your**
  RimWorld into `..\reference\rimworld\`. Never commit or share that folder.
- Pinned versions are in [versions.json](versions.json). Change them on purpose, in their own
  commit.
- Engine changes are new numbered patches in `engine-patches\`, made against the pinned
  engine commit with all earlier patches applied.
- Protocol changes touch `bridge\crates\protocol`, `protocol\PROTOCOL.md`,
  `protocol\examples.ndjson` and the mod together. `cargo test` in `bridge\` checks the
  examples.
- Tests: `cargo test` in `bridge\`, `cargo test -p rimworld_world` in `engine\`, and
  `python scripts\setup.py build-mod`.
- Notes for AI coding agents: [CLAUDE.md](CLAUDE.md).

## License

Apache-2.0, the same as IW4L and the engine base. See [LICENSE](LICENSE) and [NOTICE](NOTICE).
RimWorld's EULA allows decompiling for learning and as a reference for mods, but forbids
redistributing its code, and mods must be free. This is an unofficial fan project, not
affiliated with Ludeon Studios or Activision.
