using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorldMW2
{
    /// <summary>
    /// M20: the played colonist's grenades, thrown from MW2. Fire or G there sends a
    /// <c>player_throw</c> with where the player aimed on the ground; the colonist throws one
    /// there at once, RimWorld's own grenade with its own sound, which flies, lands and goes
    /// off as any other (MW2 already waited RimWorld's aiming and cooldown time between
    /// throws, as it does between a gun's bursts). Under Combat Extended the colonist throws
    /// with CE's own throw, which uses the grenade up and draws the next one
    /// (<see cref="CombatExtendedCompat"/>).
    /// </summary>
    public static class Throws
    {
        /// <summary>Whether <paramref name="verb"/> throws its projectile (grenades, molotovs)
        /// rather than shooting it.</summary>
        internal static bool Thrown(Verb verb, ThingWithComps weapon)
        {
            if (Projectile(verb) == null || verb.IsMeleeAttack) return false;
            if (CombatExtendedCompat.ThrowsGrenade(verb)) return true;
            if (verb.verbProps.rangedFireRulepack?.defName == "Combat_RangedFire_Thrown") return true;
            return weapon?.def.weaponTags?.Any(tag => tag.Contains("Grenade")) ?? false;
        }

        /// <summary>A <c>player_throw</c>: <paramref name="id"/> throws at cell
        /// (<paramref name="x"/>, <paramref name="z"/>), or as far that way as it can.</summary>
        public static void Throw(string id, float x, float z)
        {
            Map map = PawnSnapshot.MapInView();
            Pawn pawn = PlayerPawn.Current(map);
            if (pawn == null || pawn.ThingID != id || Find.TickManager.Paused) return;
            ThingWithComps weapon = pawn.equipment?.Primary;
            Verb verb = pawn.equipment?.PrimaryEq?.PrimaryVerb;
            if (weapon == null || !Thrown(verb, weapon)) return;
            ThingDef projectile = Projectile(verb);
            Vector3 from = pawn.DrawPos;
            var aim = new Vector3(x, 0f, z);
            Vector3 way = aim - new Vector3(from.x, 0f, from.z);
            float range = verb.verbProps.range;
            if (way.magnitude > range) aim = new Vector3(from.x, 0f, from.z) + way.normalized * range;
            IntVec3 cell = aim.ToIntVec3().ClampInsideMap(map);
            if (cell == pawn.Position)
            {
                Messages.Message($"{pawn.LabelShort} can't throw at their own feet.", pawn, MessageTypeDefOf.RejectInput, false);
                return;
            }
            if (CombatExtendedCompat.Launches(verb))
            {
                if (!CombatExtendedCompat.Throw(verb, cell))
                {
                    Messages.Message($"{pawn.LabelShort} can't throw there.", pawn, MessageTypeDefOf.RejectInput, false);
                    return;
                }
            }
            else
            {
                var thrown = (Projectile)GenSpawn.Spawn(projectile, pawn.Position, map);
                thrown.Launch(pawn, from, cell, cell, ProjectileHitFlags.NonTargetWorld, false, weapon);
            }
            verb.verbProps.soundCast?.PlayOneShot(new TargetInfo(pawn.Position, map));
            pawn.records?.Increment(RecordDefOf.ShotsFired);
            Log.Message($"[RimWorldMW2] {pawn.LabelShort} throws {projectile.label} at {cell}");
        }

        /// <summary>What <paramref name="verb"/> launches, RimWorld's or Combat Extended's; null
        /// when it launches nothing.</summary>
        private static ThingDef Projectile(Verb verb) =>
            verb is Verb_LaunchProjectile launch ? launch.Projectile : CombatExtendedCompat.Projectile(verb);
    }
}
