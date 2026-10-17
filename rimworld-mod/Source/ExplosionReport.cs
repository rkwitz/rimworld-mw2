using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace RimWorldMW2
{
    /// <summary>
    /// M4: tells IW4L about every explosion on the map in view as it starts, so MW2 can show and
    /// sound it. RimWorld applies the damage itself. Combat Extended starts its explosions its
    /// own way (<see cref="CombatExtendedCompat"/>), and may start one again when it merges
    /// another into it, so each is told once.
    /// </summary>
    [HarmonyPatch(typeof(Explosion), nameof(Explosion.StartExplosion))]
    internal static class ExplosionReport
    {
        private static readonly HashSet<Explosion> told = new HashSet<Explosion>();

        private static void Postfix(Explosion __instance) => Report(__instance);

        internal static void Report(Explosion explosion)
        {
            System.Action<string> send = ShotReport.Send;
            if (send == null || explosion.Map == null || explosion.Map != PawnSnapshot.MapInView()) return;
            if (told.Count > 256) told.RemoveWhere(e => e.Destroyed);
            if (!told.Add(explosion)) return;
            IntVec3 cell = explosion.Position;
            send(Json.Msg("explosion")
                .Num("x", cell.x + 0.5f)
                .Num("z", cell.z + 0.5f)
                .Num("radius", explosion.radius)
                .Str("damage", explosion.damType?.defName ?? "Bomb")
                .ToString());
        }
    }
}
