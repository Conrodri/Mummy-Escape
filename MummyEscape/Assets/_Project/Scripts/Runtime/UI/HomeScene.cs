using System.Collections.Generic;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI
{
    /// <summary>
    /// The home screen's backdrop when an act is picked: inside that act's tomb, seen from behind the camera's shoulder.
    /// The back wall (its carved faces and two burning torches) rises over the horizon, the floor slabs come towards the
    /// viewer, growing row after row; the menu's mummy stands on them, facing us.
    /// </summary>
    public sealed class HomeScene : MonoBehaviour
    {
        /// <summary>Height of the horizon (wall foot) from the bottom of the screen, as a share of it.</summary>
        const float Horizon = 0.42f;
        const float WallTile = 216f;
        static readonly Color Dim = new Color(0.55f, 0.52f, 0.5f);
        /// <summary>Gaussian blur of the backdrop tiles (sigma, in source art pixels; a tile is 32): soft, the menu stands out.</summary>
        const float BlurRadius = 0.6f;
        /// <summary>The blurred copies are drawn at this multiple of the source size, bilinear-filtered.</summary>
        const int BlurScale = 2;
        static readonly Dictionary<Sprite, Sprite> Blurred = new Dictionary<Sprite, Sprite>();

        int _act;

        public static HomeScene Create(Transform parent)
        {
            var go = new GameObject("HomeScene", typeof(RectTransform)); // noloc
            go.transform.SetParent(parent, false);
            go.transform.SetAsFirstSibling();
            UIKit.Stretch((RectTransform)go.transform);
            return go.AddComponent<HomeScene>();
        }

        /// <summary>Builds the tomb of <paramref name="act"/> (anything already there is replaced).</summary>
        public void Show(ArtLibrary art, int act)
        {
            gameObject.SetActive(true);
            if (act == _act) return; // already built: the menu shows again
            _act = act;
            UIKit.ClearChildren(transform);

            // The back wall: whole faces, the bottom row standing on the horizon; two of them hold a burning torch, either
            // side of the mummy (in place of a face, so a torch never cuts a carved one).
            var wall = Area("Wall", Horizon, 1f); // noloc
            for (int row = 0; row < 10; row++)
                for (int col = -3; col <= 3; col++)
                {
                    int v = Mathf.Abs(col * 7 + row * 13 + act * 5);
                    bool torch = row == 1 && Mathf.Abs(col) == 1;
                    var face = Tile(wall, torch ? art.ActWallTorch(act) : art.ActWall(act, v), WallTile);
                    face.rectTransform.anchorMin = face.rectTransform.anchorMax = new Vector2(0.5f, 0f);
                    face.rectTransform.pivot = new Vector2(0.5f, 0f);
                    face.rectTransform.anchoredPosition = new Vector2(col * WallTile, row * WallTile);
                }
            // The light dies out towards the vault.
            Shade(wall, new Color(0, 0, 0, 0.85f), new Color(0, 0, 0, 0.1f));

            // The floor: rows of slabs growing as they come closer, every other one offset like laid stone.
            var floor = Area("Floor", 0f, Horizon); // noloc
            float y = 0f, size = 110f;
            for (int row = 0; y < 1400f; row++)
            {
                float offset = row % 2 == 0 ? 0f : size * 0.5f;
                int across = Mathf.CeilToInt(1400f / size) + 1;
                for (int k = -across / 2 - 1; k <= across / 2 + 1; k++)
                {
                    var slab = Tile(floor, art.ActFloor(act, Mathf.Abs(k * 11 + row * 17 + act)), size);
                    slab.rectTransform.anchorMin = slab.rectTransform.anchorMax = new Vector2(0.5f, 1f);
                    slab.rectTransform.pivot = new Vector2(0.5f, 1f);
                    slab.rectTransform.anchoredPosition = new Vector2(k * size + offset, -y);
                }
                y += size;
                size *= 1.38f;
            }
            // Darker at the wall's foot.
            Shade(floor, new Color(0, 0, 0, 0.7f), new Color(0, 0, 0, 0.15f));
        }

        public void Hide() => gameObject.SetActive(false);

        RectTransform Area(string name, float from, float to)
        {
            var rt = UIKit.Rect(name, transform);
            rt.anchorMin = new Vector2(0f, from);
            rt.anchorMax = new Vector2(1f, to);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        static Image Tile(Transform parent, Sprite sprite, float size)
        {
            var img = UIKit.Image(parent, Blur(sprite), Dim, false, "Tile"); // noloc
            img.rectTransform.sizeDelta = new Vector2(size, size);
            return img;
        }

        /// <summary>
        /// A blurred copy of a tile sprite, made once. Sampling wraps around the tile, so tiles laid edge to edge stay
        /// seamless. Imported tiles are not CPU-readable: those go through a GPU copy first.
        /// </summary>
        static Sprite Blur(Sprite sprite)
        {
            if (sprite == null) return null;
            if (Blurred.TryGetValue(sprite, out var done)) return done;
            var r = sprite.textureRect;
            int sw = (int)r.width, sh = (int)r.height, w = sw * BlurScale, h = sh * BlurScale;
            var src = Read(sprite.texture, r);

            var a = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    a[y * w + x] = src[(y / BlurScale) * sw + x / BlurScale];
            var k = Kernel(BlurRadius * BlurScale);
            var b = new Color[a.Length];
            Pass(a, b, w, h, k, 1, 0);
            Pass(b, a, w, h, k, 0, 1);

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
            tex.SetPixels(a);
            tex.Apply(false, true);
            return Blurred[sprite] = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), sprite.pixelsPerUnit * BlurScale, 0, SpriteMeshType.FullRect);
        }

        /// <summary>The sprite's pixels; a non-readable (imported) texture is copied through the GPU.</summary>
        static Color[] Read(Texture2D tex, Rect r)
        {
            int w = (int)r.width, h = (int)r.height;
            if (tex.isReadable) return tex.GetPixels((int)r.x, (int)r.y, w, h);
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB); // sRGB both ways: the colours come back as imported
            var keep = RenderTexture.active;
            Graphics.Blit(tex, rt, new Vector2(r.width / tex.width, r.height / tex.height), new Vector2(r.x / tex.width, r.y / tex.height));
            RenderTexture.active = rt;
            var copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
            RenderTexture.active = keep;
            RenderTexture.ReleaseTemporary(rt);
            var px = copy.GetPixels();
            Destroy(copy);
            return px;
        }

        static float[] Kernel(float sigma)
        {
            int n = Mathf.CeilToInt(sigma * 2.5f);
            var k = new float[2 * n + 1];
            float sum = 0f;
            for (int i = -n; i <= n; i++) sum += k[i + n] = Mathf.Exp(-i * i / (2f * sigma * sigma));
            for (int i = 0; i < k.Length; i++) k[i] /= sum;
            return k;
        }

        /// <summary>One direction of the separable blur (dx, dy = 1, 0 or 0, 1), wrapping around the tile.</summary>
        static void Pass(Color[] from, Color[] to, int w, int h, float[] k, int dx, int dy)
        {
            int n = k.Length / 2;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = new Color(0, 0, 0, 0);
                    for (int i = -n; i <= n; i++)
                    {
                        int sx = ((x + i * dx) % w + w) % w, sy = ((y + i * dy) % h + h) % h;
                        c += from[sy * w + sx] * k[i + n];
                    }
                    to[y * w + x] = c;
                }
        }

        static void Shade(RectTransform area, Color top, Color bottom)
        {
            var shade = UIKit.Image(area, UIKit.Art.White, Color.white, false, "Shade"); // noloc
            UIKit.Stretch(shade.rectTransform);
            UIFx.Gradient(shade, top, bottom);
        }
    }
}
