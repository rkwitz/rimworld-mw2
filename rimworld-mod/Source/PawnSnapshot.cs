using System.Collections.Generic;
using System.Globalization;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldMW2
{
    /// <summary>
    /// Builds the M1 <c>snapshot</c> message: every spawned pawn on the map the player is looking at.
    /// Read on the main thread only.
    /// </summary>
    public static class PawnSnapshot
    {
        public static Map MapInView() => Find.CurrentMap ?? Find.AnyPlayerHomeMap;

        /// <summary>The snapshot line, or null when no map is loaded (world view at game start).</summary>
        public static string Build()
        {
            Map map = MapInView();
            if (map == null) return null;
            var pawns = new List<string>();
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                if (pawn == null || pawn.Dead) continue;
                pawns.Add(PawnJson(pawn));
            }
            // M16: the dead, lying where they fell (not being carried: those are in hand).
            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse))
            {
                if (!(thing is Corpse corpse) || !corpse.Spawned || corpse.InnerPawn == null) continue;
                string dead = CorpseJson(corpse);
                if (dead != null) pawns.Add(dead);
            }
            string size = "[" + map.Size.x.ToString(CultureInfo.InvariantCulture) + ","
                + map.Size.z.ToString(CultureInfo.InvariantCulture) + "]";
            var json = Json.Msg("snapshot")
                .Num("tick", (long)Find.TickManager.TicksGame)
                .Raw("map", size)
                .Raw("pawns", "[" + string.Join(",", pawns) + "]");
            Pawn player = PlayerPawn.Current(map);
            if (player != null)
            {
                // M12: how fast the played colonist runs now, injuries included, in cells per
                // second of game time, and how many game seconds pass each real second.
                json.Str("player_pawn", player.ThingID)
                    .Num("player_speed", player.GetStatValue(StatDefOf.MoveSpeed))
                    .Num("rate", Find.TickManager.Paused ? 1f : Find.TickManager.TickRateMultiplier);
                string gun = GunTiming(player);
                if (gun != null) json.Raw("player_gun", gun);
            }
            // M18: MW2 holds its player still while RimWorld is paused.
            if (Find.TickManager.Paused) json.Bool("paused", true);
            return json.ToString();
        }

        /// <summary>
        /// M15: how the colonist's gun fires in RimWorld, so MW2 fires it the same way: shots a
        /// burst, seconds between them, the colonist's aiming time and the gun's cooldown (M24:
        /// and its Combat Extended magazine and ammo); null without a ranged gun.
        /// </summary>
        private static string GunTiming(Pawn pawn)
        {
            ThingWithComps gun = pawn.equipment?.Primary;
            Verb verb = pawn.equipment?.PrimaryEq?.PrimaryVerb;
            if (gun == null || verb?.verbProps == null || verb.IsMeleeAttack) return null;
            VerbProperties props = verb.verbProps;
            float warmup = props.warmupTime * pawn.GetStatValue(StatDefOf.AimingDelayFactor);
            float cooldown = gun.GetStatValue(StatDefOf.RangedWeapon_Cooldown);
            var json = new Json()
                .Num("burst", (long)Mathf.Max(1, props.burstShotCount))
                .Num("between", props.ticksBetweenBurstShots / 60f)
                .Num("warmup", warmup)
                .Num("cooldown", cooldown);
            // M20: grenades are thrown, this far.
            if (Throws.Thrown(verb, gun)) json.Num("throw", props.range);
            // M24: under Combat Extended, the gun's magazine and ammo.
            else CombatExtendedCompat.AmmoReport(pawn, gun, verb, json);
            return json.ToString();
        }

        /// <summary>M16: a corpse, as its pawn lying dead where it lies; null if it cannot be drawn.</summary>
        private static string CorpseJson(Corpse corpse)
        {
            Pawn pawn = corpse.InnerPawn;
            try
            {
                Vector3 pos = corpse.DrawPos;
                var json = new Json()
                    .Str("id", pawn.ThingID)
                    .Str("name", pawn.LabelShort)
                    .Str("kind", KindOf(pawn))
                    .Num("x", pos.x)
                    .Num("z", pos.z)
                    .Num("rot", corpse.Rotation.AsAngle)
                    .Str("stance", "prone");
                Pictures(pawn, json);
                float angle;
                try { angle = pawn.Drawer.renderer.BodyAngle(PawnRenderFlags.None); }
                catch { angle = 90f; }
                json.Num("lie", angle);
                return json.Bool("downed", true).Bool("dead", true).Str("faction", FactionOf(pawn)).ToString();
            }
            catch
            {
                return null;
            }
        }

        private static string PawnJson(Pawn pawn)
        {
            var pos = pawn.DrawPos;
            var json = new Json()
                .Str("id", pawn.ThingID)
                .Str("name", pawn.LabelShort)
                .Str("kind", KindOf(pawn))
                .Num("x", pos.x)
                .Num("z", pos.z)
                .Num("rot", pawn.Rotation.AsAngle)
                .Str("stance", pawn.Downed ? "prone" : "stand");
            string weapon = pawn.equipment?.Primary?.def?.defName;
            if (weapon != null) json.Str("weapon", weapon);
            Pictures(pawn, json);
            int hurt = Hurt.Of(pawn);
            if (hurt > 0) json.Num("hurt", (long)hurt);
            // M15: lying down (in bed, asleep, downed), head the way RimWorld turns the body.
            if (pawn.GetPosture() != PawnPosture.Standing)
            {
                json.Num("lie", pawn.Drawer.renderer.BodyAngle(PawnRenderFlags.None));
                if (pawn.CurrentBed() != null) json.Bool("bed", true);
            }
            string carried = Carried(pawn.carryTracker?.CarriedThing);
            if (carried != null) json.Raw("carry", carried);
            // M23: the weapon in its hands, and its melee attacks.
            string held = HeldReport.Held(pawn);
            if (held != null) json.Raw("held", held);
            int swings = HeldReport.Swings(pawn);
            if (swings > 0) json.Num("swings", (long)swings);
            return json.Bool("downed", pawn.Downed).Str("faction", FactionOf(pawn)).ToString();
        }

        /// <summary>M15: what a pawn carries, as RimWorld draws it: a person's portrait, an
        /// animal's body facing south, a thing's own picture; null for nothing.</summary>
        private static string Carried(Thing thing)
        {
            if (thing == null) return null;
            try
            {
                int tex;
                float size;
                bool pawn = thing is Pawn;
                if (thing is Pawn carried && carried.RaceProps.Humanlike)
                {
                    tex = PawnArt.South(carried);
                    size = PawnArt.Span;
                }
                else if (thing is Pawn animal)
                {
                    Graphic body = animal.Drawer?.renderer?.BodyGraphic;
                    if (body == null) return null;
                    tex = TextureExport.Of(body.MatAt(Rot4.South, animal), cutout: true);
                    size = body.drawSize.x;
                }
                else
                {
                    Graphic graphic = thing.Graphic;
                    if (graphic == null) return null;
                    tex = TextureExport.Of(graphic.MatSingleFor(thing), cutout: true);
                    size = Mathf.Max(graphic.drawSize.x, graphic.drawSize.y);
                }
                if (tex < 0) return null;
                var json = new Json().Num("tex", (long)tex).Num("size", size);
                if (pawn) json.Bool("pawn", true);
                return json.ToString();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// RimWorld's pictures of <paramref name="pawn"/>: a person's portrait, else (when
        /// RimWorld could not draw one) their body as drawn on the map; an animal's or mech's
        /// body. IW4L shows a pawn only as these (M22).
        /// </summary>
        private static void Pictures(Pawn pawn, Json json)
        {
            if (!pawn.RaceProps.Humanlike) Art(pawn, json);
            else if (!PawnArt.Art(pawn, json) && PawnArt.CouldNotDraw(pawn)) Art(pawn, json);
        }

        /// <summary>
        /// An animal's or mech's own pictures, facing north, east and south (west is east
        /// mirrored), cut out, and how big RimWorld draws them in cells.
        /// </summary>
        private static void Art(Pawn pawn, Json json)
        {
            try
            {
                Graphic body = pawn.Drawer?.renderer?.BodyGraphic;
                if (body == null) return;
                int north = TextureExport.Of(body.MatAt(Rot4.North, pawn), cutout: true);
                // Art with no east picture draws its west one mirrored.
                int east = TextureExport.Of(body.MatAt(Rot4.East, pawn), flip: body.EastFlipped, cutout: true);
                int south = TextureExport.Of(body.MatAt(Rot4.South, pawn), cutout: true);
                if (north < 0 && east < 0 && south < 0) return;
                // A side RimWorld has no picture for shows another side, not nothing (M22).
                int any = south >= 0 ? south : east >= 0 ? east : north;
                if (north < 0) north = any;
                if (east < 0) east = any;
                if (south < 0) south = any;
                json.Raw("art", "[" + north + "," + east + "," + south + "]")
                    .Raw("draw", "[" + F(body.drawSize.x) + "," + F(body.drawSize.y) + "]");
            }
            catch
            {
                // Art some mods draw their own way: IW4L shows nothing for it.
            }
        }

        private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        private static string KindOf(Pawn pawn)
        {
            RaceProperties race = pawn.RaceProps;
            if (race == null) return "other";
            if (race.Humanlike) return "humanlike";
            if (race.IsMechanoid) return "mech";
            if (race.Animal) return "animal";
            return "other";
        }

        private static string FactionOf(Pawn pawn)
        {
            Faction player = Faction.OfPlayer;
            if (pawn.Faction == player) return "player";
            if (pawn.HostileTo(player)) return "hostile";
            return "neutral";
        }
    }
}
