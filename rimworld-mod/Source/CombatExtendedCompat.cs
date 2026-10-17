using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimWorldMW2
{
    /// <summary>
    /// Combat Extended, when it is loaded. CE swaps RimWorld's projectiles for its own
    /// (<c>ProjectileCE</c>, which is not a <see cref="Projectile"/>): they fly with real
    /// ballistics and find what they hit on the way, instead of rolling hit or miss as they
    /// leave the gun. Its explosions start through <c>StartExplosionCE</c> and its guns and
    /// grenades use verbs of their own. The mod builds without CE, so what is needed from it is
    /// found by name and patched by hand, only when CE is there:
    /// <list type="bullet">
    /// <item>CE's bullets and shells go to IW4L as <c>shot</c> and <c>shell</c> (<see cref="ShotReport"/>)</item>
    /// <item>its bullets pass the colonist MW2 plays, and strike them as CE would when IW4L
    /// says (<see cref="DodgeableShots"/>)</item>
    /// <item>the MW2 player's look-alike bullets are CE's own and touch nothing, and their hits
    /// pierce armour as the loaded ammo does (<see cref="PlayerShots"/>)</item>
    /// <item>the MW2 player's shots use the gun's CE ammo, and MW2's clip is CE's magazine
    /// (<see cref="ReadyShot"/>, <see cref="AmmoReport"/>, <see cref="Reload"/>)</item>
    /// <item>grenades are thrown with CE's own throw (<see cref="Throws"/>)</item>
    /// <item>CE's explosions show in MW2 (<see cref="ExplosionReport"/>)</item>
    /// </list>
    /// </summary>
    public static class CombatExtendedCompat
    {
        /// <summary>How far from a CE bullet's line a pawn it was aimed at may stand and still
        /// be in its way: about the half-width CE gives a person.</summary>
        private const float HitReach = 0.5f;

        private static Type projectileType;
        private static Type bulletType;
        private static Type explosiveType;
        private static Type launchVerbType;
        private static Type throwVerbType;
        private static Type propsType;

        private static AccessTools.FieldRef<object, Vector2> originOf;
        private static AccessTools.FieldRef<object, Vector2> destinationOf;
        private static AccessTools.FieldRef<object, float> shotHeightOf;
        private static AccessTools.FieldRef<object, float> shotSpeedOf;
        private static AccessTools.FieldRef<object, int> ticksToImpactOf;
        private static AccessTools.FieldRef<object, Vector3> velocityOf;
        private static AccessTools.FieldRef<object, float> gravityOf;
        private static AccessTools.FieldRef<object, Thing> launcherOf;
        private static AccessTools.FieldRef<object, ThingDef> equipmentDefOf;
        private static AccessTools.FieldRef<object, LocalTargetInfo> intendedTargetOf;
        private static AccessTools.FieldRef<object, int> ticksToDetonationOf;
        private static AccessTools.FieldRef<object, bool> toBeMergedOf;
        private static AccessTools.FieldRef<object, float> sharpPenetrationOf;
        private static MethodInfo damageInfoOf;
        private static MethodInfo extraDamagesOf;
        private static MethodInfo trajectoryWorkerOf;
        private static MethodInfo shotAngle;
        private static MethodInfo shotRotation;
        private static MethodInfo launch;
        private static MethodInfo verbProjectileOf;
        private static MethodInfo verbShotHeightOf;

        // CompAmmoUser, the gun's magazine and the ammo it draws from the inventory.
        private static Type ammoUserType;
        private static MethodInfo useAmmoOf;
        private static MethodInfo hasMagazineOf;
        private static MethodInfo magSizeOf;
        private static MethodInfo magCountOf;
        private static MethodInfo spareOf;
        private static MethodInfo ammoPropsOf;
        private static MethodInfo tryPrepareShot;
        private static MethodInfo notifyShotFired;
        private static MethodInfo loadAmmo;
        private static MethodInfo tryUnload;
        private static MethodInfo tryFindAmmo;
        private static FieldInfo reloadTimeOf;
        private static FieldInfo oneAtATimeOf;
        private static FieldInfo ammoSetOf;
        private static FieldInfo setPerShotOf;
        private static FieldInfo verbPerShotOf;
        private static FieldInfo equipmentStatsOf;
        // CE's reload stats, found once the defs have loaded (after the mod's constructor patches).
        private static StatDef reloadTimeStat;
        private static StatDef reloadFactorStat;
        private static StatDef reloadSpeedStat;
        private static bool reloadStatsFound;

        private static readonly AccessTools.FieldRef<Verb, LocalTargetInfo> CurrentTargetOf =
            AccessTools.FieldRefAccess<Verb, LocalTargetInfo>("currentTarget");
        private static readonly MethodInfo TryCastShot = AccessTools.Method(typeof(Verb), "TryCastShot");

        /// <summary>Combat Extended is loaded and patched.</summary>
        public static bool Active { get; private set; }

        /// <summary>The MW2 player's shots use CE's ammo (CE is active and its ammo was found).</summary>
        public static bool AmmoActive { get; private set; }

        /// <summary>Patches CE's projectiles, explosions and verbs, if CE is loaded.</summary>
        internal static void Patch(Harmony harmony)
        {
            projectileType = AccessTools.TypeByName("CombatExtended.ProjectileCE");
            if (projectileType == null) return;
            try
            {
                Resolve();
                var self = typeof(CombatExtendedCompat);
                harmony.Patch(AccessTools.Method(projectileType, "Launch", new[] { typeof(Thing), typeof(Vector2), typeof(Thing) }),
                    postfix: new HarmonyMethod(self, nameof(LaunchPostfix)));
                harmony.Patch(AccessTools.Method(projectileType, "Throw", new[] { typeof(Thing), typeof(Vector3), typeof(Vector3), typeof(Thing) }),
                    postfix: new HarmonyMethod(self, nameof(ThrowPostfix)));
                harmony.Patch(AccessTools.Method(projectileType, "CanCollideWith"),
                    prefix: new HarmonyMethod(self, nameof(CanCollideWithPrefix)));
                // Every CE projectile that lands its own way, so a look-alike lands as nothing.
                foreach (Type type in AccessTools.GetTypesFromAssembly(projectileType.Assembly))
                {
                    if (!projectileType.IsAssignableFrom(type)) continue;
                    MethodInfo impact = AccessTools.DeclaredMethod(type, "Impact", new[] { typeof(Thing) });
                    if (impact != null && !impact.IsAbstract)
                        harmony.Patch(impact, prefix: new HarmonyMethod(self, nameof(ImpactPrefix)));
                }
                harmony.Patch(AccessTools.Method("CombatExtended.ExplosionCE:StartExplosionCE"),
                    postfix: new HarmonyMethod(self, nameof(StartExplosionPostfix)));
                Active = true;
                Log.Message("[RimWorldMW2] Combat Extended found: its shots, grenades and explosions go to MW2");
            }
            catch (Exception e)
            {
                Log.Error($"[RimWorldMW2] Combat Extended found, but patching it failed, so its shots won't show in MW2: {e}");
                return;
            }
            try
            {
                ResolveAmmo();
                // CE reloads a drafted colonist's half-empty gun with a job, which would stop the
                // colonist MW2 plays; MW2's player reloads when the magazine is empty instead.
                harmony.Patch(Need(AccessTools.Method("CombatExtended.JobGiver_CheckReload:TryGiveJob"), "JobGiver_CheckReload.TryGiveJob"),
                    prefix: new HarmonyMethod(typeof(CombatExtendedCompat), nameof(CheckReloadPrefix)));
                AmmoActive = true;
            }
            catch (Exception e)
            {
                Log.Warning($"[RimWorldMW2] Combat Extended's ammo wasn't found, so the MW2 player's shots use none: {e}");
            }
        }

        /// <summary>Finds CE's ammo by name; throws when something needed has gone.</summary>
        private static void ResolveAmmo()
        {
            ammoUserType = Need(AccessTools.TypeByName("CombatExtended.CompAmmoUser"), "CompAmmoUser");
            Type ammoPropsType = Need(AccessTools.TypeByName("CombatExtended.CompProperties_AmmoUser"), "CompProperties_AmmoUser");
            Type ammoSetType = Need(AccessTools.TypeByName("CombatExtended.AmmoSetDef"), "AmmoSetDef");
            Type verbPropsType = Need(AccessTools.TypeByName("CombatExtended.VerbPropertiesCE"), "VerbPropertiesCE");
            MethodInfo Getter(string name) => Need(AccessTools.PropertyGetter(ammoUserType, name), "CompAmmoUser." + name);
            FieldInfo Field(Type type, string name) => Need(AccessTools.Field(type, name), type.Name + "." + name);
            useAmmoOf = Getter("UseAmmo");
            hasMagazineOf = Getter("HasMagazine");
            magSizeOf = Getter("MagSize");
            magCountOf = Getter("CurMagCount");
            spareOf = AccessTools.PropertyGetter(ammoUserType, "MagsLeftReadOnly"); // newer CE only
            ammoPropsOf = Getter("Props");
            tryPrepareShot = Need(AccessTools.Method(ammoUserType, "TryPrepareShot", Type.EmptyTypes), "CompAmmoUser.TryPrepareShot");
            notifyShotFired = Need(AccessTools.Method(ammoUserType, "Notify_ShotFired", new[] { typeof(int) }), "CompAmmoUser.Notify_ShotFired");
            loadAmmo = Need(AccessTools.Method(ammoUserType, "LoadAmmo", new[] { typeof(Thing), typeof(bool) }), "CompAmmoUser.LoadAmmo");
            tryUnload = Need(AccessTools.Method(ammoUserType, "TryUnload", new[] { typeof(bool) }), "CompAmmoUser.TryUnload");
            tryFindAmmo = Need(AccessTools.Method(ammoUserType, "TryFindAmmoInInventory", new[] { typeof(Thing).MakeByRefType() }),
                "CompAmmoUser.TryFindAmmoInInventory");
            reloadTimeOf = Field(ammoPropsType, "reloadTime");
            oneAtATimeOf = Field(ammoPropsType, "reloadOneAtATime");
            ammoSetOf = Field(ammoPropsType, "ammoSet");
            setPerShotOf = Field(ammoSetType, "ammoConsumedPerShot");
            verbPerShotOf = Field(verbPropsType, "ammoConsumedPerShotCount");
            equipmentStatsOf = AccessTools.Field(verbPropsType, "useEquipmentStatValues");
        }

        /// <summary>Finds CE's types and members by name; throws when one has gone.</summary>
        private static void Resolve()
        {
            bulletType = Need(AccessTools.TypeByName("CombatExtended.BulletCE"), "BulletCE");
            explosiveType = Need(AccessTools.TypeByName("CombatExtended.ProjectileCE_Explosive"), "ProjectileCE_Explosive");
            launchVerbType = Need(AccessTools.TypeByName("CombatExtended.Verb_LaunchProjectileCE"), "Verb_LaunchProjectileCE");
            throwVerbType = Need(AccessTools.TypeByName("CombatExtended.Verb_ThrowGrenade"), "Verb_ThrowGrenade");
            propsType = Need(AccessTools.TypeByName("CombatExtended.ProjectilePropertiesCE"), "ProjectilePropertiesCE");
            Type workerType = Need(AccessTools.TypeByName("CombatExtended.BaseTrajectoryWorker"), "BaseTrajectoryWorker");
            Type explosionType = Need(AccessTools.TypeByName("CombatExtended.ExplosionCE"), "ExplosionCE");

            originOf = AccessTools.FieldRefAccess<Vector2>(projectileType, "origin");
            destinationOf = AccessTools.FieldRefAccess<Vector2>(projectileType, "Destination");
            shotHeightOf = AccessTools.FieldRefAccess<float>(projectileType, "shotHeight");
            shotSpeedOf = AccessTools.FieldRefAccess<float>(projectileType, "shotSpeed");
            ticksToImpactOf = AccessTools.FieldRefAccess<int>(projectileType, "intTicksToImpact");
            velocityOf = AccessTools.FieldRefAccess<Vector3>(projectileType, "velocity");
            gravityOf = AccessTools.FieldRefAccess<float>(projectileType, "GravityPerHeight");
            launcherOf = AccessTools.FieldRefAccess<Thing>(projectileType, "launcher");
            equipmentDefOf = AccessTools.FieldRefAccess<ThingDef>(projectileType, "equipmentDef");
            intendedTargetOf = AccessTools.FieldRefAccess<LocalTargetInfo>(projectileType, "intendedTarget");
            ticksToDetonationOf = AccessTools.FieldRefAccess<int>(explosiveType, "ticksToDetonation");
            toBeMergedOf = AccessTools.FieldRefAccess<bool>(explosionType, "toBeMerged");
            sharpPenetrationOf = AccessTools.FieldRefAccess<float>(propsType, "armorPenetrationSharp");
            damageInfoOf = Need(AccessTools.PropertyGetter(projectileType, "DamageInfo"), "ProjectileCE.DamageInfo");
            extraDamagesOf = Need(AccessTools.PropertyGetter(projectileType, "ExtraDamages"), "ProjectileCE.ExtraDamages");
            trajectoryWorkerOf = Need(AccessTools.PropertyGetter(propsType, "TrajectoryWorker"), "TrajectoryWorker");
            shotAngle = Need(AccessTools.Method(workerType, "ShotAngle"), "ShotAngle");
            shotRotation = Need(AccessTools.Method(workerType, "ShotRotation"), "ShotRotation");
            launch = Need(AccessTools.Method(projectileType, "Launch", new[]
            {
                typeof(Thing), typeof(Vector2), typeof(float), typeof(float), typeof(float), typeof(float), typeof(Thing), typeof(float),
            }), "ProjectileCE.Launch");
            verbProjectileOf = Need(AccessTools.PropertyGetter(launchVerbType, "Projectile"), "Verb_LaunchProjectileCE.Projectile");
            verbShotHeightOf = Need(AccessTools.PropertyGetter(launchVerbType, "ShotHeight"), "Verb_LaunchProjectileCE.ShotHeight");
        }

        private static T Need<T>(T found, string name) where T : class =>
            found ?? throw new MissingMemberException("CombatExtended", name);

        // ---------------------------------------------------------------- verbs

        /// <summary>Whether <paramref name="verb"/> is one of CE's shooting or throwing verbs.</summary>
        internal static bool Launches(Verb verb) => Active && verb != null && launchVerbType.IsInstanceOfType(verb);

        /// <summary>Whether <paramref name="verb"/> is CE's grenade throw.</summary>
        internal static bool ThrowsGrenade(Verb verb) => Active && verb != null && throwVerbType.IsInstanceOfType(verb);

        /// <summary>What CE's <paramref name="verb"/> fires now (the loaded ammo's projectile), or
        /// null for any other verb.</summary>
        internal static ThingDef Projectile(Verb verb)
        {
            if (!Launches(verb)) return null;
            try
            {
                return verbProjectileOf.Invoke(verb, null) as ThingDef;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>The armour penetration of a bullet <paramref name="projectile"/> from
        /// <paramref name="gun"/>, as CE measures it (against sharp armour, which bullets meet).
        /// False for a projectile CE hasn't given its own properties.</summary>
        internal static bool SharpPenetration(ThingDef projectile, Thing gun, out float penetration)
        {
            penetration = 0f;
            if (!Active || !propsType.IsInstanceOfType(projectile?.projectile)) return false;
            float multiplier = gun?.GetStatValue(StatDefOf.RangedWeapon_ArmorPenetrationMultiplier) ?? 1f;
            penetration = sharpPenetrationOf(projectile.projectile) * multiplier;
            return true;
        }

        /// <summary>The played colonist throws CE's grenade (or other thrown weapon) at
        /// <paramref name="cell"/>, the way CE throws it, which also uses it up and draws the
        /// next one. False when CE could not throw it there.</summary>
        internal static bool Throw(Verb verb, IntVec3 cell)
        {
            try
            {
                CurrentTargetOf(verb) = new LocalTargetInfo(cell);
                return (bool)TryCastShot.Invoke(verb, null);
            }
            catch (Exception e)
            {
                Log.ErrorOnce($"[RimWorldMW2] Combat Extended throw failed: {e}", 0x3C4E0001);
                return false;
            }
        }

        // ---------------------------------------------------------------- ammo

        /// <summary>The CE ammo (<c>CompAmmoUser</c>) of <paramref name="gun"/> when it takes
        /// any: a magazine, or ammo from the inventory while CE's ammo system is on. Else null.</summary>
        private static ThingComp AmmoUser(Thing gun)
        {
            if (!AmmoActive || !(gun is ThingWithComps withComps)) return null;
            ThingComp ammo = withComps.AllComps.FirstOrDefault(comp => ammoUserType.IsInstanceOfType(comp));
            if (ammo == null) return null;
            return Is(useAmmoOf, ammo) || Is(hasMagazineOf, ammo) ? ammo : null;
        }

        private static bool Is(MethodInfo getter, object ammo) => (bool)getter.Invoke(ammo, null);

        private static int Count(MethodInfo getter, object ammo) => (int)getter.Invoke(ammo, null);

        /// <summary>Readies a shot of the MW2 player's <paramref name="gun"/> as CE would, from
        /// its magazine or, for a gun without one, by taking a round from the inventory. False
        /// when there is nothing to fire: the shot then does nothing in RimWorld. Always true for
        /// a gun CE doesn't feed.</summary>
        internal static bool ReadyShot(Thing gun)
        {
            ThingComp ammo = AmmoUser(gun);
            if (ammo == null) return true;
            try
            {
                return (bool)tryPrepareShot.Invoke(ammo, null);
            }
            catch (Exception e)
            {
                Log.ErrorOnce($"[RimWorldMW2] Combat Extended ammo check failed: {e}", 0x3C4E0004);
                return true;
            }
        }

        /// <summary>Uses up the ammo of a shot <see cref="ReadyShot"/> readied, as many rounds as
        /// CE's own shot from <paramref name="verb"/> takes. An emptied magazine stays empty
        /// until MW2 reloads (<see cref="Reload"/>).</summary>
        internal static void ShotFired(Thing gun, Verb verb)
        {
            ThingComp ammo = AmmoUser(gun);
            if (ammo == null) return;
            try
            {
                notifyShotFired.Invoke(ammo, new object[] { PerShot(ammo, verb) });
            }
            catch (Exception e)
            {
                Log.ErrorOnce($"[RimWorldMW2] Combat Extended ammo use failed: {e}", 0x3C4E0005);
            }
        }

        /// <summary>Rounds a shot takes, as CE's <c>Verb_ShootCE</c> counts them.</summary>
        private static int PerShot(ThingComp ammo, Verb verb)
        {
            object set = ammoSetOf.GetValue(ammoPropsOf.Invoke(ammo, null));
            int perShot = set != null ? (int)setPerShotOf.GetValue(set) : 1;
            if (verb?.verbProps != null && verbPerShotOf.DeclaringType.IsInstanceOfType(verb.verbProps))
                perShot *= (int)verbPerShotOf.GetValue(verb.verbProps);
            return Mathf.Max(1, perShot);
        }

        /// <summary>The MW2 player started reloading <paramref name="gun"/>: CE reloads its
        /// magazine at once, as its reload job would, while MW2 plays the reload out. The
        /// rounds still in it go back to the inventory first (a gun that loads a round at a
        /// time keeps them), then it is filled from there. Nothing for a gun CE doesn't feed.</summary>
        internal static void Reload(Thing gun)
        {
            ThingComp ammo = AmmoUser(gun);
            if (ammo == null || !Is(hasMagazineOf, ammo)) return;
            try
            {
                int size = Count(magSizeOf, ammo);
                if (Count(magCountOf, ammo) >= size) return;
                // Keep a full-enough magazine when there is nothing to load it with.
                if (Is(useAmmoOf, ammo) && !(bool)tryFindAmmo.Invoke(ammo, new object[] { null })) return;
                tryUnload.Invoke(ammo, new object[] { false });
                Refill(ammo);
            }
            catch (Exception e)
            {
                Log.ErrorOnce($"[RimWorldMW2] Combat Extended reload failed: {e}", 0x3C4E0007);
            }
        }

        /// <summary>Fills the magazine from the inventory, as far as the ammo carried
        /// goes, without CE's reload job: that job (<c>TryStartReload</c>) takes the colonist
        /// over and would fight MW2 for it. Never called with no ammo to load, as CE's
        /// <c>LoadAmmo</c> would then send the colonist off to find some or change weapons.</summary>
        private static void Refill(ThingComp ammo)
        {
            bool useAmmo = Is(useAmmoOf, ammo);
            int size = Count(magSizeOf, ammo);
            // Shotguns and bolt actions load a round at a time, the rest a magazine at once.
            int loads = (bool)oneAtATimeOf.GetValue(ammoPropsOf.Invoke(ammo, null)) ? size : 1;
            for (int i = 0; i < loads && Count(magCountOf, ammo) < size; i++)
            {
                if (useAmmo)
                {
                    var found = new object[] { null };
                    if (!(bool)tryFindAmmo.Invoke(ammo, found)) return;
                }
                int before = Count(magCountOf, ammo);
                loadAmmo.Invoke(ammo, new object[] { null, false });
                if (Count(magCountOf, ammo) <= before) return;
            }
        }

        /// <summary>
        /// For <c>player_gun</c>, CE's ammo for <paramref name="pawn"/>'s <paramref name="gun"/>:
        /// <c>mag</c> and <c>loaded</c>, its magazine's size and the rounds in it, and
        /// <c>reload</c>, CE's seconds to fill it from empty, for a gun with a magazine;
        /// <c>spare</c>, the ammo carried for it, while CE's ammo system is on. Nothing for a gun
        /// CE doesn't feed.
        /// </summary>
        internal static void AmmoReport(Pawn pawn, Thing gun, Verb verb, Json json)
        {
            ThingComp ammo = AmmoUser(gun);
            if (ammo == null) return;
            try
            {
                if (Is(hasMagazineOf, ammo))
                {
                    int size = Count(magSizeOf, ammo);
                    json.Num("mag", (long)size)
                        .Num("loaded", (long)Mathf.Clamp(Count(magCountOf, ammo), 0, size))
                        .Num("reload", ReloadSeconds(pawn, gun, verb, ammo, size));
                }
                if (Is(useAmmoOf, ammo) && spareOf != null) json.Num("spare", (long)Count(spareOf, ammo));
            }
            catch (Exception e)
            {
                Log.ErrorOnce($"[RimWorldMW2] couldn't report Combat Extended ammo: {e}", 0x3C4E0006);
            }
        }

        /// <summary>How long CE's reload job takes to fill the empty magazine of
        /// <paramref name="size"/> rounds, in seconds of game time (<c>JobDriver_Reload</c>'s
        /// wait, for each round of a gun that loads one at a time).</summary>
        private static float ReloadSeconds(Pawn pawn, Thing gun, Verb verb, ThingComp ammo, int size)
        {
            if (!reloadStatsFound)
            {
                reloadTimeStat = DefDatabase<StatDef>.GetNamedSilentFail("ReloadTime");
                reloadFactorStat = DefDatabase<StatDef>.GetNamedSilentFail("CE_RangedWeapon_ReloadFactor");
                reloadSpeedStat = DefDatabase<StatDef>.GetNamedSilentFail("ReloadSpeed");
                reloadStatsFound = true;
            }
            object props = ammoPropsOf.Invoke(ammo, null);
            float seconds = (float)reloadTimeOf.GetValue(props);
            object verbProps = verb?.verbProps;
            if (reloadTimeStat != null && equipmentStatsOf != null && equipmentStatsOf.DeclaringType.IsInstanceOfType(verbProps)
                && (bool)equipmentStatsOf.GetValue(verbProps))
                seconds = gun.GetStatValue(reloadTimeStat);
            if (reloadFactorStat != null) seconds *= gun.GetStatValue(reloadFactorStat);
            if (reloadSpeedStat != null) seconds /= Mathf.Max(0.001f, pawn.GetStatValue(reloadSpeedStat));
            if ((bool)oneAtATimeOf.GetValue(props)) seconds *= Mathf.Max(1, size);
            return seconds;
        }

        // ---------------------------------------------------------------- look-alikes

        /// <summary>Launches a CE look-alike bullet (<see cref="PlayerShots"/>) from
        /// <paramref name="from"/> to land on <paramref name="land"/>. False if it couldn't.</summary>
        internal static bool LaunchLookAlike(Thing bullet, Pawn shooter, Thing gun, Verb verb, Vector3 from, IntVec3 land)
        {
            if (!Active || !projectileType.IsInstanceOfType(bullet) || !propsType.IsInstanceOfType(bullet.def.projectile))
                return false;
            try
            {
                object props = bullet.def.projectile;
                object worker = trajectoryWorkerOf.Invoke(props, null);
                float height = Launches(verb) ? (float)verbShotHeightOf.Invoke(verb, null) : ShotReport.MuzzleHeight;
                float speed = bullet.def.projectile.speed;
                var source = new Vector3(from.x, height, from.z);
                Vector3 target = land.ToVector3Shifted().Yto0();
                float angle = (float)shotAngle.Invoke(worker, new object[] { props, source, target, speed });
                float rotation = (float)shotRotation.Invoke(worker, new object[] { props, source, target });
                launch.Invoke(bullet, new object[] { shooter, new Vector2(from.x, from.z), angle, rotation, height, speed, gun, -1f });
                return true;
            }
            catch (Exception e)
            {
                Log.ErrorOnce($"[RimWorldMW2] Combat Extended look-alike bullet failed: {e}", 0x3C4E0002);
                return false;
            }
        }

        // ---------------------------------------------------------------- patches

        private static void LaunchPostfix(Thing __instance) => Report(__instance, thrown: false);

        private static void ThrowPostfix(Thing __instance) => Report(__instance, thrown: true);

        /// <summary>CE's bullets fly past the colonist MW2 plays (IW4L says what strikes them),
        /// and look-alikes past everything.</summary>
        private static bool CanCollideWithPrefix(Thing __instance, Thing thing, ref float dist, ref bool __result)
        {
            if (PlayerShots.IsLookAlike(__instance)
                || bulletType.IsInstanceOfType(__instance) && DodgeableShots.Spares(thing))
            {
                dist = -1f;
                __result = false;
                return false;
            }
            return true;
        }

        /// <summary>A look-alike only disappears where it lands.</summary>
        private static bool ImpactPrefix(Thing __instance)
        {
            if (!PlayerShots.IsLookAlike(__instance)) return true;
            PlayerShots.Forget(__instance);
            if (!__instance.Destroyed) __instance.Destroy();
            return false;
        }

        /// <summary>No reload job for the colonist MW2 plays (<see cref="Refill"/> reloads it).</summary>
        private static bool CheckReloadPrefix(Pawn pawn, ref Job __result)
        {
            if (!PlayerPawn.Controls(pawn)) return true;
            __result = null;
            return false;
        }

        /// <summary>A CE explosion goes off; one merged into another shows as that one.</summary>
        private static void StartExplosionPostfix(Explosion __instance)
        {
            if (!__instance.Spawned || toBeMergedOf(__instance)) return;
            ExplosionReport.Report(__instance);
        }

        // ---------------------------------------------------------------- reports

        /// <summary>Tells IW4L about a CE projectile as it leaves, like <see cref="ShotReport"/>
        /// does for RimWorld's own: bullets as <c>shot</c>s, anything else as a <c>shell</c>.</summary>
        private static void Report(Thing projectile, bool thrown)
        {
            if (ShotReport.Send == null || projectile.Map == null || PlayerShots.IsLookAlike(projectile)) return;
            if (projectile.Map != PawnSnapshot.MapInView()) return;
            try
            {
                if (!thrown && bulletType.IsInstanceOfType(projectile)) ReportBullet(projectile);
                else ReportShell(projectile, thrown);
            }
            catch (Exception e)
            {
                Log.ErrorOnce($"[RimWorldMW2] couldn't report a Combat Extended projectile: {e}", 0x3C4E0003);
            }
        }

        /// <summary>A CE bullet hits nothing yet as it leaves: it flies its line to the ground at
        /// <c>Destination</c>. It goes to IW4L as a hit when that line runs through the pawn it
        /// was aimed at, ending there, so a bullet aimed at the MW2 player that would meet them
        /// can strike them (and one CE's spread sends wide cannot).</summary>
        private static void ReportBullet(Thing bullet)
        {
            Vector2 origin = originOf(bullet);
            Vector2 to = destinationOf(bullet);
            float toHeight = 0f;
            Thing target = intendedTargetOf(bullet).Thing;
            if (!(target is Pawn pawn && pawn.Spawned && InTheWay(origin, to, pawn, out Vector2 at))) target = null;
            else
            {
                to = at;
                toHeight = ShotReport.ImpactHeight;
            }
            Thing launcher = launcherOf(bullet);
            var from3 = new Vector3(origin.x, 0f, origin.y);
            var to3 = new Vector3(to.x, 0f, to.y);
            var hit = (DamageInfo)damageInfoOf.Invoke(bullet, null);
            var extras = ((IEnumerable<ExtraDamage>)extraDamagesOf.Invoke(bullet, null))?.ToList();
            DodgeableShots.LaunchedCE(bullet, hit, extras, from3, to3);
            float speed = shotSpeedOf(bullet) * Find.TickManager.TickRateMultiplier;
            ShotReport.SendShot(bullet, launcher, equipmentDefOf(bullet), from3, to3, toHeight, target, speed);
        }

        /// <summary>Whether the line <paramref name="from"/> to <paramref name="to"/> runs
        /// through <paramref name="pawn"/>, and where it passes them.</summary>
        private static bool InTheWay(Vector2 from, Vector2 to, Pawn pawn, out Vector2 at)
        {
            at = to;
            Vector2 way = to - from;
            float length = way.sqrMagnitude;
            if (length < 0.0001f) return false;
            var stands = new Vector2(pawn.DrawPos.x, pawn.DrawPos.z);
            float along = Vector2.Dot(stands - from, way) / length;
            if (along < 0f || along > 1f) return false;
            at = from + way * along;
            return (stands - at).sqrMagnitude <= HitReach * HitReach;
        }

        /// <summary>A CE shell, rocket or grenade. Grenades are thrown (<c>Throw</c>) at the
        /// cell aimed at; everything else is launched to land on its <c>Destination</c>.</summary>
        private static void ReportShell(Thing shell, bool thrown)
        {
            Vector2 origin = originOf(shell);
            Vector3 velocity = velocityOf(shell); // cells a tick
            float fall = gravityOf(shell) / (60f * 60f); // cells a tick, each tick
            Vector2 to;
            float ticks;
            LocalTargetInfo aim = intendedTargetOf(shell);
            if (thrown && aim.IsValid)
            {
                Vector3 cell = aim.CenterVector3;
                to = new Vector2(cell.x, cell.z);
                float across = new Vector2(velocity.x, velocity.z).magnitude;
                ticks = across > 0.0001f ? (to - origin).magnitude / across : 1f;
            }
            else
            {
                to = destinationOf(shell);
                ticks = ticksToImpactOf(shell);
            }
            // How high it climbs over where it left: the top of its fall.
            float lob = fall > 0f && velocity.y > 0f ? velocity.y * velocity.y / (2f * fall) : 0f;
            int fuse = 0;
            if (explosiveType.IsInstanceOfType(shell))
            {
                int detonation = ticksToDetonationOf(shell);
                fuse = detonation > 0 ? Mathf.Max(detonation - Mathf.CeilToInt(ticks), 0) : shell.def.projectile.explosionDelay;
            }
            ShotReport.SendShell(shell, launcherOf(shell), new Vector3(origin.x, 0f, origin.y), new Vector3(to.x, 0f, to.y),
                ticks, lob, fuse);
        }
    }
}
