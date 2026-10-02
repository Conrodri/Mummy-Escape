using System.IO;
using MummyEscape.Visual;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace MummyEscape.EditorTools
{
    /// <summary>Paints a 1024px app icon from the procedural mummy sprite (torch-lit, on tomb darkness) and assigns it.</summary>
    public static class AppIconGenerator
    {
        const string IconPath = "Assets/_Project/Art/AppIcon.png";
        const int Size = 1024;

        [MenuItem("Mummy Escape/Generate app icon", priority = 21)]
        public static void Generate()
        {
            var art = new ArtLibrary();
            var mummy = ReadSprite(art.MummyPortrait(SkinCatalog.Get("classic")));

            var px = new Color32[Size * Size];
            var c = new Vector2(Size * 0.5f, Size * 0.47f);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    // Torch light: warm radial falloff into black.
                    float d = Vector2.Distance(new Vector2(x, y), c) / (Size * 0.62f);
                    float light = Mathf.Clamp01(1.08f - d);
                    light *= light;
                    var col = Color.Lerp(new Color(0.02f, 0.012f, 0.006f), new Color(0.85f, 0.55f, 0.22f), light);
                    // Sandstone block seams.
                    bool seam = (y % 128) < 6 || ((x + ((y / 128) % 2) * 64) % 128) < 6;
                    if (seam) col = new Color(col.r * 0.45f, col.g * 0.45f, col.b * 0.45f, 1f);
                    px[y * Size + x] = col;
                }

            // Mummy, nearest-neighbour upscale, centred.
            int scale = Mathf.Max(1, (int)(Size * 0.72f) / Mathf.Max(mummy.width, mummy.height));
            int ox = (Size - mummy.width * scale) / 2, oy = (Size - mummy.height * scale) / 2 - Size / 40;
            var src = mummy.GetPixels32();
            for (int y = 0; y < mummy.height * scale; y++)
                for (int x = 0; x < mummy.width * scale; x++)
                {
                    var s = src[(y / scale) * mummy.width + x / scale];
                    if (s.a < 8) continue;
                    int i = (oy + y) * Size + ox + x;
                    var m = Color32.Lerp(px[i], s, s.a / 255f);
                    m.a = 255;
                    px[i] = m;
                }

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.SetPixels32(px);
            tex.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(IconPath));
            File.WriteAllBytes(IconPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(mummy);
            AssetDatabase.ImportAsset(IconPath);

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            Debug.Log("[Icon] App icon written to " + IconPath);
        }

        /// <summary>The runtime sprites are GPU-only, so read them back through a render texture.</summary>
        static Texture2D ReadSprite(Sprite sprite)
        {
            var src = sprite.texture;
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var r = sprite.rect;
            var tex = new Texture2D((int)r.width, (int)r.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(r, 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }
    }
}
