# Engine patches

Our changes to the IW4L engine base live here as `git diff` patches, numbered in the order
they apply (`0001-bridge-plugin.patch`, `0002-...`). `scripts/setup.py engine` clones the
pinned engine commit into `engine/` and applies every patch in this folder.

Why patches instead of a fork: anyone can rebuild the exact engine from the pinned commit plus
this folder, and bumping the engine commit shows exactly which of our changes need updating.

`setup.py` lists the patches it applied in `engine/.rwmw2-patches`, because a later patch can
change the lines around an earlier one so the earlier one no longer reverse-checks.

Making a new patch: `git diff` in `engine/` shows every applied patch together, so diff against
a scratch copy that already has the earlier patches (`add -N` makes new files show up;
`Cargo.lock` is left out because Cargo updates it on the next build):

    git -C engine worktree add --detach ../scratch HEAD
    # in ../scratch: apply the existing patches, commit them there, copy your changed files in
    git -C ../scratch add -N .
    git -C ../scratch diff -- . ":!Cargo.lock" > engine-patches/0003-short-name.patch
    git -C engine worktree remove --force ../scratch

## Patches

- `0001-rimworld-bridge-plugin.patch`: adds the `rimworld_bridge` crate, which listens for the
  RimWorld mod on `127.0.0.1:47820` (or `RWMW2_BRIDGE_ADDR`), answers `hello` and keeps the latest
  state in the `RimworldLink` resource. Registered for host roles in `bootstrap/src/plugins.rs`.
- `0002-rimworld-flat-floor.patch`: adds the `rimworld:flat` map: a flat floor of 250 by 250
  cells (one block per RimWorld cell, `RWMW2_FLAT_SIZE` to change) with a low wall around it and
  an orange cell in the middle at map origin, a darker grid line every 10 cells. It is made in
  code by the new `rimworld_world` crate and drawn and collided through the Minecraft world's
  block renderer and `sim::voxel`, on `mp_rust` as the stand-in map, so it needs no Minecraft
  files. Spawns land on the floor; anyone who falls off is put back. Small changes elsewhere:
  `assets::minecraft_map` lists the zone and treats it as a block world (no map ambience, no
  time limit), `minecraft_terrain::mesh::Atlas::from_pixels`, and a `MinecraftWorldUpdate`
  system set to order against.
- `0003-rimworld-content-namespace.patch`: lets the launcher accept the `rimworld:` map
  namespace, which needs MW2's own content as `minecraft:` does.
- `0004-flat-floor-start-minimap-warnings.patch`: on `rimworld:flat` the match starts at once
  (no waiting for players, no countdown, as `force_match_start`) and the minimap shows the floor
  (`MinecraftUi::floor_minimap`). Also quiets four compiler warnings in the engine base
  (`render_frame`, `skate-core`, `skate-host`).
- `0005-rimworld-pawn-figures.patch`: M1 ghosts. `rimworld_bridge` keeps the latest `snapshot`
  in `RimworldLink` (map size and pawns); on `rimworld:flat` the floor takes RimWorld's map size
  and every pawn stands on it as a placeholder figure (blue yours, red hostile, yellow neutral,
  brown animals; lying down when downed). Also marks the Minecraft sky's position `@invariant`,
  which wgpu warned about.
- `0006-rimworld-play-a-colonist.patch`: M2, step one. When a colonist is drafted in RimWorld
  the local player plays as them: put on their cell with their figure hidden, and their position
  sent back as `player_move` through the new `RimworldOutbox`. When RimWorld holds the colonist
  back (a wall, the map edge) the player is put back on them. `FlatFloor::rimworld_to_map` and
  `map_to_rimworld` convert positions. The terminal now prints RimWorld's tick once, not every
  second.
- `0007-rimworld-tracers.patch`: M2, step two. `rimworld_bridge` queues RimWorld's `shot`s in
  `RimworldLink::shots`; on `rimworld:flat` each one flies as a yellow tracer from the gun to
  where the bullet lands, at the bullet's speed.
- `0008-rimworld-your-gun-and-shots.patch`: M2, step three. The player holds only the MW2 version
  of their colonist's RimWorld gun, or nothing (new `SimWorld::set_only_weapon`, as GSC's
  `takeAllWeapons` + `giveWeapon`). Figures are `sim::voxel` mob boxes, so the player's bullets
  stop on them; each bullet goes to RimWorld as a `player_shot` with the pawn, head or body, and
  MW2's damage.
- `0009-rimworld-client-holds-your-gun.patch`: fixes 0008. The client picks the gun its
  commands ask for, so handing the gun on the server alone left MW2 showing the old one; the
  floor now sets the client's `WeaponSelect` to the colonist's gun too.
- `0010-rimworld-only-your-colonists-gun.patch`: on Minecraft maps every player is handed the
  Intervention, SPAS-12 and UMP45 each tick (`item::sustain_block_world_arsenal`), so an
  unarmed colonist still had the UMP45. New `sim::voxel::set_own_guns_only` exempts the player
  while they play a colonist.
- `0011-rimworld-terrain.patch`: M3, step one. `rimworld_bridge` keeps RimWorld's `terrain` in
  `RimworldLink::terrain`; the floor builds every cell from it (ground tiles, then columns: rock,
  walls, trees, cover, door lintels), rebuilds only the chunks around changed cells, colours the
  minimap by it, and spawns players on open cells.
- `0012-rimworld-trees-and-textures.patch`: trees become a thin trunk (a new `sim::voxel` shape)
  under a block of leaves nothing collides with. Where the engine has fetched Minecraft's files
  for its Minecraft map, the floor's tiles are Minecraft's block textures (dirt, sand, gravel,
  planks, water, stone, stone bricks, packed mud, oak log, leaves); otherwise the plain ones.
- `0013-rimworld-bullets-stop-at-walls.patch`: a RimWorld bullet that missed ends its tracer at
  the first wall, rock or map edge on its path, skipping walls next to the shooter (they lean
  past a corner). Hits fly all the way, since RimWorld only lands those along a clear line.
- `0014-rimworld-mw2-textures.patch`: the floor's blocks wear MW2's own textures, read from the
  game's `.iwd` files under `IW4L_GAMES` (dirt, sand, gravel, concrete floor, cliff rock, stone
  walls, sandbags, pine bark, wood, pine branches as cut-out leaves, a concrete outer wall). Each
  texture spans 4 by 4 blocks and runs on across them. Where MW2 has none (water), Minecraft's
  texture is used, then the plain tile. Atlas tiles are now 512 pixels, 8 to a row.
- `0015-rimworld-mw2-soldiers.patch`: people on the floor are MW2 soldiers: the map's soldier
  body and head (one side's for colonists, the other's for raiders) playing MW2's stand, run or
  prone animation, picked by name. `render_anim` gains `ClientSceneModel`, a script model a
  client system places itself with no server entity, so the snapshot does not hide it. Animals
  and mechs stay box figures, as does everyone if the map has no soldier model.
- `0016-rimworld-forest-and-spectate.patch`: the floor stands in for Estate (`mp_estate`), for its
  forest; `RWMW2_STANDIN_MAP` in `engine\target\play\.env` names another (such as
  `mp_overgrown` where installed; Rust if the one named is missing). RimWorld's trees and bushes
  (`plants`) become Estate's MW2 pines, birches, oaks and shrubs, sized by growth, the nearest 500
  within 60 cells of the view; their blocks then keep only the trunk's collision. Grassy cells
  wear MW2's `grass_spotted01_col`. Figures and soldiers face the way they walk, diagonals too,
  and turn smoothly. With no colonist drafted the player spectates: empty handed, flying free
  (WASD, Space up, C or Ctrl down, Shift faster) with the sim's movement off for them.

- `0017-rimworld-shells-and-explosions.patch`: RimWorld's mortar shells, grenades and rockets
  (`shell`) fly as streaks along RimWorld's path and timing, arcing high for mortars, with an
  MW2 incoming whistle before a mortar lands. Each RimWorld explosion (`explosion`) plays an MW2
  blast with its sound: grenade sized for small ones, airstrike sized from 2.5 cells, smoke,
  flash or EMP looks for those damage kinds. Visual only; RimWorld does the damage. The engine
  log line `rimworld: explosions` names the weapons and whistle sounds it found.

- `0018-rimworld-loot.patch`: looting. While a colonist is played, RimWorld sends what it has
  and what lies on the cells around it, corpses' and downed pawns' gear included (`loot`). A hint
  shows when something is in reach; F opens an MW2-style panel (the Minecraft inventory's chrome,
  made shareable in `hud`): the colonist's kit and load on the left, what is nearby on the right,
  guns with their MW2 kill icons. A click takes (`loot_take`) or drops (`loot_drop`); RimWorld
  does it with its own calls: weapons go in hand, apparel is worn, the rest is carried up to
  the colonist's capacity. The panel frees the mouse like the inventory (`console`, `reticle`).

- `0019-rimworld-all-mw2-models.patch`: the stand-in map's own props (cars, crates, paint
  buckets) are left off the RimWorld floor; they hung at that map's heights. The models and
  textures of every other MW2 multiplayer map installed are loaded too, so RimWorld things can be
  drawn with any of them; the stand-in's own win on a name clash. This makes loading slower and
  uses more memory; `RWMW2_ALL_MAPS=0` in `engine\target\play\.env` turns it off, and the log
  line `rimworld: added N models from M other maps` says what came in. Trees and bushes now draw
  up to 4000 at once within 200 cells (`RWMW2_PLANT_MODELS`, `RWMW2_PLANT_REACH` to change).

- `0020-rimworld-guns-in-hands.patch`: each soldier holds the MW2 version of its pawn's gun in
  its right hand (the same RimWorld-to-MW2 gun table the player's colonist uses); unarmed and
  downed pawns hold nothing. On the RimWorld floor every common MW2 model is kept, not only those
  the map's scripts name. Palms and cacti use the first such MW2 foliage any loaded map has. The
  log line `rimworld: soldiers hold <gun> as <model>` says which model each gun got.

- `0021-rimworld-lazy-other-maps.patch`: loading with every MW2 map is fast again. From the other
  maps only models whose names contain a listed word are taken (foliage, tree, palm, cact, bush,
  shrub, sandbag, barrier, crate, barrel, chair, table, bed, couch, sofa, shelf, lamp, fence), with
  just the materials and textures they use. `RWMW2_EXTRA_MODELS` in `engine\target\play\.env`
  adds words, comma separated, or `*` for every model. Their shader pipelines are built the first
  time one is drawn instead of before the map opens, so a new model can appear a few frames late.

- `0022-rimworld-models-roofs-mountains.patch`: the map as RimWorld builds it (needs the mod from
  the same version, which sends `roofs`, `textures`, `skins` and `buildings`). Built roofs are a
  thin slab on the walls, and rooms under them are dimmer. Caves are rock overhead, dark inside.
  Mountains rise with the distance from their edge, as high as the world tile is hilly: RimWorld
  stores no height for its rock, only thin and thick rock roofs, so the height is made up from
  those. Walls, doors, smoothed stone and built floors wear RimWorld's own art, which the mod saves
  as PNGs under RimWorld's save folder (`RimWorldMW2\textures`) and the engine reads from there.
  Everything else built is a model of boxes for what it is (beds with a headboard, shelves,
  tables and chairs on legs, armchairs, couches, dressers, end tables, lamps, wall lamps, heaters,
  coolers, vents, batteries, solar panels, hydroponics, sculptures, graves, turrets, sandbags,
  barricades, fences linked to each other, spike traps, columns, work benches; anything else a
  box as tall as its cover), wearing its RimWorld picture as RimWorld lays it over the thing.
  Low models (beds, chairs) can be stepped onto. Also: the stand-in map's loose props (planters,
  traffic cones) are gone, and the arms and gun no longer flicker from the stand-in map's lighting.

- `0023-rimworld-scale-sides-animals-items.patch`: Ryan's 0022 notes (needs the mod from the same
  version). Each RimWorld cell is now 2 blocks across, and sprinting in MW2 moves exactly as fast
  as an uninjured drafted colonist walking to a point (4.6 cells a second, scaled by the held
  gun's MW2 move speed), slowed in trees as RimWorld slows pawns there. Trees no longer block;
  their trunks are taller so you see under the leaves. Beds, chairs and traps no longer catch you.
  The stand-in map's kill volumes (`trigger_hurt`, which killed the player over and over at
  spawn) are left out. Wall sides wear the wall's own front face (RimWorld's piece open to the
  south, cropped to the face) instead of the top-down picture. Animals and mechs are their
  RimWorld pictures, facing you by their heading; items on the ground lie flat as their
  RimWorld pictures; anything still being built is not drawn. The spike trap is a board of
  stakes.

- `0024-rimworld-dressed-soldiers.patch`: people's MW2 soldiers wear their RimWorld look (needs
  the mod from the same version, which sends `looks`). While a RimWorld floor loads, each
  soldier kit's body and head get 24 copies sharing their geometry, each with its own colour
  maps (`MapXModelSceneCatalog::retexture`; script models now take their materials by the
  name they are composed under). Each person RimWorld describes takes a copy, whose pictures
  are MW2's repainted: skin where the face and hands show skin and on anything bare, every
  other part in the colour of the outermost thing worn over it, as RimWorld draws it, keeping
  MW2's shading. The engine log line `rimworld: dressed soldier slots` lists the pictures.
- `0025-rimworld-dodgeable-bullets.patch`: RimWorld's bullets can miss the player (needs the mod
  from the same version, which stops RimWorld's own bullets striking the played colonist and
  sends each bullet's `id`). Every bullet in flight is checked each frame against the player's
  body, as tall as MW2 has them standing, crouched or prone; a bullet RimWorld aimed at the
  played colonist flies at the player's middle and on past, until rock, a wall, cover, a
  machine, a lintel or a roof stops it. One that meets the player stops there and is sent as
  `player_struck`; the engine log says `[rimworld] struck by <bullet> (<part>)`.

- `0026-rimworld-in-mw2.patch`: RimWorld inside MW2 (needs the mod from the same version, which
  sends `view` and its window). The minimap shows RimWorld's own camera around the player
  instead of the floor's map, north up and as wide as RimWorld shows (the floor's map comes
  back if the pictures stop). Tab brings RimWorld's window up over MW2's, sized to it, and
  tells RimWorld which window to go back to, instead of opening the scoreboard. The Escape
  menu's items that name the map, the game type, the teams, the classes or muting other
  players are hidden, and the engine log line `rimworld: escape menu` lists them.

- `0027-rimworld-look.patch`: RimWorld's own art in place of MW2's stand-ins (needs the mod
  from the same version, which sends `plant_art`, people's pictures and the art of every kind
  of ground). Floors repeat every four cells instead of two (`RWMW2_FLOOR_SPAN`); the atlas
  stops shrinking while RimWorld's pictures are still two pixels, so wall tops no longer blur
  into their neighbours; people RimWorld drew stand as those pictures rather than MW2 soldiers
  (`RWMW2_RIMWORLD_PAWNS=0` keeps the soldiers); trees, bushes and grass stand as RimWorld's
  pictures, sized by growth, instead of MW2's tree models (`RWMW2_RIMWORLD_PLANTS=0`), with
  grass scattered over the grassy cells near the player; the spike trap lies flat.

- `0028-rimworld-doors.patch`: doors that open as the played colonist walks up to them (needs
  the mod from the same version, which sends `doors`). A door RimWorld has not swung halfway
  fills its cell with blocks, so the MW2 player is held back exactly as a pawn is; past
  halfway the blocks go and the door's two leaves are drawn sliding the rest of the way into
  the walls beside it, under the lintel that always stays. Only the chunks around a door that
  crossed over are built again, because doors move far too often to walk the whole floor. A
  door RimWorld says nothing about is the gap under a lintel it was before.

- `0029-rimworld-one-app.patch`: one application, and Ryan's notes from testing 0026-0028
  (needs the mod from the same version). MW2's window hides until RimWorld's "Control in
  MW2" hands it a colonist (`RWMW2_ONE_WINDOW=0` keeps it up) and tells RimWorld its window in
  its `hello`. Colonists are RimWorld's pictures now (an old filter kept MW2 soldiers). E sends
  `use`. A pawn RimWorld hurt flashes red. Sprint runs at the colonist's own speed, injuries
  and game speed included (up to 6x), with no limit (the Marathon perk, through a new
  `set_unlimited_sprint` on the sim carrier). Lamps light roofed rooms from RimWorld's glow
  grid; wall lamps hang on their wall. Things on the ground are smaller, and on a shelf stand
  on its boards. On RimWorld's map Escape opens a menu of our own (Resume, Options, Quit,
  `ui/menus/rimworld.json`) instead of MW2's class menu, and the scoreboard and the score
  bottom left never show.

- `0030-rimworld-test-fixes.patch`: Ryan's notes from testing 0029 (needs the mod from the
  same version). The minimap shows RimWorld (its picture was registered under the wrong
  sampling, so only the arrow drew). Taking control makes MW2 borderless fullscreen
  (`RWMW2_FULLSCREEN=0` keeps the window). MW2's class and team prompts never open on
  RimWorld's map; the class choice answers itself. People are as tall as an MW2 soldier,
  animals and mechs twice as big as before, and the downed lie flat. New pictures reach the
  engine before what uses them, and loading them or a change of light no longer rebuilds the
  whole floor. Grass sits on a grid fixed to the map, stone chunks lie flat, trees are taller,
  and only plants near the eye are drawn. A shut door is its two leaves (the middle of
  RimWorld's door picture) under a lintel of the wall beside it. Cover (sandbags, furniture,
  beds) is walked through, as RimWorld's pawns walk through it.

- `0031-rimworld-sizes-minimap.patch`: Ryan's notes from testing 0030 (needs the mod from the
  same version). Animals and mechs are drawn at the floor's own scale (2 blocks a cell) and
  grow by the same factor people grow to stand as tall as the player, so they keep RimWorld's
  size against people. The player is put back on their colonist only when RimWorld really
  held it back (far from everywhere the player was in the last 1.5 s), not because snapshots
  trail a fast player. RimWorld's minimap is three times the size, north up, without the
  arrow, in RimWorld's own colours (the picture was decoded from sRGB once too often).

- `0032-rimworld-fidelity.patch`: Ryan's notes from testing 0031 (needs the mod from the same
  version). Everything of MW2's match that is not RimWorld is gone on RimWorld's map: the
  "Free for all" and "Show no mercy" splash, team emblem, score popups, kill feed, player
  card, announcer, compass ring and grenade slots, and the loading screen says RimWorld. The
  minimap is RimWorld's camera at full resolution, shared through a file the mod writes up
  to 60 times a second instead of a JPEG ten times a second, north up the right way, twice
  the size. Wide doors are shut on every cell, people lie in beds and are carried, smoothed
  stone walls are full height, doorways are lit and roofed as their room, and the atlas holds
  1536 of RimWorld's pictures and grows as new ones come. Crops stand on their basins and
  planters; statues, thrones, lecterns, instruments, altars and poppers stand as RimWorld's
  pictures; models' sides take their picture's colour. The colonist's gun fires its RimWorld
  burst, then reloads and holds fire for RimWorld's cooldown and aiming time (its reserve
  never runs out). Water is smooth ripples, and deep water holds the player back.

- `0033-rimworld-overlay-guns-floors-statues.patch` (M16): RimWorld's own window is the
  minimap (the mod puts it borderless and on top in MW2's top left corner while a colonist
  is played and sends `overlay`; MW2's minimap hides meanwhile). The ammo counter shows the
  RimWorld burst as the clip and MW2's low ammo and reload hints are gone; a reload takes
  RimWorld's cooldown plus aiming time, through MW2's own reload with its timer and
  animation scaled. Floors use RimWorld's scale, one texture across 16 cells (a paved tile a
  cell): the most common 12 ground kinds whole in big atlas tiles, the rest cut to 4 cells.
  A wide door (the security door) is one door, its leaves across all its cells. Dead pawns
  lie where they fell and are not shot at. Statues and other art stand as 3D bodies cut from
  their pictures; workshop benches, machines, gene machines, tanks, consoles, thrones,
  lecterns, altars, instruments and poppers are models of what they are.
  `RWMW2_FLOOR_SPAN` is gone.

- `0034-rimworld-minimap-corner-path-costs.patch` (M17): the player is slowed on each cell as
  much as RimWorld's path cost slows a pawn there (`costs`, sent by the mod: furniture,
  trees, mud, snow; capped at RimWorld's 450 ticks a cell). Models' fronts wear their
  picture as RimWorld's slanted view shows them; heaters, tool cabinets, coolers and vents
  wear it whole up the front with a plain top; end tables are bedside tables.

- `0035-rimworld-tab-tells-rimworld-first.patch` (M17): Tab tells RimWorld the player asked
  for it before bringing its window up, so RimWorld, while it is the minimap, does not hand
  the focus straight back to MW2.

- `0036-rimworld-follow-pause-hits-carry-models.patch` (M18): the player is held still, view
  and all, while RimWorld is paused (`snapshot`'s `paused`); RimWorld hears when MW2's Escape
  menu opens and closes (`menu`), so its minimap window hides from in front of it. A bullet
  tells RimWorld where it struck: `head`, `neck`, `upper`, `lower` or `legs`, measured on
  RimWorld's own picture of a person (mostly head) or MW2's soldier. The ammo number is the
  rounds left in the clip. Heaters smaller with their picture on all four sides; dressers
  with their drawers on the front; sleep accelerators, electric inhibitors, drapes and wind
  turbines modelled; crops a little smaller; statues a few tall boxes of one depth.

- `0037-rimworld-cursor-held-in-middle.patch` (M19): on Windows, while play holds the mouse on
  RimWorld's map, the cursor is put back in the middle of MW2's window every frame, so it
  cannot wander onto RimWorld's minimap window or off the screen and click into something else.

- `0038-rimworld-effects-grenades.patch` (M20): RimWorld's effects with MW2's own. Thrown
  grenades fly as RimWorld's picture of them, lobbed as high as RimWorld lobs them, and lie
  where they land until they go off. Fire or G throws the played colonist's grenade where the
  player aims (`player_throw`). Fires (`fires`) burn as MW2 fire, big ones smoking; gas
  (`gas`) hangs as MW2 smoke; firefoam, molotov, EMP, smoke and stun blasts get their own MW2
  effects (foam and flames without a bang); RimWorld's shots flash at the muzzle and kick up
  dirt, wall dust or blood where they land. The effects system plays them from
  `frame::RimFx`, trying a list of MW2 effect names and logging which it found.

- `0039-rimworld-weather-filth-flecks.patch` (M21): RimWorld's weather and mess. Rain streaks
  and snowflakes fall around the eye as hard as `weather` says, slanted by the wind and kept
  off by roofs; rain, snow and fog close in MinecraftOSS's fog and grey the sky, and thick fog
  lays MW2's ground fog effects in the open. `filth` lies flat as RimWorld's own see-through
  pictures, and `flecks` fly, turn, grow and fade at the game's speed, all in the translucent
  entity mesh. MW2 closes itself once RimWorld's window has gone and the bridge is down (a
  hidden MW2 window stalled its loop, so it never noticed and play.bat kept running).

- `0040-rimworld-only-rimworld-pictures.patch` (M22): pawns are only ever drawn as RimWorld's
  pictures of them. MW2 soldiers are off (`MW2_SOLDIERS`) and the coloured box figures are
  gone; a pawn whose picture has not arrived or loaded is not shown, and one whose new
  picture is loading keeps its old one. `RWMW2_RIMWORLD_PAWNS` is gone.

- `0041-rimworld-held-weapons-melee-swings.patch` (M23): the snapshot's `held` weapon stands
  at the pawn's hands as RimWorld's picture, pointing where it aims as seen from the eye
  (mirrored to point left, down when towards the eye), turned by the weapon's own offset;
  `swings` going up plays a melee swing (raised to struck) and a lunge at the target.

- `0042-rimworld-ce-ammo.patch` (M24): under Combat Extended, `player_gun`'s `mag` is the
  player's clip (still capped at the MW2 gun's own clip), never holding more than RimWorld has
  `loaded`; fire is held for the cooldown and aiming time after each burst, the reload key
  does nothing until the clip is empty, and a reload takes CE's `reload` plus the aiming
  time. A dry gun (nothing `loaded`, `spare` 0) has an empty clip and reserve.
- `0043-rimworld-art-starts-over.patch` (M25): when RimWorld's `textures` list starts over
  (the atlas was nearly full), pawns drop the pictures they were shown by until RimWorld sends
  theirs again. Independent of 0042 (pull request #3), which it applies with or without.
- `0044-rimworld-ce-ammo-sync.patch` (M24, after 0042): MW2's clip is exactly what RimWorld
  has `loaded` and its reserve the `spare` ammo carried, so the two counts match. The player
  can reload at any time; starting a reload sends `player_reload`, and RimWorld reloads first,
  so the reload ends with the clip at RimWorld's count. A shotgun's extra pellets go out as
  `player_shot` with `pellet`, using no ammo.

If the engine changes grow large, switch to a real fork and point `versions.json` at it.
