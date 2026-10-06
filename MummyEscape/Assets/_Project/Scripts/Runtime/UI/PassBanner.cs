using MummyEscape.Monetization;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI
{
    /// <summary>
    /// The season pass at a glance (home and shop): the next skin to unlock, the tier and its XP bar, what the next tier
    /// gives, and a badge with the rewards waiting. A tap opens the pass.
    /// </summary>
    public sealed class PassBanner
    {
        public static readonly Color Pink = new Color32(255, 96, 220, 255);

        readonly Image _preview;
        readonly Text _season, _next, _skin, _badge;
        readonly Image _bar;
        readonly GameObject _badgeRoot;

        public RectTransform Root { get; }

        public PassBanner(Transform parent, float height, System.Action onClick)
        {
            var plate = UIKit.Plate(parent, Color.white, 30, new Color(1f, 0.45f, 0.86f, 0.55f), false, "PassBanner"); // noloc
            UIFx.Gradient(plate, new Color32(112, 44, 120, 255), new Color32(40, 20, 52, 255));
            UIKit.Size(plate, height);
            plate.raycastTarget = true;
            var btn = plate.gameObject.AddComponent<Button>();
            btn.targetGraphic = plate;
            btn.onClick.AddListener(() => { App.GameApp.I?.Audio.Play(Services.Sfx.Click, 0f); onClick?.Invoke(); });
            plate.gameObject.AddComponent<PressScale>().Amount = 0.98f;
            Root = plate.rectTransform;

            // Content in its own rect so the shine (a mask) does not clip the badge sticking out of the corner.
            var inner = UIKit.Rect("Inner", plate.transform); // noloc
            UIKit.Stretch(inner);
            UIFx.Shine(inner, 4.5f, 0.12f);
            var row = inner.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(18, 30, 12, 12);
            row.spacing = 20;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;

            var slot = UIKit.Rect("Next", inner); // noloc
            UIKit.Size(slot, height - 24, height - 10);
            UIFx.Halo(slot, new Color(1f, 0.5f, 0.9f, 0.55f), height * 1.2f, 25f);
            _preview = UIKit.Image(slot, null, Color.white, false, "Preview"); // noloc
            _preview.preserveAspect = true;
            UIKit.Stretch(_preview.rectTransform, 4, 4, 4, 4);
            UIFx.Pulse(_preview, 0.05f, 2.2f);

            var info = UIKit.Rect("Info", inner); // noloc
            UIKit.Size(info, -1, -1, 1);
            UIKit.Column(info, 4, 0, TextAnchor.MiddleLeft);
            _season = UIKit.Label(info, "", 26, new Color32(255, 200, 245, 255), TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.FitText(_season, 16);
            UIKit.Size(_season, 36);
            _bar = UIFx.Bar(info, 22, Pink);
            _next = UIKit.Label(info, "", 26, UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.FitText(_next, 16);
            UIKit.Size(_next, 36);
            _skin = UIKit.Label(info, "", 22, UIKit.Dim, TextAnchor.MiddleLeft);
            UIKit.FitText(_skin, 14);
            UIKit.Size(_skin, 32);

            var chevron = UIKit.Image(inner, UISprites.Next, new Color(1f, 0.8f, 0.95f, 0.8f), false, "Chevron"); // noloc
            UIKit.Size(chevron, 40, 40);

            var badge = UIKit.Image(plate.transform, UISprites.Circle, UIKit.Danger, false, "Badge"); // noloc
            UIKit.Place(badge.rectTransform, 1f, 1f, 54, 54, 8, 8);
            UIFx.Pulse(badge, 0.08f, 1.2f);
            _badge = UIKit.Label(badge.transform, "", 28, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Stretch(_badge.rectTransform);
            _badgeRoot = badge.gameObject;
        }

        public void Refresh(App.GameApp app)
        {
            var save = app.Save;
            var season = BattlePass.Current;
            int tier = save.PassTier, xp = save.PassXp;
            bool max = tier >= BattlePass.Tiers;
            _season.text = (Loc.T(season.Name) + "  ·  " + Loc.F("palier {0}/{1}", tier, BattlePass.Tiers)).ToUpperInvariant();
            _bar.fillAmount = max ? 1f : xp % BattlePass.XpPerTier / (float)BattlePass.XpPerTier;

            if (max) _next.text = Loc.T("Pass terminé : tout est débloqué !");
            else
            {
                int next = tier + 1;
                string free = RewardText(BattlePass.Free(next));
                _next.text = save.HasPass
                    ? Loc.F("Palier {0} : {1} + {2}", next, free, RewardText(BattlePass.Premium(season, next)))
                    : Loc.F("Palier {0} : {1}", next, free);
            }

            // The next skin is the reason to keep going: the next set piece, or the legendary before the pass is bought.
            int skinTier = NextSkinTier(tier);
            string skinId = !save.HasPass && !save.Data.OwnedSkins.Contains(season.Legendary) ? season.Legendary
                          : skinTier > 0 ? BattlePass.Premium(season, skinTier).SkinId : season.Legendary;
            var skin = SkinCatalog.Get(skinId);
            ItemPreview.Show(_preview, skin);
            if (skinId == season.Legendary && !save.HasPass)
                _skin.text = Loc.F("Légendaire « {0} » avec le pass premium", Loc.T(skin.Name));
            else if (skinTier > 0)
                _skin.text = Loc.F("Prochain skin : {0} · palier {1}", Loc.T(skin.Name), skinTier);
            else _skin.text = Loc.T("Tous les skins de la saison sont à toi");

            int claimable = save.ClaimableCount;
            _badgeRoot.SetActive(claimable > 0);
            _badge.text = claimable > 99 ? "99+" : claimable.ToString(); // noloc
        }

        /// <summary>The next tier holding a set piece (multiple of 10) after <paramref name="tier"/>, 0 when none is left.</summary>
        public static int NextSkinTier(int tier)
        {
            int next = (tier / 10 + 1) * 10;
            return next <= BattlePass.Tiers ? next : 0;
        }

        public static string RewardText(PassReward r)
        {
            switch (r.Kind)
            {
                case PassRewardKind.Skin: return Loc.T(SkinCatalog.Get(r.SkinId).Name);
                case PassRewardKind.Gold: return Loc.F("{0} scarabées dorés", r.Amount);
                case PassRewardKind.Scarabs: return Loc.F("+{0} scarabées", r.Amount);
                default: return "";
            }
        }
    }
}
