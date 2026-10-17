using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Verse;

namespace RimWorldMW2
{
    /// <summary>
    /// M5: RimWorld's own art for IW4L. Copies a material's texture (or part of it) to a PNG,
    /// <see cref="Size"/> pixels square and tinted as RimWorld draws it, under RimWorld's save
    /// data folder, once per art and colour. The files stay on this computer; IW4L reads them
    /// by the names in <see cref="Names"/>. Main thread only.
    /// </summary>
    public static class TextureExport
    {
        public const int Size = 128;
        /// <summary>M16: ground art is saved whole at this size, as it spans 16 cells (one
        /// paved tile a cell), and named <c>..._floor</c>.</summary>
        public const int FloorSize = 512;
        /// <summary>Linked art (walls, fences) is a 4 by 4 atlas; this piece links all ways.</summary>
        public const int AllLinked = 15;
        /// <summary>The piece linked every way but south: the wall's front face shows along its bottom.</summary>
        public const int FaceLinked = 11;

        private static readonly Dictionary<string, int> byKey = new Dictionary<string, int>();
        public static readonly List<string> Names = new List<string>();
        /// <summary>Counts new names, so the list is sent again when it grows.</summary>
        public static int Revision;
        /// <summary>M25: how many pictures IW4L's atlas holds (<c>SMALL_TILES</c> in the
        /// engine); a picture past it is never shown.</summary>
        public const int Capacity = 1536;
        /// <summary>Room kept for the pictures a frame can add before the list starts over.</summary>
        private const int Headroom = 160;

        /// <summary>M25: the list is close to what IW4L holds.</summary>
        public static bool NearlyFull => Names.Count >= Capacity - Headroom;

        /// <summary>M25: forgets every picture, so each is numbered again as it is next used
        /// and the list is sent again from the first. IW4L makes its atlas again when the list
        /// does not go on from the last one. The files stay and are saved over.</summary>
        public static void StartOver()
        {
            byKey.Clear();
            Names.Clear();
            Revision++;
        }
        private static string dir;

        public static string Dir
        {
            get
            {
                if (dir == null)
                {
                    dir = Path.Combine(GenFilePaths.SaveDataFolderPath, "RimWorldMW2", "textures");
                    Directory.CreateDirectory(dir);
                }
                return dir;
            }
        }

        /// <summary>The <c>textures</c> line: the folder and every name so far.</summary>
        public static string Line()
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < Names.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(Names[i]).Append('"');
            }
            return Json.Msg("textures").Str("dir", Dir.Replace('\\', '/')).Raw("names", sb.Append(']').ToString()).ToString();
        }

        /// <summary>
        /// The index of <paramref name="mat"/>'s art, saving it first if it is new: the whole
        /// texture, or for linked art the piece <paramref name="linked"/>, mirrored when
        /// <paramref name="flip"/>. A <paramref name="cutout"/> (a pawn, something on the ground)
        /// is named <c>..._cut</c> so IW4L keeps its see-through parts; a <paramref name="floor"/>
        /// (ground art) <c>..._floor</c>, <see cref="FloorSize"/> square. -1 when it has none or
        /// it could not be read.
        /// </summary>
        public static int Of(Material mat, int linked = -1, bool flip = false, bool cutout = false, bool floor = false)
        {
            Texture texture = mat?.mainTexture;
            if (texture == null) return -1;
            Color tint = mat.HasProperty("_Color") ? mat.color : Color.white;
            // M12: two-colour art (beds, armchairs, clothes) has a mask; where it is red the
            // first colour (the stuff) shows, elsewhere the second (a bed's blankets), as
            // RimWorld's CutoutComplex shader does.
            Texture mask = mat.HasProperty("_MaskTex") ? mat.GetTexture("_MaskTex") : null;
            Color two = mask != null && mat.HasProperty("_ColorTwo") ? mat.GetColor("_ColorTwo") : tint;
            string key = Name(texture) + "_" + ColorUtility.ToHtmlStringRGB(tint)
                + (mask != null ? "_" + ColorUtility.ToHtmlStringRGB(two) : "")
                + (linked >= 0 ? "_l" + linked : "") + (flip ? "_f" : "") + (cutout ? "_cut" : "") + (floor ? "_floor" : "");
            if (byKey.TryGetValue(key, out int index)) return index;
            index = -1;
            try
            {
                Vector2 scale = Vector2.one, offset = Vector2.zero;
                if (linked >= 0)
                {
                    // As MaterialAtlasPool cuts its sub-materials.
                    scale = new Vector2(0.1875f, 0.1875f);
                    offset = new Vector2(linked % 4 * 0.25f + 1f / 32f, linked / 4 * 0.25f + 1f / 32f);
                }
                int size = floor ? FloorSize : Size;
                Color32[] pixels = Pixels(texture, scale, offset, size);
                Color32[] masked = mask != null ? Pixels(mask, scale, offset, size) : null;
                var image = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var out_ = new Color32[pixels.Length];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        int from = y * size + (flip ? size - 1 - x : x);
                        Color32 p = pixels[from];
                        Color c = masked == null ? tint : Color.Lerp(two, tint, masked[from].r / 255f);
                        out_[y * size + x] = new Color32(
                            (byte)(p.r * c.r), (byte)(p.g * c.g), (byte)(p.b * c.b), (byte)(p.a * tint.a));
                    }
                image.SetPixels32(out_);
                image.Apply();
                File.WriteAllBytes(Path.Combine(Dir, key + ".png"), image.EncodeToPNG());
                UnityEngine.Object.Destroy(image);
                index = Names.Count;
                Names.Add(key);
                Revision++;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[RimWorldMW2] could not save texture " + key + ": " + e.Message, key.GetHashCode());
            }
            byKey[key] = index;
            return index;
        }

        /// <summary>A texture's pixels, <paramref name="size"/> square, read through the GPU since
        /// RimWorld's textures are not readable.</summary>
        private static Color32[] Pixels(Texture texture, Vector2 scale, Vector2 offset, int size)
        {
            RenderTexture rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            Graphics.Blit(texture, rt, scale, offset);
            RenderTexture.active = rt;
            var image = new Texture2D(size, size, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            Color32[] pixels = image.GetPixels32();
            UnityEngine.Object.Destroy(image);
            return pixels;
        }

        /// <summary>
        /// The index of a picture RimWorld just drew for us (a colonist's portrait), saved under
        /// <paramref name="key"/> the first time; cut out, so IW4L keeps its see-through parts.
        /// -1 when it could not be read.
        /// </summary>
        public static int OfDrawn(RenderTexture drawn, string key)
        {
            key = Safe(key) + "_cut";
            if (byKey.TryGetValue(key, out int index)) return index;
            index = -1;
            try
            {
                RenderTexture square = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32);
                RenderTexture previous = RenderTexture.active;
                Graphics.Blit(drawn, square);
                RenderTexture.active = square;
                var image = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(square);
                image.Apply();
                File.WriteAllBytes(Path.Combine(Dir, key + ".png"), image.EncodeToPNG());
                UnityEngine.Object.Destroy(image);
                index = Names.Count;
                Names.Add(key);
                Revision++;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[RimWorldMW2] could not save picture " + key + ": " + e.Message, key.GetHashCode());
            }
            byKey[key] = index;
            return index;
        }

        /// <summary>The textures seen with each file name, first first.</summary>
        private static readonly Dictionary<string, List<int>> sameName = new Dictionary<string, List<int>>();

        /// <summary>
        /// A texture's name for its pictures: its file name, and for a different texture with a
        /// file name already seen, <c>_v</c> and how many came before it. Textures in different
        /// folders can share a file name (Combat Extended's ammo: <c>FMJ_a</c> for every
        /// calibre), and all of them showed as the first one.
        /// </summary>
        private static string Name(Texture texture)
        {
            string name = Safe(texture.name);
            if (!sameName.TryGetValue(name, out List<int> ids)) sameName[name] = ids = new List<int>();
            int id = texture.GetInstanceID();
            int index = ids.IndexOf(id);
            if (index < 0)
            {
                ids.Add(id);
                index = ids.Count - 1;
            }
            return index == 0 ? name : name + "_v" + index;
        }

        private static string Safe(string name)
        {
            var sb = new StringBuilder();
            foreach (char c in name ?? "art")
                sb.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
            return sb.Length == 0 ? "art" : sb.ToString();
        }
    }
}
