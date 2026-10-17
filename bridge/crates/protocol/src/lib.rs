//! Protocol v0: one JSON object per line over a localhost TCP connection.
//! RimWorld connects, the IW4L side listens. See `protocol/PROTOCOL.md`.
//!
//! RimWorld is the source of truth: everything it sends is authoritative state,
//! everything IW4L sends is a request or a report that RimWorld applies.

use serde::{Deserialize, Serialize};

pub const PROTOCOL_VERSION: u32 = 0;
pub const DEFAULT_ADDR: &str = "127.0.0.1:47820";

/// Every message, tagged by its `"t"` field.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
#[serde(tag = "t", rename_all = "snake_case")]
pub enum Message {
    /// First message from each side after connecting.
    Hello {
        side: Side,
        protocol: u32,
        #[serde(default, skip_serializing_if = "Option::is_none")]
        game_version: Option<String>,
        /// The sender's own window (M9+), a Windows window handle in decimal; `"0"` where there
        /// is none. RimWorld's is what IW4L brings up on Tab; IW4L's (M12+) is what RimWorld's
        /// "Control in MW2" brings up.
        #[serde(default, skip_serializing_if = "Option::is_none")]
        window: Option<String>,
    },

    // ---- RimWorld -> IW4L
    /// M0 heartbeat: RimWorld's game tick, once per second at 1x.
    Tick { tick: i64 },
    /// Every pawn on the map the player is looking at, about 10 times a second (M1+).
    Snapshot {
        tick: i64,
        /// The map's size in cells, x then z.
        #[serde(default, skip_serializing_if = "Option::is_none")]
        map: Option<[i32; 2]>,
        pawns: Vec<PawnState>,
        /// The colonist the MW2 player plays as, if any: a drafted colonist (M2+).
        #[serde(default, skip_serializing_if = "Option::is_none")]
        player_pawn: Option<String>,
        /// How fast the played colonist moves now, injuries included, in cells per second of
        /// game time (M12+): RimWorld's MoveSpeed.
        #[serde(default, skip_serializing_if = "Option::is_none")]
        player_speed: Option<f32>,
        /// Game seconds per real second (M12+): 1 at normal speed, 3 fast, 6 superfast; 1
        /// while paused.
        #[serde(default, skip_serializing_if = "Option::is_none")]
        rate: Option<f32>,
        /// How the played colonist's gun fires in RimWorld (M15), so MW2 fires it the same way.
        #[serde(default, skip_serializing_if = "Option::is_none")]
        player_gun: Option<GunTiming>,
        /// RimWorld is paused (M18): MW2 holds its player still, view and all.
        #[serde(default, skip_serializing_if = "std::ops::Not::not")]
        paused: bool,
    },
    /// A shot RimWorld already resolved, sent as its bullet leaves the gun; IW4L only renders
    /// it. `from` and `to` are `[x, height, z]` in cells, `to` where the bullet lands.
    /// M8+: `id` names the bullet. RimWorld lets IW4L decide whether it strikes the colonist
    /// the MW2 player plays: IW4L flies it on past `to` when that colonist was the target, and
    /// sends [`Message::PlayerStruck`] if it meets the player.
    Shot {
        shooter: String,
        weapon: String,
        from: [f32; 3],
        to: [f32; 3],
        result: ShotResult,
        /// The bullet's speed in cells per second at the current game speed (M2+).
        #[serde(default, skip_serializing_if = "Option::is_none")]
        speed: Option<f32>,
        #[serde(default, skip_serializing_if = "Option::is_none")]
        target: Option<String>,
        #[serde(default, skip_serializing_if = "Option::is_none")]
        part: Option<String>,
        #[serde(default, skip_serializing_if = "Option::is_none")]
        id: Option<String>,
    },
    /// What stands on every cell of the map in view (M3+), as one character a cell: `size` is
    /// `[x, z]` and `cells` runs row by row from the south edge (`z` 0), west to east, so cell
    /// `(x, z)` is character `z * size[0] + x`. The codes are in [`terrain`]. Sent when the
    /// bridge connects, when the map in view changes and when any cell changes.
    Terrain { size: [i32; 2], cells: String },
    /// The trees and bushes on the map in view (M3+), so IW4L can stand a model on each.
    /// Sent with `terrain`, when the map in view changes and when a plant comes, goes or grows.
    Plants { plants: Vec<PlantState> },
    /// Something other than a bullet RimWorld fired (M4+): a mortar shell, a grenade, a
    /// rocket, sent as it leaves. `from` and `to` are `[x, height, z]` in cells, `to` where it
    /// lands; it takes `seconds` at the current game speed. `arc` when it flies over
    /// everything, as mortar shells do. What it does when it lands comes as an `explosion`.
    /// M20: `tex` is RimWorld's own picture of it in flight (a cut-out index into `textures`)
    /// and `size` how many cells across RimWorld draws it; `lob` how many cells high RimWorld
    /// lobs it (a thrown grenade's arc); `fuse` how many game seconds it lies where it landed
    /// before it goes off (a frag grenade's), at the current game speed.
    Shell {
        shooter: String,
        weapon: String,
        from: [f32; 3],
        to: [f32; 3],
        seconds: f32,
        arc: bool,
        #[serde(default, skip_serializing_if = "Option::is_none")]
        tex: Option<i32>,
        #[serde(default, skip_serializing_if = "Option::is_none")]
        size: Option<f32>,
        #[serde(default, skip_serializing_if = "Option::is_none")]
        lob: Option<f32>,
        #[serde(default, skip_serializing_if = "Option::is_none")]
        fuse: Option<f32>,
    },
    /// An explosion on the map in view (M4+), as it starts: its middle cell's middle, its
    /// radius in cells and RimWorld's damage type (`Bomb`, `Flame`, `EMP`, `Smoke`, ...).
    /// RimWorld applies its damage; IW4L only shows and sounds it.
    Explosion { x: f32, z: f32, radius: f32, damage: String },
    /// What the played colonist `pawn` has and what lies within its reach (M4+): its `kit`
    /// (the weapon in hand, apparel worn, things carried), the `mass` it carries of its
    /// `capacity` in kg, and `nearby` things to take: what lies on the ground around it, and the
    /// gear of any corpse or downed pawn there. Sent while a colonist is played, when any of
    /// it changes.
    Loot { pawn: String, mass: f32, capacity: f32, kit: Vec<LootItem>, nearby: Vec<LootSource> },
    /// The roof over every cell of the map in view (M5+), one character a cell in the order of
    /// `terrain`'s cells (codes in [`roof`]), and how hilly the map's world tile is: `hills`
    /// 0 unknown, 1 flat, 2 small hills, 3 large hills, 4 mountainous, 5 impassable. RimWorld
    /// keeps no height for a cell; IW4L raises its rock from these. Sent with `terrain`.
    Roofs {
        size: [i32; 2],
        cells: String,
        #[serde(default)]
        hills: u8,
    },
    /// RimWorld's own art the mod saved as PNG files for IW4L (M5+), tinted as RimWorld draws
    /// it: `names[i]` is `dir/names[i].png`, 128 pixels square. `skins` and `buildings` refer
    /// to them by index. Grows as new art is needed; sent whole when it does.
    Textures { dir: String, names: Vec<String> },
    /// The texture of every cell's wall, door or smoothed rock (`walls`) and of its built or
    /// smoothed floor (`floors`), one character a cell in the order of `terrain`'s cells: `.`
    /// for none, else the index into `textures` plus 48 (`0` is 0), past `~` too (M5+).
    /// A linked wall's art (`..._l15`) has a picture of its face beside it (`..._l11`).
    Skins { size: [i32; 2], walls: String, floors: String },
    /// Everything built on the map in view but walls, doors and rock (M5+): furniture,
    /// machines, cover, so IW4L can model each. Sent when any comes, goes or turns.
    Buildings { buildings: Vec<BuildingState> },
    /// Everything lying on the ground of the map in view (M6+): resources, food, weapons,
    /// apparel, so IW4L can show each. Sent when any comes, goes or changes.
    Items { items: Vec<ItemState> },
    /// How each person on the map in view looks (M7+), so IW4L dresses their MW2 soldier
    /// the same. Sent when anyone's changes.
    Looks { looks: Vec<LookState> },
    /// What RimWorld's camera shows (M9+), about ten times a second while MW2 has the focus,
    /// for MW2's minimap: a `size` pixel square JPEG (`jpg`, base64, its rows bottom first) of
    /// the middle of RimWorld's screen, `cells` cells across, centred on map point `x`, `z`.
    /// RimWorld's camera follows the MW2 player meanwhile. M15: with `file`, the picture is not
    /// in the message (`jpg` empty) but in that shared file, every frame, uncompressed: a 32
    /// byte head (`RWV1`, frame number, odd while being written, side in pixels, cells across,
    /// middle x and z, all little-endian u32 or f32) then RGBA, north row first. The message
    /// names the file about once a second.
    View {
        size: u32,
        x: f32,
        z: f32,
        cells: f32,
        #[serde(default)]
        jpg: String,
        #[serde(default, skip_serializing_if = "Option::is_none")]
        file: Option<String>,
    },
    /// What each kind of plant looks like in RimWorld (M10+): for every `kind` of
    /// [`PlantState`], and for `grass` (the low cover on grassy cells of `terrain`), the
    /// pictures IW4L may stand there, as `_cut` indexes into `textures`. Sent when a kind's
    /// pictures are first seen.
    PlantArt { kinds: Vec<PlantKindArt> },
    /// How far every door on the map has swung (M11+), as RimWorld has it now: `doors`, and
    /// `steps` (how many whole steps `open` counts, so `open == steps` is wide open). Sent
    /// whenever any door moves. A door opens because the played colonist walked up to it, so
    /// RimWorld decides when; IW4L lets the MW2 player through once the way is clear.
    Doors { doors: Vec<DoorState>, steps: u32 },
    /// How brightly lamps, torches and other glowers light every roofed cell (M12+), one hex
    /// digit a cell in the order of `terrain`'s cells: `0` dark, `f` as lit as RimWorld counts
    /// "lit". Daylight is left out (IW4L shows open sky fully lit). Sent when any changes.
    Glow { size: [i32; 2], cells: String },
    /// RimWorld's own window now shows over MW2's top left corner as its minimap (M16), or no
    /// longer does: MW2 draws no minimap of its own under it while `shown`.
    Overlay { shown: bool },
    /// What it costs a pawn to walk into each cell (M17), RimWorld's own path cost (ground,
    /// what stands there, snow), one character a cell in the order of `terrain`'s cells:
    /// `'0'` plus the cost in ticks, capped at 1000; `'0'` plus 1001 where pawns cannot go.
    /// Sent when any changes.
    Costs { size: [i32; 2], cells: String },
    /// Every fire burning on the map in view (M20), on the ground or on someone: where, in
    /// cells, and how big RimWorld says it is (`size`, 0.1 a spark to 1.75 a blaze). Sent when
    /// any comes, goes or grows, at most a few times a second.
    Fires { fires: Vec<FireState> },
    /// Gas hanging over the map in view (M20): smoke from a smoke popper or a smoke launcher,
    /// tox gas, rot stink, deadlife dust. One entry a patch of [`GAS_PATCH`] by
    /// [`GAS_PATCH`] cells, the patch's south-west cell, with its thickest gas. Sent when
    /// any changes, at most once a second.
    Gas { gas: Vec<GasPatch> },
    /// The weather on the map in view (M21), as RimWorld blends it while it changes: how hard
    /// it rains and snows (RimWorld's `rainRate` and `snowRate`: 0 none, 1 a steady rain or
    /// snow, more for storms), how foggy (0 clear to 1 fog), and how windy (RimWorld's wind
    /// speed factor). Sent when any changes, at most once a second.
    Weather {
        rain: f32,
        snow: f32,
        fog: f32,
        #[serde(default)]
        wind: f32,
    },
    /// The filth near the MW2 player on the map in view (M21): blood, firefoam, fuel, vomit,
    /// ash, dirt and the rest, each splotch where RimWorld draws it: its picture (`tex`, a
    /// cut-out index into `textures`), `size` in cells, `x` and `z`, and `rot` degrees. Sent
    /// when any comes, goes or thickens, at most every two seconds.
    Filth { filth: Vec<FilthState> },
    /// RimWorld's flecks made since the last (M21): the small effects it draws everywhere,
    /// puffs of dust and smoke, sparks, glows, splashes, as they start, near where the MW2
    /// player is. Each flies, turns, grows and fades on its own from there.
    Flecks { flecks: Vec<FleckState> },

    // ---- IW4L -> RimWorld
    /// Where the MW2 player is, about 10 times a second while it plays `pawn` (M2+). Same
    /// units as [`PawnState`]: map coordinates, `z` north, `yaw` degrees clockwise from north.
    /// RimWorld moves the pawn there if the cell is walkable.
    PlayerMove { pawn: String, x: f32, z: f32, yaw: f32 },
    /// A bullet the MW2 player fired while playing `pawn` (M2+), with MW2's `weapon`: from the
    /// gun to where it stopped, `[x, height, z]` in cells. When it struck a pawn, `target` and
    /// `part` say which and where, and `damage` is MW2's damage for that hit, which RimWorld
    /// scales to its own. `part` (M18) is `head`, `neck`, `upper` (chest, shoulders, arms),
    /// `lower` (belly, hands) or `legs` on a standing person, `body` (anywhere) on anyone else.
    /// RimWorld ignores it while paused. M24: a `pellet` is another pellet of the round before
    /// it (a shotgun's), or a ricochet: it flies and strikes as the rest, but uses no ammo.
    PlayerShot {
        pawn: String,
        weapon: String,
        from: [f32; 3],
        to: [f32; 3],
        #[serde(default, skip_serializing_if = "Option::is_none")]
        target: Option<String>,
        #[serde(default, skip_serializing_if = "Option::is_none")]
        part: Option<String>,
        #[serde(default, skip_serializing_if = "Option::is_none")]
        damage: Option<f32>,
        #[serde(default, skip_serializing_if = "std::ops::Not::not")]
        pellet: bool,
    },
    /// The MW2 player started reloading the gun of `pawn`, the colonist it plays (M24). Under
    /// Combat Extended RimWorld reloads its magazine at once from the colonist's inventory,
    /// while MW2 plays the reload out; otherwise nothing happens.
    PlayerReload { pawn: String },
    /// RimWorld's bullet `shot` (the `id` of a [`Message::Shot`]) met the MW2 player, who
    /// plays `pawn` (M8+); `part` is `head` or `body`. RimWorld deals that bullet's own damage
    /// to the colonist, once per bullet.
    PlayerStruck { pawn: String, shot: String, part: String },
    /// Where the MW2 player flies while spectating (M9+), map `x` and `z`, about ten times a
    /// second, for RimWorld's camera to follow.
    Spectate { x: f32, z: f32 },
    /// MW2 brought RimWorld's window up over its own (M9+, Tab); `back` is MW2's window handle
    /// in decimal, which Tab in RimWorld brings back.
    RimworldShown { back: String },
    /// The MW2 player pressed use (E) while playing `pawn` (M12+): the colonist works the
    /// nearest door or switch in front of them, as RimWorld decides. M18: or picks up the
    /// downed pawn there, or puts down the one they carry.
    Use { pawn: String },
    /// MW2's Escape menu opened or closed (M18): RimWorld's minimap window hides while `open`.
    Menu { open: bool },
    /// The MW2 player threw the grenade the played colonist `pawn` holds (M20), at map point
    /// `to` (`[x, z]` in cells), where they aimed on the ground: RimWorld throws it there, as
    /// that colonist throws it, and it flies, lands and goes off as RimWorld's own.
    PlayerThrow { pawn: String, to: [f32; 2] },
    /// Take `item` (an id from the latest `loot`'s `nearby`) for the played colonist `pawn`
    /// (M4+), as RimWorld would: a weapon goes in hand (the one held drops), apparel is worn
    /// (what it replaces drops), anything else is carried, as much as the colonist can.
    LootTake { pawn: String, item: String },
    /// Drop `item` (an id from the latest `loot`'s `kit`) at the played colonist's feet (M4+).
    LootDrop { pawn: String, item: String },
}

/// How many cells across a [`Message::Gas`] patch is.
pub const GAS_PATCH: i32 = 2;

/// A fire (M20): where, in cells, and RimWorld's `fireSize`.
#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct FireState {
    pub x: f32,
    pub z: f32,
    pub size: f32,
}

/// A patch of gas (M20): its south-west cell `[x, z]`, what gas (`smoke`, `tox`, `rot` or
/// `dust`) and how thick, 1 to 255.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct GasPatch {
    pub at: [i32; 2],
    pub kind: String,
    pub density: u8,
}

/// One splotch of filth (M21).
#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct FilthState {
    pub tex: i32,
    pub size: f32,
    pub x: f32,
    pub z: f32,
    #[serde(default)]
    pub rot: f32,
}

/// One fleck as it starts (M21): its picture (`tex`) and `color` (RGBA, 0..1); where (`x`,
/// `z` in cells, `y` cells above the ground); `size` in cells and how many cells across it
/// grows each game second (`grow`); `rot` degrees and `spin` degrees a game second; `vx`,
/// `vz` and `vy` its speed in cells a game second; how long it fades in, stays and fades
/// out, in game seconds (60 ticks, faster at higher game speed); `flat` when RimWorld
/// draws it on the ground.
#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct FleckState {
    pub tex: i32,
    pub x: f32,
    pub z: f32,
    #[serde(default)]
    pub y: f32,
    pub size: f32,
    #[serde(default)]
    pub grow: f32,
    #[serde(default)]
    pub rot: f32,
    #[serde(default)]
    pub spin: f32,
    #[serde(default)]
    pub vx: f32,
    #[serde(default)]
    pub vz: f32,
    #[serde(default)]
    pub vy: f32,
    #[serde(default)]
    pub fade_in: f32,
    pub solid: f32,
    #[serde(default)]
    pub fade_out: f32,
    #[serde(default = "opaque")]
    pub color: [f32; 4],
    #[serde(default, skip_serializing_if = "std::ops::Not::not")]
    pub flat: bool,
}

fn opaque() -> [f32; 4] {
    [1.0; 4]
}

/// One door, as far as RimWorld has swung it (M11+).
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct DoorState {
    /// Its cell, `[x, z]`, `z` north.
    pub at: [i32; 2],
    /// How far it has swung: 0 shut, `steps` wide open.
    pub open: u32,
    /// Which way its leaves slide: 0 east and west, 1 north and south.
    pub axis: u8,
    /// The whole door when it covers more than one cell (M16: a security door), `[x0, z0,
    /// x1, z1]` inclusive: one door, its leaves meeting in the middle of all of it.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub rect: Option<[i32; 4]>,
}

/// What one kind of plant looks like in RimWorld (M10+).
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct PlantKindArt {
    /// A [`PlantState`] kind (`oak`, `pine`, `bush`, ...), or `grass`.
    pub kind: String,
    /// Its pictures, `_cut` indexes into the latest `textures`.
    pub tex: Vec<i32>,
}

/// The cell codes of [`Message::Terrain`]. Anything else reads as [`terrain::SOIL`].
pub mod terrain {
    /// Bare ground.
    pub const SOIL: u8 = b'.';
    pub const SAND: u8 = b',';
    /// Stony ground with nothing growing: gravel, rough stone, ice.
    pub const STONE: u8 = b':';
    /// A built floor or a bridge.
    pub const FLOOR: u8 = b'=';
    /// Water a pawn can wade through.
    pub const SHALLOW_WATER: u8 = b'~';
    /// Water no pawn can cross.
    pub const DEEP_WATER: u8 = b'O';
    /// Natural rock, a mountain's mass.
    pub const ROCK: u8 = b'#';
    /// A wall or anything else built that fills its cell.
    pub const WALL: u8 = b'W';
    pub const DOOR: u8 = b'+';
    /// Something built that a pawn can shoot over: sandbags, barricades, furniture.
    pub const COVER: u8 = b'c';
    pub const TREE: u8 = b'T';
    /// Ground with grass or other low plants growing on it.
    pub const GRASS: u8 = b'g';
    /// Something built a pawn cannot walk through that is not a wall: machines, turrets,
    /// solar panels (M5+). Older mods send these as [`WALL`].
    pub const MACHINE: u8 = b'M';
}

/// The roof codes of [`Message::Roofs`]. Anything else reads as [`roof::NONE`].
pub mod roof {
    pub const NONE: u8 = b'.';
    /// A roof built by colonists.
    pub const BUILT: u8 = b'c';
    /// A thin rock roof, as over a cave.
    pub const ROCK: u8 = b'r';
    /// RimWorld's overhead mountain: thick rock deep in a mountain.
    pub const THICK: u8 = b'R';
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum Side {
    Rimworld,
    Iw4l,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum ShotResult {
    Hit,
    Miss,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum Stance {
    Stand,
    Crouch,
    Prone,
}

/// A thing in a [`Message::Loot`].
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct LootItem {
    /// RimWorld's `ThingID`, which `loot_take` and `loot_drop` name.
    pub id: String,
    /// RimWorld's label, count and quality included (`assault rifle (good)`).
    pub label: String,
    /// The thing's `defName` (`Gun_AssaultRifle`).
    pub def: String,
    pub count: i32,
    pub kind: LootKind,
    /// Its mass in kg, the whole stack.
    #[serde(default)]
    pub mass: f32,
}

/// What a [`LootItem`] is, which decides what taking it does.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "snake_case")]
pub enum LootKind {
    Weapon,
    Apparel,
    Item,
}

/// Where nearby loot lies: `ground` (everything on the cells around the colonist), or a
/// corpse or downed pawn by its `ThingID`, with its `label`.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct LootSource {
    pub id: String,
    pub label: String,
    pub items: Vec<LootItem>,
}

/// Something built on RimWorld's map: see [`Message::Buildings`].
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct BuildingState {
    /// Its ThingDef, as `Bed` or `SolarGenerator`.
    pub def: String,
    /// The cells it covers, `[x0, z0, x1, z1]` inclusive.
    pub rect: [i32; 4],
    /// Which way it faces: 0 north, 1 east, 2 south, 3 west (RimWorld's `Rot4`).
    pub rot: u8,
    /// Where RimWorld draws its picture: the middle, in cells, and the picture's width and
    /// height in cells before it is turned.
    pub at: [f32; 2],
    pub draw: [f32; 2],
    /// Its picture, an index into `textures`, or -1 for none.
    pub tex: i32,
    /// Quarter turns clockwise the picture is drawn turned by, and whether it is mirrored.
    #[serde(default)]
    pub turn: u8,
    #[serde(default)]
    pub flip: bool,
    /// How much of its cells it fills against bullets, 0 to 1, and whether pawns cannot walk
    /// through it.
    pub fill: f32,
    #[serde(default)]
    pub solid: bool,
    /// Its picture cut out of its background, for what reads better stood upright facing the
    /// player than as a model of boxes (statues, thrones, lecterns, instruments, poppers):
    /// an index into `textures`, not mirrored (`flip` says). Drawn `draw` cells across, its
    /// feet on the floor.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub card: Option<i32>,
}

/// Something lying on RimWorld's map: see [`Message::Items`].
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct ItemState {
    /// Its ThingDef, as `Steel` or `Gun_AssaultRifle`.
    pub def: String,
    /// Its cell, `z` north.
    pub x: i32,
    pub z: i32,
    /// Its picture as RimWorld draws it on the ground, an index into `textures` (a cut-out
    /// picture, `..._cut`), or -1 for none.
    pub tex: i32,
    /// How many are stacked there.
    #[serde(default = "one")]
    pub count: i32,
}

fn one() -> i32 {
    1
}

/// How a person looks: see [`Message::Looks`].
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct LookState {
    /// The pawn's ThingID, as in `snapshot`.
    pub id: String,
    /// Skin colour, 0 to 255.
    pub skin: [u8; 3],
    /// Hair colour; none when bald.
    #[serde(default)]
    pub hair: Option<[u8; 3]>,
    /// What they wear, innermost first.
    #[serde(default)]
    pub wear: Vec<WearState>,
}

/// One piece of apparel a person wears.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct WearState {
    /// Its ThingDef, as `Apparel_BasicShirt`.
    pub def: String,
    /// The colour RimWorld draws it in (its stuff's, or dyed), 0 to 255.
    pub color: [u8; 3],
    /// Its picture facing south, an index into `textures` (cut out), or -1 for none.
    #[serde(default = "minus_one")]
    pub tex: i32,
    /// Its outermost layer: `OnSkin`, `Middle`, `Shell`, `Belt` or `Overhead`.
    #[serde(default)]
    pub layer: String,
    /// The body part groups it covers: `Torso`, `Legs`, `Shoulders`, `Arms`, `Hands`,
    /// `Feet`, `FullHead`, `UpperHead`, `Eyes`, `Waist`, ...
    #[serde(default)]
    pub parts: Vec<String>,
}

fn minus_one() -> i32 {
    -1
}

/// A tree or bush on RimWorld's map.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct PlantState {
    /// Its cell, `z` north.
    pub x: i32,
    pub z: i32,
    /// `pine`, `birch`, `oak` (any other broadleaf tree), `palm`, `cactus`, `dead` (a dead or
    /// leafless tree) or `bush`; a crop is `crop_` and its ThingDef, with `_young` while
    /// RimWorld draws it unripe. `plant_art` says what each kind looks like.
    pub kind: String,
    /// How grown it is, 0 to 1, in tenths.
    pub growth: f32,
}

#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct PawnState {
    pub id: String,
    /// The pawn's short name, for labels.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub name: Option<String>,
    /// `humanlike`, `animal`, `mech` or `other`.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub kind: Option<String>,
    /// RimWorld map coordinates from `DrawPos`: cell `(x, z)` spans `x..x+1`,
    /// `z` grows north. RimWorld has no height.
    pub x: f32,
    pub z: f32,
    /// Facing in degrees clockwise from north (`Rot4.AsAngle`).
    pub rot: f32,
    pub stance: Stance,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub weapon: Option<String>,
    pub downed: bool,
    /// Dead (M16): a corpse, lying where it fell. Not shot at.
    #[serde(default, skip_serializing_if = "std::ops::Not::not")]
    pub dead: bool,
    /// `player`, `hostile` (to the player) or `neutral`.
    pub faction: String,
    /// How many times this pawn has been hurt (M12+), absent until it first is: IW4L flashes
    /// it red whenever this goes up, as RimWorld does.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub hurt: Option<u32>,
    /// For animals and mechs (M6+): the body's pictures facing north, east and south, as
    /// indices into `textures` (cut-out pictures, `..._cut`; west is east mirrored), and the
    /// size RimWorld draws them, in cells.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub art: Option<[i32; 3]>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub draw: Option<[f32; 2]>,
    /// Lying down (M15): in bed, asleep, downed, carried. Which way the head points, in
    /// degrees clockwise from north, as RimWorld turns the body (`PawnRenderer.BodyAngle`).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub lie: Option<f32>,
    /// Lying in a bed (M15), so on top of it.
    #[serde(default, skip_serializing_if = "std::ops::Not::not")]
    pub bed: bool,
    /// What the pawn carries (M15): a person, an animal or a thing, as RimWorld draws it.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub carry: Option<Carried>,
    /// The weapon in its hands where RimWorld shows it (M23): drafted, aiming or attacking,
    /// and always for enemies.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub held: Option<Held>,
    /// How many melee attacks it has made (M23), absent until its first: IW4L swings its
    /// weapon (or lunges, bare-handed) whenever this goes up.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub swings: Option<u32>,
}

/// A weapon in a pawn's hands as RimWorld draws it (M23): its picture (`tex`, a cut-out index
/// into `textures`, the barrel or blade pointing east as RimWorld's are) and `size` in cells;
/// `aim`, degrees clockwise from north, where it points while the pawn aims or attacks
/// (absent while it is only carried); RimWorld's `equippedAngleOffset` (`turn`, degrees) and
/// how far from the body it is held (`reach`, cells); and whether it is a melee weapon.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Held {
    pub tex: i32,
    pub size: [f32; 2],
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub aim: Option<f32>,
    #[serde(default)]
    pub turn: f32,
    #[serde(default)]
    pub reach: f32,
    #[serde(default, skip_serializing_if = "std::ops::Not::not")]
    pub melee: bool,
}

/// How a gun fires in RimWorld (M15), in seconds of game time: `burst` shots `between`
/// apart, then `cooldown` before the shooter aims again for `warmup` (its aiming time,
/// the shooter's aiming delay included) and fires the next burst.
#[derive(Debug, Clone, Copy, PartialEq, Serialize, Deserialize)]
pub struct GunTiming {
    pub burst: u32,
    pub between: f32,
    pub warmup: f32,
    pub cooldown: f32,
    /// M20: a thrown weapon (grenades), and how far it is thrown, in cells: the MW2 player
    /// throws it with fire or G ([`Message::PlayerThrow`]), not as a gun.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub throw: Option<f32>,
    /// M24: under Combat Extended, the gun's magazine: how many rounds it holds. MW2's clip
    /// is this instead of a burst, and the gun still waits `cooldown` and `warmup` after each
    /// burst. Absent for a gun without one (and without CE).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub mag: Option<u32>,
    /// M24: rounds in that magazine now. RimWorld uses a round for each `player_shot` (not a
    /// `pellet`) and fills the magazine on [`Message::PlayerReload`]: MW2's clip is this.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub loaded: Option<u32>,
    /// M24: seconds of game time Combat Extended takes to fill that magazine from empty:
    /// MW2's reload takes as long (with `warmup` after it).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub reload: Option<f32>,
    /// M24: under Combat Extended's ammo system, the ammo the colonist carries for the gun
    /// outside its magazine: MW2's reserve. 0 with nothing `loaded` (or, for a gun without a
    /// magazine, at all): the gun is dry, and `player_shot` does nothing.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub spare: Option<u32>,
}

impl GunTiming {
    /// M24: nothing left to fire, in the magazine or carried.
    pub fn dry(&self) -> bool {
        self.spare == Some(0) && self.loaded.unwrap_or(0) == 0
    }
}

/// Something a pawn carries (M15): its picture (a cut-out index into `textures`; a person's
/// is their portrait facing south, head up), how many cells across RimWorld draws it, and
/// whether it is a pawn (carried lying across the arms) or a thing (held in front).
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct Carried {
    pub tex: i32,
    pub size: f32,
    #[serde(default, skip_serializing_if = "std::ops::Not::not")]
    pub pawn: bool,
}

impl Message {
    /// Parse one line. Unknown message types are an error the caller should log and skip,
    /// so either side can add messages without breaking the other.
    pub fn from_line(line: &str) -> serde_json::Result<Self> {
        serde_json::from_str(line.trim_end())
    }

    /// Serialize to one line, newline included.
    pub fn to_line(&self) -> String {
        let mut s = serde_json::to_string(self).expect("protocol messages always serialize");
        s.push('\n');
        s
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Every line in protocol/examples.ndjson must parse and round-trip.
    #[test]
    fn examples_round_trip() {
        let examples = include_str!("../../../../protocol/examples.ndjson");
        let mut count = 0;
        for line in examples.lines().filter(|l| !l.trim().is_empty() && !l.starts_with("//")) {
            let msg = Message::from_line(line).unwrap_or_else(|e| panic!("{line}: {e}"));
            assert_eq!(Message::from_line(&msg.to_line()).unwrap(), msg);
            count += 1;
        }
        assert!(count >= 6, "expected the full example set, parsed {count}");
    }

    /// The exact bytes the C# mod sends for M0.
    #[test]
    fn parses_rimworld_m0_lines() {
        let hello = r#"{"t":"hello","side":"rimworld","protocol":0,"game_version":"1.6.4871 rev1234"}"#;
        assert!(matches!(Message::from_line(hello).unwrap(), Message::Hello { side: Side::Rimworld, protocol: 0, .. }));
        assert_eq!(Message::from_line(r#"{"t":"tick","tick":600}"#).unwrap(), Message::Tick { tick: 600 });
    }

    fn player_gun(line: &str) -> GunTiming {
        match Message::from_line(line).unwrap() {
            Message::Snapshot { player_gun: Some(gun), .. } => gun,
            other => panic!("expected a snapshot with player_gun, got {other:?}"),
        }
    }

    /// M24: a Combat Extended gun's magazine and ammo; without CE there are none.
    #[test]
    fn player_gun_ce_ammo() {
        let ce = player_gun(
            r#"{"t":"snapshot","tick":1,"pawns":[],"player_gun":{"burst":3,"between":0.17,"warmup":1.0,"cooldown":1.7,"mag":30,"loaded":12,"reload":4.6,"spare":90}}"#,
        );
        assert_eq!((ce.mag, ce.loaded, ce.reload, ce.spare), (Some(30), Some(12), Some(4.6), Some(90)));
        assert!(!ce.dry());
        let empty = player_gun(r#"{"t":"snapshot","tick":1,"pawns":[],"player_gun":{"burst":3,"between":0.17,"warmup":1.0,"cooldown":1.7,"mag":30,"loaded":0,"reload":4.6,"spare":0}}"#);
        assert!(empty.dry());
        let bow = player_gun(r#"{"t":"snapshot","tick":1,"pawns":[],"player_gun":{"burst":1,"between":0.0,"warmup":1.2,"cooldown":1.5,"spare":0}}"#);
        assert!(bow.dry() && bow.mag.is_none());
        let vanilla = player_gun(r#"{"t":"snapshot","tick":1,"pawns":[],"player_gun":{"burst":3,"between":0.17,"warmup":1.0,"cooldown":1.7}}"#);
        assert_eq!((vanilla.mag, vanilla.loaded, vanilla.reload, vanilla.spare), (None, None, None, None));
        assert!(!vanilla.dry());
    }

    /// M24: a pellet uses no ammo, and is only written when it is one.
    #[test]
    fn pellets_and_reloads() {
        let shot = |pellet| Message::PlayerShot {
            pawn: "Human12".into(),
            weapon: "spas12_mp".into(),
            from: [1.0, 1.6, 1.0],
            to: [2.0, 1.0, 2.0],
            target: None,
            part: None,
            damage: None,
            pellet,
        };
        assert!(!shot(false).to_line().contains("pellet"));
        assert!(shot(true).to_line().contains(r#""pellet":true"#));
        let read = Message::from_line(r#"{"t":"player_shot","pawn":"Human12","weapon":"m4_mp","from":[1,1,1],"to":[2,1,2]}"#).unwrap();
        assert!(matches!(read, Message::PlayerShot { pellet: false, .. }));
        let reload = Message::from_line(r#"{"t":"player_reload","pawn":"Human12"}"#).unwrap();
        assert_eq!(reload, Message::PlayerReload { pawn: "Human12".into() });
    }
}
