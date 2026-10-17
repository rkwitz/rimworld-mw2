#!/usr/bin/env python3
"""Workspace setup for RimWorld x MW2.

Everything here works from games the user already owns. Nothing is downloaded
from game publishers and nothing produced here may be committed or shared.

Layout: <workspace>/reference/{repos,rimworld} is shared by every project in the
workspace; this project's engine checkout lives in <repo>/engine.

Usage:
    python scripts/setup.py doctor        check tools and find game installs
    python scripts/setup.py references    clone pinned reference repos
    python scripts/setup.py engine        clone the pinned IW4L engine base
    python scripts/setup.py decompile     decompile your RimWorld Assembly-CSharp.dll
    python scripts/setup.py defs          copy RimWorld's Defs XML (Core + owned DLC)
    python scripts/setup.py all           every step above, in order
    python scripts/setup.py build-mod     build the RimWorld mod DLL
    python scripts/setup.py test-mod      optional: build the mod and run its tests
    python scripts/setup.py link-mod      link the mod into RimWorld's Mods folder
    python scripts/setup.py play          build the mod, patch the engine, start RimWorld
                                          and the engine on the rimworld:flat map

Common options: --force (redo a step that already ran), --rimworld <dir>.
Settings can also live in a .env file at the repo root (see .env.example).
"""
from __future__ import annotations

import argparse
import json
import os
import platform
import re
import shutil
import subprocess
import sys
import threading
import time
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
VERSIONS = json.loads((REPO / "versions.json").read_text(encoding="utf-8"))
RIMWORLD_FOLDER = "RimWorld"
MW2_FOLDER = "Call of Duty Modern Warfare 2"
MOD_FOLDER_NAME = "RimWorldMW2"


# ---------------------------------------------------------------- settings

def load_env() -> dict[str, str]:
    """Read KEY=VALUE lines from .env, letting real environment variables win."""
    env: dict[str, str] = {}
    path = REPO / ".env"
    if path.exists():
        for line in path.read_text(encoding="utf-8").splitlines():
            line = line.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            key, value = line.split("=", 1)
            env[key.strip()] = value.strip().strip('"')
    env.update({k: v for k, v in os.environ.items() if k.startswith(("RWMW2_", "RIMWORLD_", "IW4L_"))})
    return env


ENV = load_env()


def workspace() -> Path:
    return Path(ENV.get("RWMW2_WORKSPACE", REPO.parent)).expanduser().resolve()


def reference_dir() -> Path:
    """Shared across projects: game files per game, plus reference repos."""
    return workspace() / "reference"


def rimworld_reference() -> Path:
    return reference_dir() / "rimworld"


def engine_dir() -> Path:
    """This project's own engine checkout, with engine-patches applied. Gitignored."""
    return REPO / "engine"


# ---------------------------------------------------------------- output

def info(msg: str) -> None:
    print(f"  {msg}")


def ok(msg: str) -> None:
    print(f"  [ok]   {msg}")


def warn(msg: str) -> None:
    print(f"  [warn] {msg}")


def fail(msg: str) -> None:
    print(f"  [fail] {msg}")


def step(title: str) -> None:
    print(f"\n== {title}")


class SetupError(Exception):
    pass


def run(cmd: list[str], cwd: Path | None = None) -> None:
    info("$ " + " ".join(f'"{c}"' if " " in c else c for c in cmd))
    result = subprocess.run(cmd, cwd=cwd)
    if result.returncode != 0:
        raise SetupError(f"command failed with exit code {result.returncode}")


def capture(cmd: list[str]) -> str | None:
    try:
        return subprocess.run(cmd, capture_output=True, text=True, check=True).stdout.strip()
    except (OSError, subprocess.CalledProcessError):
        return None


# ---------------------------------------------------------------- finding games

def steam_roots() -> list[Path]:
    home = Path.home()
    candidates = [
        Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / "Steam",
        Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Steam",
        home / ".steam" / "steam",
        home / ".local" / "share" / "Steam",
        home / ".var" / "app" / "com.valvesoftware.Steam" / ".local" / "share" / "Steam",
        home / "Library" / "Application Support" / "Steam",
    ]
    return [c for c in candidates if (c / "steamapps").is_dir()]


def steam_libraries() -> list[Path]:
    """Every steamapps/common folder Steam knows about, from libraryfolders.vdf."""
    libraries: list[Path] = []
    for root in steam_roots():
        libraries.append(root / "steamapps" / "common")
        vdf = root / "steamapps" / "libraryfolders.vdf"
        if vdf.exists():
            for match in re.finditer(r'"path"\s+"([^"]+)"', vdf.read_text(encoding="utf-8", errors="ignore")):
                libraries.append(Path(match.group(1).replace("\\\\", "\\")) / "steamapps" / "common")
    seen, unique = set(), []
    for lib in libraries:
        key = str(lib).lower()
        if key not in seen and lib.is_dir():
            seen.add(key)
            unique.append(lib)
    return unique


def find_rimworld(override: str | None = None) -> Path | None:
    explicit = override or ENV.get("RIMWORLD_DIR")
    if explicit:
        path = Path(explicit).expanduser()
        return path if (path / "Version.txt").exists() else None
    for lib in steam_libraries():
        path = lib / RIMWORLD_FOLDER
        if (path / "Version.txt").exists():
            return path
    return None


def rimworld_managed_dir(rimworld: Path) -> Path | None:
    """Managed/ holds Assembly-CSharp.dll; its location differs per platform."""
    candidates = [
        rimworld / "RimWorldWin64_Data" / "Managed",
        rimworld / "RimWorldLinux_Data" / "Managed",
        rimworld / "RimWorldMac.app" / "Contents" / "Resources" / "Data" / "Managed",
    ]
    candidates += sorted(rimworld.glob("*_Data/Managed"))
    for c in candidates:
        if (c / "Assembly-CSharp.dll").exists():
            return c
    return None


def find_mw2_games_root() -> Path | None:
    explicit = ENV.get("IW4L_GAMES")
    if explicit:
        path = Path(explicit).expanduser()
        return path if path.is_dir() else None
    for lib in steam_libraries():
        if (lib / MW2_FOLDER).is_dir():
            return lib
    return None


# ---------------------------------------------------------------- git helpers

def clone_pinned(url: str, commit: str, dest: Path, force: bool) -> None:
    if dest.exists() and (dest / ".git").exists() and not force:
        head = capture(["git", "-C", str(dest), "rev-parse", "HEAD"])
        if head == commit:
            ok(f"{dest.name} already at {commit[:10]}")
            return
        info(f"{dest.name} is at {str(head)[:10]}, moving to pinned {commit[:10]}")
    elif dest.exists() and force:
        shutil.rmtree(dest)
    if not dest.exists():
        dest.parent.mkdir(parents=True, exist_ok=True)
        run(["git", "init", "-q", str(dest)])
        run(["git", "-C", str(dest), "remote", "add", "origin", url])
    run(["git", "-C", str(dest), "fetch", "-q", "--depth", "1", "origin", commit])
    run(["git", "-C", str(dest), "checkout", "-q", "--force", "FETCH_HEAD"])
    ok(f"{dest.name} at {commit[:10]}")


# ---------------------------------------------------------------- steps

def cmd_doctor(args) -> bool:
    step("Tools")
    healthy = True
    for tool, why in [("git", "cloning repos"), ("dotnet", "decompiling and building the mod"),
                      ("cargo", "building the bridge and the engine")]:
        version = capture([tool, "--version"])
        if version:
            ok(f"{tool}: {version.splitlines()[0]}")
        else:
            fail(f"{tool} not found (needed for {why})")
            healthy = False
    sdks = capture(["dotnet", "--list-sdks"]) or ""
    if sdks and not re.search(r"^(\d{2,})\.", sdks, re.M):
        warn("no .NET 10+ SDK: install it, or use ilspycmd " + VERSIONS["ilspycmd"]["fallback_for_dotnet8"])
    if capture(["ilspycmd", "--version"]):
        ok("ilspycmd installed")
    else:
        warn(f"ilspycmd missing: dotnet tool install --global ilspycmd --version {VERSIONS['ilspycmd']['version']}")
    py = sys.version_info
    ok(f"python {py.major}.{py.minor}")

    step("Games")
    rimworld = find_rimworld(args.rimworld)
    if rimworld:
        version = (rimworld / "Version.txt").read_text(encoding="utf-8").strip()
        ok(f"RimWorld {version} at {rimworld}")
        if not version.startswith(VERSIONS["rimworld"]["game_version"]):
            warn(f"this project targets RimWorld {VERSIONS['rimworld']['game_version']}")
    else:
        fail("RimWorld not found. Set RIMWORLD_DIR in .env or pass --rimworld")
        healthy = False
    games = find_mw2_games_root()
    if games:
        ok(f"MW2 found; IW4L_GAMES={games}")
    else:
        warn(f'MW2 not found. Set IW4L_GAMES in .env to the folder containing "{MW2_FOLDER}"')

    step("Workspace")
    info(f"workspace: {workspace()}")
    for name, path in [("references", reference_dir() / "repos"), ("decompiled", rimworld_reference() / "decompiled"),
                       ("defs", rimworld_reference() / "defs"), ("engine", engine_dir())]:
        (ok if path.exists() else warn)(f"{name}: {path}{'' if path.exists() else ' (not set up yet)'}")
    return healthy


def cmd_references(args) -> bool:
    step("Reference repos")
    for ref in VERSIONS["references"]:
        clone_pinned(ref["repo"], ref["commit"], reference_dir() / "repos" / ref["name"], args.force)
    return True


def cmd_engine(args) -> bool:
    step("Engine (IW4L base)")
    engine = VERSIONS["engine"]
    dest = engine_dir()
    clone_pinned(engine["repo"], engine["commit"], dest, args.force)
    patches = sorted((REPO / "engine-patches").glob("*.patch"))
    # Applied patches are listed here: a later patch can change the lines
    # around an earlier one, so reversing the earlier one no longer checks.
    record = dest / ".rwmw2-patches"
    done = set(record.read_text(encoding="utf-8").split()) if record.exists() else set()
    # --ignore-whitespace: Windows checkouts have CRLF line endings, the patches have LF.
    for patch in patches:
        applied = patch.name in done or subprocess.run(
            ["git", "-C", str(dest), "apply", "--ignore-whitespace", "--reverse", "--check", str(patch)],
            capture_output=True).returncode == 0
        if applied:
            ok(f"patch already applied: {patch.name}")
        else:
            run(["git", "-C", str(dest), "apply", "--ignore-whitespace", str(patch)])
            ok(f"applied {patch.name}")
        done.add(patch.name)
        record.write_text("\n".join(sorted(done)) + "\n", encoding="utf-8")
    if not patches:
        info("no engine patches yet")
    games = find_mw2_games_root()
    env_file = dest / ".env"
    if games and not env_file.exists():
        env_file.write_text(f"IW4L_GAMES={games}\n", encoding="utf-8")
        ok(f"wrote {env_file} with IW4L_GAMES")
    launcher_settings(games)
    return True


def launcher_settings(games: Path | None) -> None:
    """On Windows the engine reads its settings from a .env beside iw4l.exe
    (engine/target/play/.env) and asks on first start for the MW2 folder and for Skate 3
    files. Answer both here: the MW2 folder found, and Skate 3 mode off (this project does
    not use it; without the line the engine stops on a missing converter)."""
    if platform.system() != "Windows":
        return
    path = engine_dir() / "target" / "play" / ".env"
    lines = path.read_text(encoding="utf-8").splitlines() if path.exists() else []
    keys = {line.split("=", 1)[0].strip() for line in lines if "=" in line}
    added = []
    if "IW4L_GAMES" not in keys and games:
        mw2 = (games / MW2_FOLDER).as_posix()
        lines.append(f'IW4L_GAMES="{mw2}"')
        added.append("IW4L_GAMES")
    if "IW4L_SKATE" not in keys:
        lines.append("IW4L_SKATE=off")
        added.append("IW4L_SKATE=off")
    if added:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text("\n".join(lines) + "\n", encoding="utf-8")
        ok(f"wrote {', '.join(added)} to {path}")


def cmd_decompile(args) -> bool:
    step("Decompile RimWorld")
    rimworld = find_rimworld(args.rimworld)
    if not rimworld:
        fail("RimWorld not found. Set RIMWORLD_DIR in .env or pass --rimworld")
        return False
    managed = rimworld_managed_dir(rimworld)
    if not managed:
        fail(f"no Assembly-CSharp.dll under {rimworld}")
        return False
    out = rimworld_reference() / "decompiled"
    if out.exists() and any(out.iterdir()) and not args.force:
        ok(f"already decompiled at {out} (use --force to redo)")
        return True
    if not shutil.which("ilspycmd"):
        fail(f"ilspycmd missing: dotnet tool install --global ilspycmd --version {VERSIONS['ilspycmd']['version']}")
        return False
    if out.exists():
        shutil.rmtree(out)
    out.mkdir(parents=True)
    run(["ilspycmd", "-p", "-o", str(out), str(managed / "Assembly-CSharp.dll")])
    record_local_version(rimworld)
    ok(f"decompiled to {out}")
    return True


def cmd_defs(args) -> bool:
    step("Copy RimWorld Defs")
    rimworld = find_rimworld(args.rimworld)
    if not rimworld:
        fail("RimWorld not found. Set RIMWORLD_DIR in .env or pass --rimworld")
        return False
    data = rimworld / "Data"
    if not data.is_dir():  # macOS keeps Data inside the app bundle on some builds
        data = rimworld / "RimWorldMac.app" / "Contents" / "Resources" / "Data"
    out = rimworld_reference() / "defs"
    if out.exists() and not args.force:
        ok(f"already copied to {out} (use --force to redo)")
        return True
    if out.exists():
        shutil.rmtree(out)
    count = 0
    for package in sorted(p for p in data.iterdir() if p.is_dir()):
        for sub in ("Defs", "Patches"):
            src = package / sub
            if src.is_dir():
                shutil.copytree(src, out / package.name / sub, ignore=keep_xml_only)
                count += sum(1 for _ in (out / package.name / sub).rglob("*.xml"))
        info(f"{package.name}")
    record_local_version(rimworld)
    ok(f"copied {count} XML files to {out}")
    return True


def keep_xml_only(directory: str, names: list[str]) -> list[str]:
    return [n for n in names if not (Path(directory, n).is_dir() or n.lower().endswith(".xml"))]


def record_local_version(rimworld: Path) -> None:
    rimworld_reference().mkdir(parents=True, exist_ok=True)
    version = (rimworld / "Version.txt").read_text(encoding="utf-8").strip()
    (rimworld_reference() / "VERSION.txt").write_text(
        f"RimWorld {version}\nInstall: {rimworld}\n", encoding="utf-8")


def cmd_build_mod(args) -> bool:
    step("Build RimWorld mod")
    run(["dotnet", "build", str(REPO / "rimworld-mod" / "Source" / "RimWorldMW2.csproj"), "-c", "Release"])
    ok(f"built into {REPO / 'rimworld-mod' / '1.6' / 'Assemblies'}")
    return True


def cmd_test_mod(args) -> bool:
    step("Test RimWorld mod")
    run(["dotnet", "test", str(REPO / "tests" / "RimWorldMW2.Tests"), "-c", "Release"])
    ok("mod builds and its tests pass")
    return True


def cmd_link_mod(args) -> bool:
    step("Link mod into RimWorld")
    rimworld = find_rimworld(args.rimworld)
    if not rimworld:
        fail("RimWorld not found. Set RIMWORLD_DIR in .env or pass --rimworld")
        return False
    mods = rimworld / "Mods"
    mods.mkdir(exist_ok=True)
    target = mods / MOD_FOLDER_NAME
    source = REPO / "rimworld-mod"
    if target.exists() or target.is_symlink():
        ok(f"{target} already exists")
        return True
    if platform.system() == "Windows":
        # A junction needs no admin rights, unlike a symlink.
        run(["cmd", "/c", "mklink", "/J", str(target), str(source)])
    else:
        target.symlink_to(source, target_is_directory=True)
    ok(f"linked {target} -> {source}")
    info("Enable the mod in RimWorld's mod list, below Harmony.")
    return True


RIMWORLD_STEAM_APP = "294100"
RIMWORLD_PROCESSES = ("RimWorldWin64.exe", "RimWorldWin.exe")


def rimworld_running() -> bool:
    if platform.system() != "Windows":
        return False
    tasks = capture(["tasklist", "/FO", "CSV", "/NH"]) or ""
    return any(name.lower() in tasks.lower() for name in RIMWORLD_PROCESSES)


# MW2's volume the first time the engine runs; after that, what you set in its options stays.
START_VOLUME = 0.3


def default_volume() -> None:
    """Starts a fresh engine at START_VOLUME. The engine saves its options to
    target/play/iw4l-artifacts/settings.cfg and reads them back, so this only
    writes the file when it doesn't exist yet."""
    settings = engine_dir() / "target" / "play" / "iw4l-artifacts" / "settings.cfg"
    if settings.exists():
        return
    settings.parent.mkdir(parents=True, exist_ok=True)
    settings.write_text(f"master_volume={START_VOLUME:.3f}\n", encoding="utf-8")
    ok(f"MW2 volume starts at {START_VOLUME:.0%}")


def cmd_play(args) -> bool:
    """Everything needed for a test run, from one command."""
    running = rimworld_running()
    if running:
        step("Build RimWorld mod")
        warn("RimWorld is open, so the mod can't be rebuilt (its DLL is in use). "
             "Close RimWorld and run play again to pick up mod changes.")
    else:
        cmd_build_mod(args)
    cmd_engine(args)
    if not running and not args.no_rimworld:
        step("Start RimWorld")
        if platform.system() == "Windows":
            os.startfile(f"steam://rungameid/{RIMWORLD_STEAM_APP}")
            ok("asked Steam to start RimWorld; load a save once it is up")
        else:
            info("start RimWorld yourself and load a save")
    step("Start the engine on rimworld:flat")
    default_volume()
    info("Close the game window or press Ctrl+C here to stop.")
    try:
        run_logged(["cargo", "run", "-p", "launcher", "--profile", "play", "--", "map", "rimworld:flat"],
                   engine_dir(), LOGS / "engine.log", watch=stop_after_rimworld)
    except KeyboardInterrupt:
        info("stopped")
    finally:
        copy_rimworld_log()
    return True


# Each play run's output, for working out what went wrong afterwards.
LOGS = REPO / "logs"
RIMWORLD_LOG = Path(os.path.expandvars(r"%USERPROFILE%")) / "AppData" / "LocalLow" / \
    "Ludeon Studios" / "RimWorld by Ludeon Studios" / "Player.log"


# Seconds between looks at whether RimWorld is still open, and how many looks in a row it
# must be gone before play stops the engine (the engine closes itself first, normally).
RIMWORLD_POLL = 3
RIMWORLD_GONE_POLLS = 3


def stop_after_rimworld(process: subprocess.Popen) -> None:
    """Stops the engine once RimWorld has been open and then closed, so play ends with the
    games (the engine closes itself when RimWorld goes; this is the fallback)."""
    if platform.system() != "Windows":
        return
    seen, gone = False, 0
    while process.poll() is None:
        time.sleep(RIMWORLD_POLL)
        if rimworld_running():
            seen, gone = True, 0
            continue
        gone += 1
        if seen and gone >= RIMWORLD_GONE_POLLS and process.poll() is None:
            info("RimWorld has closed: stopping the engine")
            process.stopped_with_rimworld = True
            subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"],
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            return


def run_logged(cmd: list[str], cwd: Path, log: Path, watch=None) -> None:
    """Like run(), but everything the command prints also goes to log. watch, if given,
    runs beside the command on a thread with its process."""
    info("$ " + " ".join(cmd) + f"   (output also in {log})")
    log.parent.mkdir(parents=True, exist_ok=True)
    with log.open("w", encoding="utf-8", errors="replace") as out:
        process = subprocess.Popen(cmd, cwd=cwd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                                   text=True, encoding="utf-8", errors="replace")
        if watch:
            threading.Thread(target=watch, args=(process,), daemon=True).start()
        try:
            for line in process.stdout:
                sys.stdout.write(line)
                out.write(line)
                out.flush()
        finally:
            code = process.wait()
    if code != 0 and not getattr(process, "stopped_with_rimworld", False):
        raise SetupError(f"command failed with exit code {code}")


def copy_rimworld_log() -> None:
    """RimWorld's own log (Player.log) beside the engine's, when there is one."""
    if platform.system() != "Windows" or not RIMWORLD_LOG.exists():
        return
    LOGS.mkdir(parents=True, exist_ok=True)
    try:
        shutil.copyfile(RIMWORLD_LOG, LOGS / "rimworld.log")
        ok(f"logs are in {LOGS}")
    except OSError as e:
        warn(f"could not copy RimWorld's log: {e}")


def cmd_all(args) -> bool:
    healthy = cmd_doctor(args)
    if not healthy:
        warn("doctor found problems; continuing with the steps that can run")
    results = [cmd_references(args), cmd_engine(args), cmd_decompile(args), cmd_defs(args)]
    return all(results)


COMMANDS = {
    "doctor": cmd_doctor,
    "references": cmd_references,
    "engine": cmd_engine,
    "decompile": cmd_decompile,
    "defs": cmd_defs,
    "all": cmd_all,
    "build-mod": cmd_build_mod,
    "test-mod": cmd_test_mod,
    "link-mod": cmd_link_mod,
    "play": cmd_play,
}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("command", choices=COMMANDS)
    parser.add_argument("--force", action="store_true", help="redo a step that already ran")
    parser.add_argument("--rimworld", help="RimWorld install folder (the one containing Version.txt)")
    parser.add_argument("--no-rimworld", action="store_true", help="play: don't start RimWorld")
    args = parser.parse_args()
    try:
        return 0 if COMMANDS[args.command](args) else 1
    except SetupError as error:
        fail(str(error))
        return 1


if __name__ == "__main__":
    sys.exit(main())
