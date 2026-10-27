using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimWorldMW2
{
    /// <summary>
    /// M21: the weather and the mess on the ground, for MW2 to show. <c>weather</c> is how hard
    /// it rains and snows, how foggy and how windy the map in view is, as RimWorld blends one
    /// weather into the next; <c>filth</c> is every splotch of blood, firefoam, fuel, vomit,
    /// ash, dirt and the rest near the MW2 player, each where RimWorld draws it. Each is sent
    /// when it changes, not more often than its interval.
    /// </summary>
    public static class WeatherReport
    {
        private const float WeatherSeconds = 1f;
        private const float FilthSeconds = 2f;
        /// <summary>Cells around the MW2 player whose filth is sent (the engine draws 60).</summary>
        private const float FilthReach = 64f;
        private const int MaxFilth = 1500;

        /// <summary>The sky is looked at this often, real seconds: a lightning flash lasts
        /// about half a second.</summary>
        private const float SkySeconds = 0.1f;

        private static string sentWeather, sentFilth, sentSky;
        private static float weatherAt = -10f, filthAt = -10f, skyAt = -10f;

        public static void Reset()
        {
            sentWeather = sentFilth = sentSky = null;
            weatherAt = filthAt = skyAt = -10f;
        }

        /// <summary>
        /// The <c>sky</c> line due now, or null: how much daylight there is and its colour, as
        /// RimWorld's sky has them after everything that changes it: the time of day, the
        /// weather (overcast, a lightning flash), and game conditions (an eclipse, an aurora,
        /// toxic fallout, volcanic winter, noxious haze, unnatural darkness and the rest); and
        /// where the sun is in the day and how high, for MW2's sun, moon and stars.
        /// </summary>
        public static string PollSky()
        {
            Map map = PawnSnapshot.MapInView();
            float now = Time.realtimeSinceStartup;
            if (map == null || now - skyAt < SkySeconds) return null;
            skyAt = now;
            SkyTarget sky = map.skyManager.CurSky;
            // A map RimWorld lights without a sky (in space, say) is lit fully.
            bool unlit = map.Biome != null && map.Biome.disableSkyLighting;
            Color colour = unlit ? Color.white : sky.colors.sky;
            float glow = unlit ? 1f : map.skyManager.CurSkyGlow;
            string line = Json.Msg("sky").Num("glow", Hundredth(glow))
                .Raw("color", "[" + F(colour.r) + "," + F(colour.g) + "," + F(colour.b) + "]")
                .Num("day", Hundredth(GenLocalDate.DayPercent(map)))
                .Num("sun", Hundredth(GenCelestial.CurCelestialSunGlow(map))).ToString();
            if (line == sentSky) return null;
            sentSky = line;
            return line;
        }

        /// <summary>The <c>weather</c> line due now, or null.</summary>
        public static string PollWeather()
        {
            Map map = PawnSnapshot.MapInView();
            float now = Time.realtimeSinceStartup;
            if (map == null || now - weatherAt < WeatherSeconds) return null;
            weatherAt = now;
            WeatherManager weather = map.weatherManager;
            float lerp = weather.TransitionLerpFactor;
            float fog = Mathf.Lerp(Fogginess(weather.lastWeather), Fogginess(weather.curWeather), lerp);
            string line = Json.Msg("weather").Num("rain", Tenth(weather.RainRate)).Num("snow", Tenth(weather.SnowRate))
                .Num("fog", Tenth(fog)).Num("wind", Tenth(weather.CurWindSpeedFactor)).ToString();
            if (line == sentWeather) return null;
            sentWeather = line;
            return line;
        }

        /// <summary>How foggy a weather is, 0 to 1: fog, haze, ash and blown sand hide the view.</summary>
        private static float Fogginess(WeatherDef def)
        {
            if (def == null) return 0f;
            float fog = Mathf.Clamp01(def.sandRate);
            foreach (Type overlay in def.overlayClasses)
            {
                string name = overlay?.Name ?? "";
                if (name.Contains("Fog") || name.Contains("Haze") || name.Contains("Ash") || name.Contains("Sand")
                    || name.Contains("Darkness"))
                    return 1f;
            }
            return fog;
        }

        /// <summary>The <c>filth</c> line due now, or null; its pictures go to the texture list.</summary>
        public static string PollFilth()
        {
            Map map = PawnSnapshot.MapInView();
            float now = Time.realtimeSinceStartup;
            if (map == null || now - filthAt < FilthSeconds) return null;
            filthAt = now;
            Vector3 centre = RimView.Target(map) ?? (Find.CameraDriver != null ? Find.CameraDriver.MapPosition.ToVector3Shifted() : map.Center.ToVector3Shifted());
            var near = new List<(float, Filth)>();
            foreach (Thing thing in map.listerThings.ThingsInGroup(ThingRequestGroup.Filth))
            {
                if (!(thing is Filth filth) || !filth.Spawned) continue;
                float away = (thing.Position.ToVector3Shifted() - centre).MagnitudeHorizontalSquared();
                if (away <= FilthReach * FilthReach) near.Add((away, filth));
            }
            near.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            var sb = new StringBuilder("[");
            int count = 0;
            foreach (var (_, filth) in near)
            {
                if (count >= MaxFilth) break;
                count += Splotches(filth, sb, MaxFilth - count, count == 0);
            }
            string filthJson = sb.Append(']').ToString();
            if (filthJson == sentFilth) return null;
            sentFilth = filthJson;
            return Json.Msg("filth").Raw("filth", filthJson).ToString();
        }

        /// <summary>
        /// The splotches <paramref name="filth"/> is drawn as, into <paramref name="sb"/>, at most
        /// <paramref name="room"/>: as <see cref="Graphic_Cluster"/> prints them (the same random
        /// numbers in the same order, so the same pictures, places, sizes and turns).
        /// </summary>
        private static int Splotches(Filth filth, StringBuilder sb, int room, bool first)
        {
            if (!(filth.Graphic is Graphic_Cluster cluster)) return 0;
            int written = 0;
            Rand.PushState(filth.Position.GetHashCode());
            try
            {
                int count = Mathf.Min(ScatterCount(cluster, filth), room);
                float spread = Value(cluster, PositionVariance, 0.45f);
                float sizeVariance = Value(cluster, SizeVariance, 0.2f);
                Vector2 drawSize = cluster.data?.drawSize ?? Vector2.one;
                for (int i = 0; i < count; i++)
                {
                    Material mat = cluster.MatSingle;
                    Vector3 centre = Centre(cluster, filth, i)
                        + new Vector3(Rand.Range(-spread, spread), 0f, Rand.Range(-spread, spread));
                    float wide = Rand.Range(drawSize.x * (1f - sizeVariance), drawSize.x * (1f + sizeVariance));
                    _ = Rand.Range(drawSize.y * (1f - sizeVariance), drawSize.y * (1f + sizeVariance));
                    float rot = Rand.RangeInclusive(0, 360);
                    if (filth.drawInstances != null && filth.drawInstances.Count == count)
                        rot = filth.drawInstances[i].rotation;
                    _ = Rand.Value;
                    int tex = TextureExport.Of(mat, cutout: true);
                    if (tex < 0) continue;
                    if (!first || written > 0) sb.Append(',');
                    sb.Append("{\"tex\":").Append(tex).Append(",\"size\":").Append(F(wide))
                        .Append(",\"x\":").Append(F(centre.x)).Append(",\"z\":").Append(F(centre.z))
                        .Append(",\"rot\":").Append(Mathf.RoundToInt(rot)).Append('}');
                    written++;
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[RimWorldMW2] filth " + filth.def.defName + ": " + e.Message, filth.def.shortHash ^ 0x5f17);
            }
            finally
            {
                Rand.PopState();
            }
            return written;
        }

        private const string PositionVariance = "PositionVariance";
        private const string SizeVariance = "SizeVariance";

        private static float Value(Graphic_Cluster cluster, string property, float fallback)
        {
            try
            {
                return Traverse.Create(cluster).Property(property).GetValue<float>();
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        private static int ScatterCount(Graphic_Cluster cluster, Filth filth)
        {
            try
            {
                return Traverse.Create(cluster).Method("ScatterCount", new[] { typeof(Thing) }, new object[] { filth }).GetValue<int>();
            }
            catch (Exception)
            {
                return filth.thickness;
            }
        }

        private static Vector3 Centre(Graphic_Cluster cluster, Filth filth, int index)
        {
            try
            {
                return Traverse.Create(cluster).Method("GetCenter", new[] { typeof(Thing), typeof(int) }, new object[] { filth, index }).GetValue<Vector3>();
            }
            catch (Exception)
            {
                return filth.TrueCenter();
            }
        }

        private static float Tenth(float v) => Mathf.Round(v * 10f) / 10f;

        private static float Hundredth(float v) => Mathf.Round(v * 100f) / 100f;

        private static string F(float v) => (Mathf.Round(v * 100f) / 100f).ToString("0.##", CultureInfo.InvariantCulture);
    }
}
