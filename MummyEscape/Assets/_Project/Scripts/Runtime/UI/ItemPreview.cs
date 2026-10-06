using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI
{
    /// <summary>
    /// The picture of one cosmetic on its own (shop, treasure, pass, wheel, Momie menu): a torch, hat or shoes alone,
    /// a colour or a mummy on a bare mummy, rather than the player's whole outfit.
    /// </summary>
    public static class ItemPreview
    {
        public static Image Create(Transform parent, SkinDef item)
        {
            var img = UIKit.Image(parent, null, Color.white);
            img.preserveAspect = true;
            Show(img, item);
            return img;
        }

        public static void Show(Image img, SkinDef item)
        {
            var art = App.GameApp.I.Art;
            // "Bare head" and "bandaged feet" have nothing to draw on their own: the bare mummy stands for them.
            bool nothing = (item.Slot == CosmeticSlot.Hat && item.Hat == HatStyle.None) || (item.Slot == CosmeticSlot.Shoes && item.Shoes == ShoeStyle.None);
            if (item.Slot == CosmeticSlot.Color || item.Slot == CosmeticSlot.Mummy || nothing)
            {
                MummyAnimator.Show(img, art, Loadout.Bare(item)); // legendary colours keep their animation
                return;
            }
            var animator = img.GetComponent<MummyAnimator>();
            if (animator != null) Object.Destroy(animator);
            img.sprite = art.ItemIcon(item);
        }
    }
}
