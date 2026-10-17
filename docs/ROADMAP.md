# Roadmap

Get something working end to end, then iterate. Prefer the simplest thing that runs.

## M0: hello socket
- [x] RimWorld mod connects, sends `hello` and a `tick` once per second
- [x] `bridge-echo` stand-in prints them and answers `hello`
- [x] Verify in a real RimWorld game (load a save with the mod and run `cargo run -p bridge-echo`)
- [x] Bridge plugin inside the engine (`engine-patches/0001`) printing the same
- Note: RimWorld pauses when its window loses focus. Turn on Options > General > Run in background,
  or have the mod set `Prefs.RunInBackground` while the bridge is connected.

## M1: ghosts
RimWorld pawn positions show up as placeholder models in IW4L. Use a flat or generated map, not
an existing MW2 map, so positions line up from the start. Try driving bots with the engine's
existing `bot tp` / `bot give` / `bot hold` console commands before writing new entity code.

- [x] Flat map `rimworld:flat` (engine patch 0002): one block per RimWorld cell, map origin at
      the middle cell, `FlatFloor::rimworld_to_map` turns a RimWorld position into a map point
- [x] RimWorld mod sends its map size and pawn positions (`snapshot`, 10 a second, paused or not)
- [x] Placeholder figures at those positions (engine patch 0005), floor sized to RimWorld's map
- [x] The mod turns on RimWorld's "Run in background" while connected

## M2: one colonist, one raider
Player controls a drafted colonist from IW4L; its position syncs back. Raider shots render as
tracers along RimWorld's resolved path. Player hits apply RimWorld damage to the reported part.
Decide first what IW4L does while RimWorld is paused (freeze or extrapolate).

- Paused: decided. IW4L keeps running and you can still walk; RimWorld moves the colonist while
  paused too. Shots and damage will only happen while RimWorld runs.
- [x] Draft a colonist and MW2 plays as them (engine patch 0006): the selected drafted colonist,
      else the first drafted one; selecting another drafted colonist switches. You start on their
      cell; your moves walk them in RimWorld one cell at a time, drawn gliding after you; walls
      and the map edge hold them back and put you back on them. The played colonist holds fire
- [x] Every bullet RimWorld fires flies as a tracer in MW2 (`shot`, engine patch 0007)
- [x] You hold the MW2 version of your colonist's gun, or nothing (engine patch 0008)
- [x] Your bullets show in RimWorld and your hits apply RimWorld damage, MW2's damage times 0.3,
      to the head or the body (`player_shot`, engine patch 0008)

## M3: terrain
Generate the IW4L map from the RimWorld grid as block columns (rock and walls tall, sandbags and
trees as cover, floor as ground). Builds on `rimworld:flat`: its blocks and atlas tiles are made
in code, so terrain types become more tiles and taller columns.
- [x] RimWorld sends every cell of the map in view, and again when cells change (`terrain`)
- [x] The floor is built from it: ground tiles; rock 4 blocks, walls and trees 3, cover 1, doors a
      gap under a lintel; changed cells rebuild only their chunks (engine patch 0011)
- [x] Tried on a real colony: rock heights are right (Ryan, 2026-10-05)
- [x] Trees are a thin trunk to walk past under a block of leaves, as pawns walk through them;
      blocks wear Minecraft's textures when the engine has fetched them (engine patch 0012)
- [x] The mod talks to MW2 from its first frame, so a save that loads paused shows up at once
- [x] Blocks wear MW2's own textures where MW2 has one, else Minecraft's (engine patch 0014)
- [x] People are MW2 soldiers standing, running and lying prone (engine patch 0015)
- [x] The floor stands in for Estate, for its forest (Overgrown is DLC; `RWMW2_STANDIN_MAP`)
- [x] Trees and bushes are Estate's MW2 models; grassy ground wears MW2's grass (`plants`, engine
      patch 0016)
- [x] Soldiers face the way they walk, diagonals too, and turn smoothly (engine patch 0016)
- [x] No colonist drafted: spectate, flying free with empty hands (engine patch 0016)
- [ ] More MW2 models: sandbags (Estate has none), animals (MW2 has
      none; they stay figures for now) (Ryan, 2026-10-05: MW2 first, then RimWorld's art,
      Minecraft's last)
- [x] Bullets from RimWorld that miss stop at the first wall in MW2 too (engine patch 0013)

## M4: downed, loot, mortars
Downed pawns go prone. Loot through an MW2-styled inventory that calls RimWorld's own transfer
logic. Mortars resolve in RimWorld, IW4L draws arc, whistle and explosion. Then polish.

Done: downed pawns lie prone (0005); shells, mortar arcs, whistles and MW2 explosions (0017);
looting through an MW2-style panel that calls RimWorld's own equip, wear and carry (0018);
every MW2 map's models and textures loaded at once, stand-in props gone, trees drawn far (0019);
soldiers hold their pawns' guns (0020); only listed models from other maps, their shaders built
on first draw, so loading stays fast (0021).
Next: polish (animals, camera shake).

## M5: the map as RimWorld builds it
Ryan, 2026-10-05, from his Full_TEST save: model what is common (beds, shelves and the rest),
roofs, RimWorld's building materials on walls and floors, caves, taller mountains; no more
floating stand-in props or flickering arms.

- [x] Roofs: a slab over built roofs, rock over caves (0022, mod `roofs`)
- [x] Mountains rise from their edge by the world tile's hilliness; RimWorld keeps no rock
      height, only thin and thick rock roofs (0022)
- [x] RimWorld's art on walls, doors, smoothed stone and built floors (0022, mod `textures`,
      `skins`)
- [x] Box models for furniture, machines, turrets and defences, wearing their RimWorld
      picture (0022, mod `buildings`)
- [x] Stand-in map's loose props gone; arms and gun lit steadily (0022)
- [ ] Better models where boxes fall short (MW2 props where they match, as sandbags)

## M6: Ryan's notes on M5, then the next stage
Ryan, 2026-10-05: wall sides showed the top-down granite; the player sometimes died over and
over in MW2 only; sprint should match a drafted colonist; beds and trees held the player;
models for animals, mechs and the spike trap; hide what is under construction; show items on
the ground.

- [x] No more spawn-die loop: the stand-in map's kill volumes are left out (0023)
- [x] 2 blocks a cell; sprint matches a drafted colonist (4.6 cells a second), slower in trees (0023)
- [x] Wall sides wear the wall's front face, cut from RimWorld's linked art (0023, mod `_l11`)
- [x] Beds, chairs and traps walkable; trees walk-through, taller trunks (0023)
- [x] Animals and mechs as their RimWorld pictures, facing by heading (0023, mod `art`)
- [x] Items on the ground as their pictures (0023, mod `items`); frames and blueprints hidden
- [x] Spike trap as a board of stakes (0023)
- [ ] Real 3D models for animals and mechs (none in MW2; built from RimWorld art)
- [ ] Camera shake from nearby explosions

## M7: people in RimWorld's clothes
Ryan, 2026-10-06: the soldiers should wear RimWorld's textures; he chose to keep MW2's soldier
models and animations, dressed in each person's RimWorld colours.

- [x] The mod sends how each person looks (`looks`): skin, hair, and what they wear over which
      body parts (0024)
- [x] The loader makes 24 copies of each soldier kit that share its geometry but wear pictures
      of their own; each person gets one, repainted part by part (by the bones each texel's
      surfaces follow): skin on bare parts, face and hands, each part in the colour of the
      outermost thing worn over it as RimWorld draws it, MW2's shading kept (0024)
- [ ] People past 24 at once wear the kit as it is

## M8: dodging RimWorld's bullets
The handoff's "NPC shooting (later)": RimWorld picks when each raider fires and where it aims;
MW2 decides whether the bullet strikes the player.

- [x] Every bullet carries an `id`; RimWorld's own bullets no longer strike the colonist the MW2
      player plays (0025)
- [x] IW4L flies every bullet against the player's body (MW2's standing, crouched or prone
      height): a bullet aimed at the player goes at their middle and on past, stopped by rock,
      walls, cover, machines, doors' lintels and roofs; one that meets the player is a
      `player_struck`, and RimWorld deals that bullet's damage, to the head when it struck there
      (0025)
- [ ] Show being struck in MW2 (a flinch, blood on the screen, a sound)
- [ ] Grenades and rockets the player can run from (they already land where RimWorld says)

## M9: RimWorld inside MW2
Ryan, 2026-10-06: the minimap is the RimWorld game, and Tab opens RimWorld itself instead of
MW2's scoreboard. MW2's Escape menu keeps only what this game has.

- [x] RimWorld's camera follows the MW2 player while MW2 has the focus: the colonist they play,
      else where they fly (`spectate`), and what it shows goes to IW4L as `view` ten times a
      second, RimWorld's own interface left out (0026)
- [x] MW2's minimap is that picture, north up, around the player (0026)
- [x] Tab on the floor brings RimWorld's window up over MW2's and leaves it fully playable;
      Tab in RimWorld goes back to MW2 (0026)
- [x] The Escape menu drops the map, the game type, the teams, the classes and muting other
      players, keeping the options and the way out (0026)
- [ ] Tab drawn inside MW2 instead of switching windows, if Ryan would rather have that

## M10: RimWorld's own look
Ryan, 2026-10-06: "I think I want to go very stylized RimWorld and have it kind of have that
2D charm to it", so MW2's stand-ins give way to RimWorld's own art wherever they can.

- [x] Every kind of ground carries RimWorld's art, not only built floors: RimWorld's soil
      instead of MW2's grass, with RimWorld's grass standing on it (0027)
- [x] Floor art spans four cells before it repeats, not two, so it is not so fine
      (`RWMW2_FLOOR_SPAN`) (0027)
- [x] RimWorld's pictures no longer blur into each other on wall tops seen from far off: the
      atlas stops shrinking while each picture is still two pixels (0027)
- [x] People are drawn as RimWorld draws them, facing north, east and south, clothes and hair
      and all, instead of MW2 soldiers (`RWMW2_RIMWORLD_PAWNS=0` for the soldiers) (0027)
- [x] Trees, bushes and grass are RimWorld's own pictures standing on their cells, sized by
      how grown they are, instead of MW2's tree models (`RWMW2_RIMWORLD_PLANTS=0`) (0027)
- [x] The spike trap lies flat, as RimWorld draws it (0027)
- [x] Doors open as a colonist walks up, as RimWorld's do: the mod tells the door a pawn is
      coming, RimWorld swings it, and MW2 holds the player back until the way is clear (0028)
- [ ] Rooves and walls seen from inside

## M12: one application
Ryan, 2026-10-06, after testing 0026-0028: one application that starts in RimWorld; drafting
is still RimWorld; a button on a drafted colonist hands them to MW2, and the same button
(from Tab) takes them back.

- [x] "Control in MW2" / "Stop controlling" on drafted colonists; MW2's window hides while
      nobody is controlled (`RWMW2_ONE_WINDOW=0` keeps it) (0029)
- [x] Colonists really are RimWorld's pictures (0027 still kept MW2 soldiers for people) (0029)
- [x] The minimap: RimWorld draws its camera into the picture on purpose instead of copying
      its screen, which Windows stops drawing while MW2 covers it (not yet confirmed) (0029)
- [x] Escape: Resume, Options, Quit only; no scoreboard, no score bottom left (0029)
- [x] E opens or shuts the door in front (any door the colonist may open, not only
      autodoors) and flicks switches (0029)
- [x] Wall lamps hang on their wall; lamps light roofed rooms from RimWorld's glow (0029)
- [x] People and animals flash red when hurt (0029)
- [x] Smaller things on the ground; on shelves they stand on the boards (0029)
- [x] Beds and other two-colour art in their RimWorld colours (mask applied) (0029)
- [x] Sprint as fast as the colonist runs, injuries and game speed (up to 6x) included,
      without running out (0029)
- [ ] Missing limbs drawn: RimWorld's own pictures don't show them, so this needs our own
      drawing (stretch)
- [x] `play.bat` keeps logs in `logs/` (engine output, RimWorld's Player.log)

## M13: fixes from testing 0029
Ryan, 2026-10-08.

- [x] The minimap shows RimWorld behind the arrow (0030)
- [x] Taking control fills the screen (`RWMW2_FULLSCREEN=0` keeps the window) (0030)
- [x] No class prompt when MW2 starts playing a colonist (0030)
- [x] Colonists as tall as the player; downed pawns lie on the ground (0030)
- [x] Mech and animal pictures right from the start, not after a second (0030)
- [x] Less lag: plants only near the eye, pictures read once, light changes rebuild only
      what changed (0030); more if raids still stutter
- [x] Grass stays put as you move; stone chunks lie on the ground (0030)
- [x] Taller trees (0030)
- [x] Doors drawn as leaves under a wall lintel (0030)
- [x] Cover and furniture no longer block the player (0030)
- [ ] Mechs: waiting on Ryan to say what is wrong with them

## M14: fixes from testing 0030
Ryan, 2026-10-08.

- [x] Animals and mechs at RimWorld's size against people (0031)
- [x] No rubber banding when running fast or at 3x game speed (0031)
- [x] Minimap: RimWorld's colours, three times bigger showing three times as much, north up,
      no arrow (0031; the mod zooms RimWorld's camera out while MW2 is in front)

## M15: fixes from testing 0031
Ryan, 2026-10-08.

- [x] Strip MW2's match from RimWorld's map: splash, team emblem, score, kill feed, announcer (0032)
- [x] Minimap at RimWorld's own resolution and speed (shared file), the right way up, 2x (0032)
- [x] Wide doors (the security door) shut on every cell (0032)
- [x] Carried pawns, pawns lying in beds (0032)
- [x] Smoothed stone walls as tall as built walls; no dark band at the bottom of walls (0032)
- [x] Floors under doors (0032), crops in planters (0032)
- [x] Thrones, lecterns, statues, poppers and the like as RimWorld's standing pictures (0032; models and 3D statues from 0033)
- [x] Model textures: sides take the picture's colour (0032)
- [x] Guns fire RimWorld's bursts and reload when RimWorld would (0032)
- [x] Water without the block pattern; deep water cannot be entered (0032)
- [x] Pictures for everyone in a raid; baby animals (0032: the atlas had run out of room)
- [ ] Rocks floating: check after 0032 (collapsed rock is rock now; the dark band is gone)

## M16: fixes from testing 0032
Ryan, 2026-10-08 22:07.

- [x] Dead colonists and raiders lie where they fell (0033: corpses in the snapshot as `dead`)
- [x] Floors one RimWorld tile a cell: RimWorld's ground textures span 16 cells (0033; the 12
      most common kinds whole at 512px, the rest cut to 4 cells)
- [x] Minimap: RimWorld's own window, borderless and on top in MW2's top left corner, its
      interface hidden, while a colonist is played (0033; the streamed picture is gone)
- [x] Gun HUD: the clip shows RimWorld's burst, no MW2 low ammo or reload hint (0033)
- [x] Reload takes RimWorld's cooldown plus aiming time; the animation is slowed or sped to match (0033)
- [x] The security door opens as one door, its two leaves across both cells (0033)
- [x] Workshop things made as what they are, one by one: benches, machines, cabinets, gene
      machines, tanks, consoles, thrones, lecterns, altars, instruments, poppers (0033)
- [x] Statues and sculptures as 3D bodies cut from RimWorld's picture, not a picture facing the camera (0033)

## M17: fixes from testing 0033
Ryan, 2026-10-09.

- [x] Minimap much bigger (0.42 of MW2's height) and in the very top left corner, following MW2's
      window as it goes fullscreen (mod only)
- [x] MW2 comes back on a second "Control in MW2": the minimap's resize no longer keeps the focus (mod only)
- [x] Walking through things as RimWorld's pawns do: slowed by RimWorld's own path cost of each
      cell (furniture, trees, mud, snow), held back where pawns cannot go; fences hold people
      back (0034, Ryan chose RimWorld's rule over blocking furniture)
- [x] RimWorld no longer takes over every few seconds: the minimap only goes on Tab, letting the
      colonist go, MW2 hidden or another program in front; RimWorld's menus closed on it; its
      camera on the colonist at once (mod, 0035)
- [x] End tables as bedside tables; couch fronts wear their picture; heaters, tool cabinets,
      coolers and vents wear their picture up the front (0034)

## M18: fixes from testing 0035
Ryan, 2026-10-09.

- [x] Minimap follows the played colonist whatever Unity thinks of the focus, and logs where its
      camera looks; twice as zoomed in (mod)
- [x] Minimap hides while MW2's Escape menu is open (0036 + mod)
- [x] MW2 player held still, view and all, while RimWorld is paused (0036)
- [x] Hit locations: head, neck, chest/arms, belly/hands, legs, from where MW2's bullet struck
      RimWorld's picture of the person (0036 + mod)
- [x] Ammo number is the rounds left in the clip (0036)
- [x] E picks up a downed pawn (RimWorld's drafted "Carry") and puts them down (mod)
- [x] Heater smaller, picture on all sides; dresser drawers on its front; sleep accelerator,
      electric inhibitor, drapes and wind turbine modelled; crops smaller; statues without
      ledge lines (0036)

## M19: fixes from testing 0036
Ryan, 2026-10-09.

- [x] Minimap back after Tab out and in: RimWorld asks Windows which window is in front, as Unity
      said RimWorld had the focus all along (mod)
- [x] Mouse stays in MW2: RimWorld no longer hands MW2 the focus over and over, and the cursor is
      held in the middle of MW2's window (0037 + mod)
- [x] Colonist no longer flickers on the minimap when looking half way between two ways (mod)
- [x] RimWorld closes cleanly after MW2: the bridge connection is closed and its window put back
      on quitting (mod)

## M20: effects, grenades, fullscreen, double doors
Ryan, 2026-10-09 17:32.

- [x] Minimap works when RimWorld is fullscreen: a minimised RimWorld window comes back
      without the focus, goes windowed, and the minimap's size is no longer saved as the
      player's resolution (a 605x605 left in Prefs is repaired at start) (mod)
- [x] Two doors one behind the other: E works the door beyond the one the colonist stands in,
      and a door without power opens for the colonist facing into it, as RimWorld's pathing
      opens it by hand (mod)
- [x] Thrown grenades: RimWorld's own picture, lobbed, lying for their fuse (0038 + mod)
- [x] The MW2 player throws the colonist's grenades: fire or G, where they aim (0038 + mod)
- [x] RimWorld's effects in MW2: fire, gas (smoke poppers, tox, rot stink, deadlife dust),
      firefoam/molotov/EMP/smoke/stun blasts, muzzle flashes and bullet strikes (0038 + mod)
- [x] Rain, snow and fog, filth and flecks: done in M21
- [ ] MW2 effect names to tune once Ryan's engine log says which it found

## M21: weather, filth, flecks, play ends with the games
Ryan, 2026-10-09 18:28.

- [x] play.bat ends when the games close: MW2 closes itself once RimWorld has gone
      (RWMW2_ONE_WINDOW=0 keeps it open), and play stops the engine if RimWorld has been
      closed for about ten seconds (0039 + setup.py)
- [x] The minimap is bigger: half of MW2's height (was 0.42). Its zoom is still RimWorld's own
      (mod)
- [x] Rain and snow fall around the MW2 player as hard as RimWorld's, slanted by its wind, but
      not under roofs or the mountain; rain, snow and fog close in the view and grey the sky;
      thick fog also lays MW2's ground fog in the open (`weather`, 0039 + mod)
- [x] Filth lies where RimWorld draws it, its own pictures see-through: blood, firefoam, fuel,
      vomit, ash, dirt and the rest (`filth`, 0039 + mod)
- [x] RimWorld's flecks near the player (dust puffs, sparks, splashes, footprints, smoke
      puffs) fly, turn, grow and fade as in RimWorld (`flecks`, 0039 + mod). Glowing and
      heat-shimmer flecks are left out

## M22: raiders only as RimWorld pictures
Ryan, 2026-10-09 21:23: a big raid came as MW2 soldiers, RimWorld pictures and coloured boxes
at once. Pawns whose picture RimWorld had not drawn yet (8 new people a snapshot) stood as MW2
soldiers, or as boxes while the soldier models or a picture's texture were not ready.

- [x] Every pawn is only ever RimWorld's picture of it: no MW2 soldiers, no boxes; one not
      drawn yet is not shown until it is (0040)
- [x] A snapshot without pictures keeps the ones a pawn had, and new pictures (downed, new
      clothes) show the old ones until they have loaded (0040)
- [x] The mod draws 24 new people a snapshot (was 8), keeps pictures for 400 (was 200), and
      a person whose portrait RimWorld cannot draw shows their body as drawn on the map; an
      animal's missing side shows another side (mod)
- [x] `RWMW2_RIMWORLD_PAWNS` is gone

## M23: weapons in hand, melee swings
Ryan, 2026-10-12: RimWorld's people should hold the weapon they actually hold when drafted,
with some animation when they attack in melee, and raiders always.

- [x] The weapon in a pawn's hands is RimWorld's own picture of it, where RimWorld draws it:
      aimed at what the pawn aims at or attacks, carried while drafted or fighting, and
      always for enemies; turned to the view as RimWorld turns it on its map (0041 + mod)
- [x] Melee attacks swing the weapon from raised to struck and lunge the attacker at its
      target, bare-handed too, as RimWorld jolts it (0041 + mod)

## M24: Combat Extended ammo for the MW2 player
Under Combat Extended, the MW2 player's shots use the colonist's ammo, and MW2's clip and
reload are CE's.

- [x] Each `player_shot` uses the gun's CE ammo as CE's own shot does (`ammoConsumedPerShot`
      included); with none loaded or carried it does nothing in RimWorld (mod)
- [x] A magazine a shot empties is refilled from the inventory at once, without CE's reload
      job, and CE gives the played colonist no reload jobs (mod)
- [x] `player_gun` reports `mag`, `loaded`, `reload` and `spare`; MW2's clip is the
      magazine, its reload as long as CE's, and a dry gun is empty (0042 + mod)

## M25: room for every picture
Ryan, 2026-10-17: a friendly army that came to the colony was invisible in MW2. RimWorld's
pictures go to MW2 numbered in one list that only grew, and MW2's atlas holds 1536 of them;
late in a long game the newest people (portraits are 3 pictures per look) had no room.

- [x] Near full, the mod starts its list over with only the art in use now and sends
      everything again; at most once a minute (mod)
- [x] People drop their old pictures when the list starts over, instead of showing whatever
      now has their numbers, until RimWorld sends theirs again (0043)

## Balance notes
MW2 fire rates are far above RimWorld bursts. v0 scales per-bullet damage by
RimWorld DPS / MW2 DPS with one tunable multiplier. Weapon map starts as: assault rifle → ACR,
sniper rifle → Intervention, shotgun → SPAS-12.
