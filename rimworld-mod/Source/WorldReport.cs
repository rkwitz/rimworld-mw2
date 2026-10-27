using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Verse.AI;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldMW2
{
    /// <summary>
    /// M5: what IW4L needs to build the map in view as RimWorld draws it, beyond
    /// <see cref="TerrainReport"/>'s cells: the roofs (<c>roofs</c>), the art of walls and floors
    /// (<c>skins</c>), everything else built (<c>buildings</c>), what lies on the ground
    /// (<c>items</c>), and the art they use (<c>textures</c>, saved by <see cref="TextureExport"/>).
    /// Anything still being built (a frame or blueprint) is left out. Each is sent when it changed;
    /// checked once a second of real time, paused or not.
    /// </summary>
    public static class WorldReport
    {
        private const float CheckSeconds = 1f;
        /// <summary>
        /// Skin characters: <c>'0'</c> plus the texture index, past <c>'~'</c> too, up to the
        /// last character below UTF-16's surrogates.
        /// </summary>
        private const int MaxSkin = 0xD7FF - '0';
        /// <summary>More items than this on one map are left out, nearest the middle kept.</summary>
        private const int MaxItems = 5000;

        private static Map sentMap;
        private static string sentRoofs, sentSkins, sentBuildings, sentItems, sentGlow, sentCosts;
        private static int sentTextures = -1;
        private static float lastCheck = -1f;

        public static void Reset()
        {
            sentMap = null;
            sentRoofs = sentSkins = sentBuildings = sentItems = sentGlow = sentCosts = null;
            sentTextures = -1;
            lastCheck = -1f;
        }

        /// <summary>
        /// The <c>textures</c> line when art was saved since the last one, else null. Sent
        /// before anything naming the new art, so IW4L never meets an index it lacks (M13: a
        /// pawn's new picture showed as its old look until the next world check).
        /// </summary>
        public static string NewTextures()
        {
            if (TextureExport.Revision == sentTextures) return null;
            sentTextures = TextureExport.Revision;
            return TextureExport.Line();
        }

        public static List<string> Poll()
        {
            var lines = new List<string>();
            Map map = PawnSnapshot.MapInView();
            if (map == null) return lines;
            float now = Time.realtimeSinceStartup;
            if (map == sentMap && lastCheck >= 0f && now - lastCheck < CheckSeconds) return lines;
            lastCheck = now;
            bool newMap = map != sentMap;
            sentMap = map;
            string size = "[" + map.Size.x + "," + map.Size.z + "]";

            string roofs = Roofs(map);
            if (newMap || roofs != sentRoofs)
            {
                sentRoofs = roofs;
                lines.Add(Json.Msg("roofs").Raw("size", size).Str("cells", roofs).Num("hills", Hills(map)).ToString());
            }
            // Art first: skins and buildings name it by index.
            string walls = Walls(map), floors = Floors(map);
            string buildings = Buildings(map);
            string items = Items(map);
            string textures = NewTextures();
            if (textures != null) lines.Add(textures);
            string skins = walls + "|" + floors;
            if (newMap || skins != sentSkins)
            {
                sentSkins = skins;
                lines.Add(Json.Msg("skins").Raw("size", size).Str("walls", walls).Str("floors", floors).ToString());
            }
            if (newMap || buildings != sentBuildings)
            {
                sentBuildings = buildings;
                lines.Add(Json.Msg("buildings").Raw("buildings", buildings).ToString());
            }
            if (newMap || items != sentItems)
            {
                sentItems = items;
                lines.Add(Json.Msg("items").Raw("items", items).ToString());
            }
            string costs = Costs(map);
            if (newMap || costs != sentCosts)
            {
                sentCosts = costs;
                lines.Add(Json.Msg("costs").Raw("size", size).Str("cells", costs).ToString());
            }
            string glow = Glow(map);
            if (newMap || glow != sentGlow)
            {
                sentGlow = glow;
                lines.Add(Json.Msg("glow").Raw("size", size).Str("cells", glow).ToString());
            }
            return lines;
        }

        /// <summary>
        /// M17: what it costs a pawn to walk into each cell, in <c>terrain</c>'s order: RimWorld's
        /// own path cost (ground, what stands there, snow) as <c>'0'</c> plus ticks, capped at
        /// 1000, and <c>'0'</c> plus 1001 where pawns cannot go. IW4L slows the player as much.
        /// </summary>
        private static string Costs(Map map)
        {
            var sb = new StringBuilder(map.Size.x * map.Size.z);
            PathGrid grid = map.pathing?.Normal?.pathGrid;
            for (int z = 0; z < map.Size.z; z++)
                for (int x = 0; x < map.Size.x; x++)
                {
                    int cost = 0;
                    if (grid != null)
                    {
                        try { cost = grid.CalculatedCostAt(new IntVec3(x, 0, z), false, IntVec3.Invalid); }
                        catch { cost = 0; }
                    }
                    sb.Append((char)('0' + (cost >= PathGrid.ImpassableCost ? 1001 : Mathf.Clamp(cost, 0, 1000))));
                }
            return sb.ToString();
        }

        private static int Hills(Map map)
        {
            try { return (int)map.TileInfo.hilliness; }
            catch { return 0; }
        }

        /// <summary>In <c>terrain</c>'s order: row by row from the south edge, west to east.</summary>
        private static string Roofs(Map map)
        {
            var sb = new StringBuilder(map.Size.x * map.Size.z);
            for (int z = 0; z < map.Size.z; z++)
                for (int x = 0; x < map.Size.x; x++)
                {
                    RoofDef roof = map.roofGrid.RoofAt(x, z);
                    sb.Append(roof == null ? '.' : roof.isThickRoof ? 'R' : roof.isNatural ? 'r' : 'c');
                }
            return sb.ToString();
        }

        private static char Skin(int index) => index < 0 || index > MaxSkin ? '.' : (char)('0' + index);

        /// <summary>The art of each cell's wall, door or smoothed rock; natural rock keeps MW2's.</summary>
        private static string Walls(Map map)
        {
            var sb = new StringBuilder(map.Size.x * map.Size.z);
            for (int z = 0; z < map.Size.z; z++)
                for (int x = 0; x < map.Size.x; x++)
                {
                    Building edifice = TerrainReport.Edifice(new IntVec3(x, 0, z), map);
                    char code = edifice == null ? '.' : TerrainReport.Code(new IntVec3(x, 0, z), map);
                    bool smoothed = edifice?.def.building?.unsmoothedThing != null;
                    if (edifice == null || !(code == 'W' || code == '+' || smoothed))
                    {
                        sb.Append('.');
                        continue;
                    }
                    Graphic graphic = edifice.Graphic;
                    bool linked = graphic is Graphic_Linked;
                    Material mat = linked ? graphic.MatSingleFor(edifice) : graphic.MatAt(edifice.Rotation, edifice);
                    sb.Append(Skin(TextureExport.Of(mat, linked ? TextureExport.AllLinked : -1)));
                    // Its front face, which IW4L puts on the wall's sides.
                    if (linked) TextureExport.Of(mat, TextureExport.FaceLinked);
                }
            return sb.ToString();
        }

        /// <summary>The art of every cell's ground; water keeps MW2's. M16: saved whole
        /// (<c>..._floor</c>), the most common ground first, as IW4L keeps the first few whole
        /// and the rest cut down.</summary>
        private static string Floors(Map map)
        {
            var counts = new Dictionary<TerrainDef, int>();
            for (int z = 0; z < map.Size.z; z++)
                for (int x = 0; x < map.Size.x; x++)
                {
                    TerrainDef ground = map.terrainGrid.TerrainAt(new IntVec3(x, 0, z));
                    if (ground != null) counts[ground] = counts.TryGetValue(ground, out int n) ? n + 1 : 1;
                }
            var cache = new Dictionary<TerrainDef, char>();
            foreach (var pair in counts.OrderByDescending(p => p.Value))
            {
                // Every kind of ground but water: RimWorld's own art, so its soil, sand
                // and stone look like RimWorld's rather than MW2's (M10).
                TerrainDef ground = pair.Key;
                bool water = ground.IsWater;
                cache[ground] = !water && ground.graphic != null ? Skin(TextureExport.Of(ground.graphic.MatSingle, floor: true)) : '.';
            }
            var sb = new StringBuilder(map.Size.x * map.Size.z);
            for (int z = 0; z < map.Size.z; z++)
                for (int x = 0; x < map.Size.x; x++)
                {
                    TerrainDef ground = map.terrainGrid.TerrainAt(new IntVec3(x, 0, z));
                    sb.Append(ground != null && cache.TryGetValue(ground, out char skin) ? skin : '.');
                }
            return sb.ToString();
        }

        /// <summary>
        /// M12: how brightly lamps light each cell, one hex digit a cell in terrain order:
        /// RimWorld's own glow grid without daylight, <c>f</c> from where RimWorld counts a cell
        /// as lit (half glow) up. M26: open ground too, which lamps and fires light at night
        /// (daylight comes in <c>sky</c>).
        /// </summary>
        private static string Glow(Map map)
        {
            var sb = new StringBuilder(map.cellIndices.NumGridCells);
            for (int z = 0; z < map.Size.z; z++)
                for (int x = 0; x < map.Size.x; x++)
                {
                    var cell = new IntVec3(x, 0, z);
                    float lit = map.glowGrid.GroundGlowAt(cell, false, true) * 2f;
                    sb.Append("0123456789abcdef"[Mathf.Clamp(Mathf.RoundToInt(lit * 15f), 0, 15)]);
                }
            return sb.ToString();
        }

        /// <summary>Everything built but walls, doors, rock and conduits, as a JSON array.</summary>
        private static string Buildings(Map map)
        {
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial))
            {
                ThingDef def = thing.def;
                if (!thing.Spawned || def.drawerType == DrawerType.None) continue;
                if (thing is Frame || def.IsFrame || def.IsBlueprint) continue;
                if (def.altitudeLayer == AltitudeLayer.Conduits) continue;
                if (def.building != null && def.building.isNaturalRock) continue;
                char code = TerrainReport.Code(thing.Position, map);
                if (thing == map.edificeGrid[thing.Position] && (code == 'W' || code == '+' || code == '#')) continue;
                Graphic graphic;
                try { graphic = thing.Graphic; }
                catch { continue; }
                if (graphic == null) continue;
                Rot4 rot = thing.Rotation;
                bool linked = graphic is Graphic_Linked;
                bool rotated = linked || graphic.ShouldDrawRotated;
                bool flip = !rotated && (rot == Rot4.West && graphic.WestFlipped || rot == Rot4.East && graphic.EastFlipped);
                int tex;
                try
                {
                    Material mat = linked ? graphic.MatSingleFor(thing) : graphic.MatAt(rot, thing);
                    tex = TextureExport.Of(mat, linked ? TextureExport.AllLinked : -1, flip);
                }
                catch { tex = -1; }
                // Art: IW4L cuts its body from its picture (M15, M16).
                int card = -1;
                if (!linked && Upright(def))
                {
                    try { card = TextureExport.Of(graphic.MatAt(rot, thing), cutout: true); }
                    catch { card = -1; }
                }
                Vector2 draw = graphic.drawSize;
                if (rot.IsHorizontal && !rotated) draw = new Vector2(draw.y, draw.x);
                CellRect rect = thing.OccupiedRect();
                Vector3 at = thing.DrawPos;
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"def\":\"").Append(def.defName)
                    .Append("\",\"rect\":[").Append(rect.minX).Append(',').Append(rect.minZ).Append(',')
                    .Append(rect.maxX).Append(',').Append(rect.maxZ)
                    .Append("],\"rot\":").Append(rot.AsInt)
                    .Append(",\"at\":[").Append(F(at.x)).Append(',').Append(F(at.z))
                    .Append("],\"draw\":[").Append(F(draw.x)).Append(',').Append(F(draw.y))
                    .Append("],\"tex\":").Append(tex)
                    .Append(",\"turn\":").Append(rotated && !linked ? rot.AsInt : 0)
                    .Append(",\"flip\":").Append(flip ? "true" : "false")
                    .Append(",\"fill\":").Append(F(def.fillPercent))
                    .Append(",\"solid\":").Append(def.passability == Traversability.Impassable ? "true" : "false");
                if (card >= 0) sb.Append(",\"card\":").Append(card);
                sb.Append('}');
            }
            return sb.Append(']').ToString();
        }

        /// <summary>
        /// Every item lying on the map (not carried, not a corpse), as a JSON array of
        /// <c>{def, x, z, tex, count}</c>; <c>tex</c> is its picture as RimWorld draws it there.
        /// </summary>
        private static string Items(Map map)
        {
            var found = new List<Thing>();
            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
            {
                if (thing == null || !thing.Spawned || thing.def.category != ThingCategory.Item) continue;
                if (thing is Corpse || thing.def.drawerType == DrawerType.None) continue;
                found.Add(thing);
            }
            if (found.Count > MaxItems)
            {
                IntVec3 middle = map.Center;
                found.Sort((a, b) => a.Position.DistanceToSquared(middle).CompareTo(b.Position.DistanceToSquared(middle)));
                found.RemoveRange(MaxItems, found.Count - MaxItems);
            }
            var sb = new StringBuilder("[");
            bool first = true;
            foreach (Thing thing in found)
            {
                int tex;
                try { tex = TextureExport.Of(thing.Graphic?.MatSingleFor(thing), cutout: true); }
                catch { tex = -1; }
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"def\":\"").Append(thing.def.defName)
                    .Append("\",\"x\":").Append(thing.Position.x)
                    .Append(",\"z\":").Append(thing.Position.z)
                    .Append(",\"tex\":").Append(tex)
                    .Append(",\"count\":").Append(thing.stackCount)
                    .Append('}');
            }
            return sb.Append(']').ToString();
        }

        /// <summary>Words in the defs of art whose shape boxes cannot give.</summary>
        private static readonly string[] UprightWords =
        {
            "Statue", "Sculpture", "Skullspike", "Totem", "Effigy", "Stele", "Monument",
        };

        /// <summary>
        /// Whether a building is art whose body IW4L cuts from its picture (statues,
        /// sculptures, totems and the like), rather than a model of boxes. M16: thrones,
        /// lecterns, instruments, altars and poppers are models again, made as what they are.
        /// </summary>
        internal static bool Upright(ThingDef def)
        {
            if (def.thingClass != null && typeof(Building_Art).IsAssignableFrom(def.thingClass))
                return true;
            foreach (string word in UprightWords)
                if (def.defName.IndexOf(word, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
