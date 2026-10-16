using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldMW2
{
    /// <summary>
    /// M2: the MW2 player's bullets. Each one is shown in RimWorld as the played colonist's own
    /// bullet flying where MW2 sent it (a harmless look-alike), and a hit damages the pawn MW2
    /// struck, as the colonist's gun would. Only while RimWorld runs: nothing happens paused.
    /// </summary>
    public static class PlayerShots
    {
        /// <summary>MW2's damage for a hit, times this, is RimWorld's. An MW2 M4 does about 30 up
        /// close, so about 9 here, near RimWorld's assault rifle (11).</summary>
        public const float DamageScale = 0.3f;

        /// <summary>Look-alike bullets in flight: they land without touching anything.</summary>
        private static readonly HashSet<Thing> lookAlikes = new HashSet<Thing>();

        /// <summary>A RimWorld <see cref="Projectile"/>, or Combat Extended's own.</summary>
        internal static bool IsLookAlike(Thing projectile) => lookAlikes.Contains(projectile);

        internal static void Forget(Thing projectile) => lookAlikes.Remove(projectile);

        /// <summary>A <c>player_shot</c>: <paramref name="from"/> and <paramref name="to"/> are
        /// RimWorld <c>x</c> and <c>z</c>; <paramref name="part"/> is where it struck (<see cref="Part"/>).</summary>
        public static void Fired(string id, Vector3 from, Vector3 to, string target, string part, float damage)
        {
            if (Find.TickManager.Paused) return;
            Map map = PawnSnapshot.MapInView();
            Pawn shooter = PlayerPawn.Current(map);
            if (shooter == null || shooter.ThingID != id) return;
            Thing gun = shooter.equipment?.Primary;
            VerbProperties verb = gun?.def?.Verbs?.FirstOrDefault();
            // Combat Extended fires the loaded ammo's bullet, not the gun's default one.
            Verb shooting = shooter.equipment?.PrimaryEq?.PrimaryVerb;
            ThingDef ceBullet = CombatExtendedCompat.Projectile(shooting);
            ShowBullet(shooter, gun, shooting, ceBullet ?? verb?.defaultProjectile, from, to, map);
            if (target == null || damage <= 0f) return;
            Pawn victim = map.mapPawns.AllPawnsSpawned.FirstOrDefault(p => p.ThingID == target);
            if (victim == null || victim.Dead) return;
            float armorPenetration = verb?.defaultProjectile?.projectile?.GetArmorPenetration(gun) ?? 0.15f;
            ThingDef weapon = gun?.def;
            // CE measures armour and penetration on its own scale, and names the bullet as the
            // weapon so a deflected round can turn blunt (as BulletCE does).
            if (CombatExtendedCompat.SharpPenetration(ceBullet, gun, out float cePenetration))
            {
                armorPenetration = cePenetration;
                weapon = ceBullet;
            }
            BodyPartRecord struck = Part(victim, part);
            var hit = new DamageInfo(DamageDefOf.Bullet, Mathf.Max(1f, damage * DamageScale), armorPenetration,
                (victim.Position - shooter.Position).AngleFlat, shooter, struck, weapon, intendedTarget: victim);
            victim.TakeDamage(hit);
        }

        /// <summary>Where on <paramref name="victim"/> MW2's bullet struck (M18), as RimWorld's
        /// body part: <c>head</c> one of the head's outer parts (head, eyes, ears, nose, jaw),
        /// <c>neck</c> the neck, <c>upper</c> the chest, shoulders or arms, <c>lower</c> the
        /// belly or hands, <c>legs</c> the legs and feet. Chosen by size, as RimWorld picks among the
        /// parts at a height. Null, for RimWorld to choose anywhere, for <c>body</c> or a body
        /// without such parts.</summary>
        internal static BodyPartRecord Part(Pawn victim, string part)
        {
            if (part == null || victim?.health?.hediffSet == null) return null;
            var parts = victim.health.hediffSet.GetNotMissingParts(depth: BodyPartDepth.Outside).ToList();
            bool InHead(BodyPartRecord p) => Under(p, BodyPartDefOf.Head);
            IEnumerable<BodyPartRecord> pick;
            switch (part)
            {
                case "head": pick = parts.Where(InHead); break;
                case "neck": pick = parts.Where(p => p.def == BodyPartDefOf.Neck); break;
                case "upper":
                    pick = parts.Where(p => p.def == BodyPartDefOf.Torso
                        || Under(p, BodyPartDefOf.Shoulder) && p.height != BodyPartHeight.Bottom);
                    break;
                case "lower":
                    pick = parts.Where(p => p.def == BodyPartDefOf.Torso
                        || Under(p, BodyPartDefOf.Arm) && p.height == BodyPartHeight.Bottom);
                    break;
                case "legs": pick = parts.Where(p => Under(p, BodyPartDefOf.Leg)); break;
                default: return null;
            }
            var list = pick.ToList();
            if (list.Count == 0) return null;
            return list.RandomElementByWeight(p => Mathf.Max(0.001f, p.coverageAbs));
        }

        private static bool Under(BodyPartRecord part, BodyPartDef def)
        {
            for (BodyPartRecord p = part; p != null; p = p.parent)
                if (p.def == def) return true;
            return false;
        }

        private static void ShowBullet(Pawn shooter, Thing gun, Verb verb, ThingDef bulletDef, Vector3 from, Vector3 to,
            Map map)
        {
            verb?.verbProps?.soundCast?.PlayOneShot(new TargetInfo(shooter.Position, map));
            if (bulletDef == null) return;
            var land = new IntVec3((int)to.x, 0, (int)to.z);
            if (!land.InBounds(map)) land = land.ClampInsideMap(map);
            if (lookAlikes.Count > 512) lookAlikes.RemoveWhere(p => p.Destroyed);
            Thing bullet = GenSpawn.Spawn(bulletDef, shooter.Position, map);
            lookAlikes.Add(bullet);
            if (bullet is Projectile projectile)
                projectile.Launch(shooter, new Vector3(from.x, 0f, from.z), land, land, ProjectileHitFlags.None, false, gun);
            else if (!CombatExtendedCompat.LaunchLookAlike(bullet, shooter, gun, verb, from, land))
            {
                lookAlikes.Remove(bullet);
                bullet.Destroy();
            }
        }
    }

    /// <summary>A look-alike bullet only disappears where it lands.</summary>
    [HarmonyPatch(typeof(Bullet), "Impact")]
    internal static class LookAlikeBulletImpact
    {
        private static bool Prefix(Bullet __instance)
        {
            if (!PlayerShots.IsLookAlike(__instance)) return true;
            PlayerShots.Forget(__instance);
            if (!__instance.Destroyed) __instance.Destroy();
            return false;
        }
    }
}
