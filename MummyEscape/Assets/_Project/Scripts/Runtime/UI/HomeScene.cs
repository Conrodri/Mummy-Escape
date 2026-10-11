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

            // The back wall: whole faces, the bottom row standing on the horizon.
            var wall = Area("Wall", Horizon, 1f); // noloc
            for (int row = 0; row < 10; row++)
                for (int col = -3; col <= 3; col++)
                {
                    int v = Mathf.Abs(col * 7 + row * 13 + act * 5);
                    var face = Tile(wall, art.ActWall(act, v), WallTile);
                    face.rectTransform.anchorMin = face.rectTransform.anchorMax = new Vector2(0.5f, 0f);
                    face.rectTransform.pivot = new Vector2(0.5f, 0f);
                    face.rectTransform.anchoredPosition = new Vector2(col * WallTile, row * WallTile);
                }
            // Two torches on the wall, either side of the mummy.
            foreach (int side in new[] { -1, 1 })
            {
                var torch = Tile(wall, art.ActWallTorch(act), WallTile);
                Place(torch, side * WallTile * 1.5f, WallTile * 1.5f, WallTile);
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
            var img = UIKit.Image(parent, sprite, Dim, false, "Tile"); // noloc
            img.rectTransform.sizeDelta = new Vector2(size, size);
            return img;
        }

        static void Place(Image img, float x, float y, float size)
        {
            img.rectTransform.anchorMin = img.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            img.rectTransform.sizeDelta = new Vector2(size, size);
            img.rectTransform.anchoredPosition = new Vector2(x, y);
        }

        static void Shade(RectTransform area, Color top, Color bottom)
        {
            var shade = UIKit.Image(area, UIKit.Art.White, Color.white, false, "Shade"); // noloc
            UIKit.Stretch(shade.rectTransform);
            UIFx.Gradient(shade, top, bottom);
        }
    }
}
