# Notes for coding agents

Read `docs/ARCHITECTURE.md` first, then `docs/ROADMAP.md` for the current milestone.

- RimWorld is the source of truth. IW4L renders and reports; it never decides NPC outcomes.
- Never commit game files, decompiled RimWorld code, or anything under `reference/` or `engine/`.
  Read them freely: `../reference/rimworld/decompiled` is the RimWorld source to search.
- Engine changes go in `engine-patches/` as patches against the commit in `versions.json`.
- Protocol changes: update `bridge/crates/protocol`, `protocol/PROTOCOL.md`,
  `protocol/examples.ndjson` and the C# side together; `cargo test` in `bridge/` must pass.
- RimWorld state is only touched on the main thread (`BridgeGameComponent`), never from the socket thread.
- Build checks: `python scripts/setup.py build-mod` and `cargo test` in `bridge/`.
  `python scripts/setup.py test-mod` also builds the mod and runs its tests in `tests/`.
- Offline only: never connect modded clients to online services.
