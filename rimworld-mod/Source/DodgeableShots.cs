using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldMW2
{
    /// <summary>
    /// M8: bullets the MW2 player can dodge. RimWorld still decides when each shot is fired and
    /// where it is aimed, but it no longer decides whether a bullet strikes the colonist MW2
    /// plays: IW4L flies every bullet and sends <c>player_struck</c> when one meets the MW2
    /// player, who may have stepped aside or ducked behind a wall since. Only then does the
    /// colonist take the bullet's damage, as RimWorld's own bullet would have dealt it.
    /// </summary>
    public static class DodgeableShots
    {
        /// <summary>What a fired bullet does to whoever it strikes.</summary>
        private sealed class Fired
        {
            public DamageDef damage;
            public ThingDef weapon;
            public Thing launcher;
            public float amount;
            public float penetration;
            public float angle;
            public List<ExtraDamage> extras;
            /// <summary>Combat Extended takes extra damage's armour penetration as written.</summary>
            public bool extraPenetrationAsWritten;
        }

        /// <summary>Bullets IW4L may still report, newest last: enough for any firefight.</summary>
        private const int Remembered = 512;
        private static readonly Dictionary<string, Fired> fired = new Dictionary<string, Fired>();
        private static readonly Queue<string> order = new Queue<string>();

        /// <summary>Remembers a bullet RimWorld just fired, by the id <see cref="ShotReport"/>
        /// sends IW4L with it.</summary>
        internal static void Launched(Projectile bullet, Thing launcher, Vector3 origin, Vector3 destination) =>
            Remember(bullet.ThingID, new Fired
            {
                damage = bullet.def.projectile.damageDef,
                weapon = bullet.EquipmentDef,
                launcher = launcher,
                amount = bullet.DamageAmount,
                penetration = bullet.ArmorPenetration,
                angle = Angle(origin, destination),
                extras = bullet.def.projectile.extraDamages,
            });

        /// <summary>Remembers a Combat Extended bullet (<see cref="CombatExtendedCompat"/>) with
        /// the damage CE's own impact would deal (<c>BulletCE.Impact</c>), as it leaves the gun.
        /// CE names the bullet, not the gun, as the weapon, so its armour can turn a deflected
        /// round into blunt damage.</summary>
        internal static void LaunchedCE(Thing bullet, DamageInfo hit, List<ExtraDamage> extras, Vector3 origin,
            Vector3 destination) =>
            Remember(bullet.ThingID, new Fired
            {
                damage = hit.Def,
                weapon = hit.Weapon,
                launcher = hit.Instigator,
                amount = hit.Amount,
                penetration = hit.ArmorPenetrationInt,
                angle = Angle(origin, destination),
                extras = extras,
                extraPenetrationAsWritten = true,
            });

        private static void Remember(string id, Fired bullet)
        {
            if (fired.ContainsKey(id)) return;
            while (order.Count >= Remembered) fired.Remove(order.Dequeue());
            fired[id] = bullet;
            order.Enqueue(id);
        }

        private static float Angle(Vector3 origin, Vector3 destination)
        {
            Vector3 way = (destination - origin).Yto0();
            return way == Vector3.zero ? 0f : Quaternion.LookRotation(way).eulerAngles.y;
        }

        /// <summary>Whether a RimWorld bullet hitting <paramref name="thing"/> should pass by:
        /// the colonist MW2 plays, while IW4L is there to say what strikes it.</summary>
        internal static bool Spares(Thing thing)
        {
            if (!(thing is Pawn pawn) || !BridgeGameComponent.Linked) return false;
            Map map = PawnSnapshot.MapInView();
            return pawn.Map == map && PlayerPawn.Current(map) == pawn;
        }

        /// <summary>A <c>player_struck</c>: bullet <paramref name="shot"/> met the MW2 player,
        /// who plays <paramref name="pawn"/>; <paramref name="part"/> is <c>head</c> or <c>body</c>.
        /// Each bullet strikes once.</summary>
        public static void Struck(string pawn, string shot, string part)
        {
            if (!fired.TryGetValue(shot, out Fired bullet)) return;
            fired.Remove(shot);
            Map map = PawnSnapshot.MapInView();
            Pawn victim = map?.mapPawns.AllPawnsSpawned.FirstOrDefault(p => p.ThingID == pawn);
            if (victim == null || victim.Dead) return;
            BodyPartRecord head = part == "head"
                ? victim.health.hediffSet.GetNotMissingParts().FirstOrDefault(p => p.def == BodyPartDefOf.Head)
                : null;
            Thing launcher = bullet.launcher != null && !bullet.launcher.Destroyed ? bullet.launcher : null;
            victim.TakeDamage(new DamageInfo(bullet.damage, bullet.amount, bullet.penetration, bullet.angle,
                launcher, head, bullet.weapon, intendedTarget: victim));
            if (bullet.extras == null) return;
            foreach (ExtraDamage extra in bullet.extras)
            {
                if (victim.Dead || !Rand.Chance(extra.chance)) continue;
                float penetration = bullet.extraPenetrationAsWritten ? extra.armorPenetration : extra.AdjustedArmorPenetration();
                victim.TakeDamage(new DamageInfo(extra.def, extra.amount, penetration, bullet.angle,
                    launcher, head, bullet.weapon, intendedTarget: victim));
            }
        }
    }

    /// <summary>A RimWorld bullet reaching the colonist MW2 plays flies on into the ground:
    /// IW4L says when a bullet strikes the MW2 player (<see cref="DodgeableShots.Struck"/>).</summary>
    [HarmonyPatch(typeof(Bullet), "Impact")]
    internal static class DodgeableBulletImpact
    {
        private static void Prefix(Bullet __instance, ref Thing hitThing)
        {
            if (hitThing != null && !PlayerShots.IsLookAlike(__instance) && DodgeableShots.Spares(hitThing))
                hitThing = null;
        }
    }
}
