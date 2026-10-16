using HarmonyLib;
using UnityEngine;
using Verse;

namespace RimWorldMW2
{
    /// <summary>Mod entry point: applies Harmony patches and owns the bridge settings.</summary>
    public class BridgeMod : Mod
    {
        public const string HarmonyId = "rimworldmw2.bridge";

        public static BridgeSettings Settings;

        public BridgeMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<BridgeSettings>();
            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll();
            CombatExtendedCompat.Patch(harmony);
            Application.quitting += BridgeGameComponent.Shutdown;
            RimView.RepairPrefs();
            Log.Message($"[RimWorldMW2] loaded, bridge target {Settings.Host}:{Settings.Port}");
        }

        public override string SettingsCategory() => "RimWorld x MW2";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var list = new Listing_Standard();
            list.Begin(inRect);
            list.Label("IW4L bridge address (RimWorld connects, IW4L listens)");
            Settings.Host = list.TextEntryLabeled("Host", Settings.Host);
            string port = Settings.Port.ToString();
            list.TextFieldNumericLabeled("Port", ref Settings.Port, ref port, 1, 65535);
            list.CheckboxLabeled("Connect automatically when a game loads", ref Settings.AutoConnect);
            list.End();
        }
    }

    public class BridgeSettings : ModSettings
    {
        public string Host = "127.0.0.1";
        public int Port = 47820;
        public bool AutoConnect = true;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref Host, "host", "127.0.0.1");
            Scribe_Values.Look(ref Port, "port", 47820);
            Scribe_Values.Look(ref AutoConnect, "autoConnect", true);
        }
    }
}
