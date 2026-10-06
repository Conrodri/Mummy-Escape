using System.Collections.Generic;
using MummyEscape.Monetization;
using MummyEscape.Services;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// The season pass: progress (XP earned by playing), the paid pass and the tiers bought 10 at a time, and the 100
    /// tiers with their free reward (left) and paid reward (right). Paid players collect both.
    /// </summary>
    public sealed class PassScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Shop;
        /// <summary>A wallet call is on its way (claims and purchases wait for it).</summary>
        bool _busy;

        static readonly Color PremiumColor = new Color32(255, 96, 220, 255);
        const float RowHeight = 150;

        Text _season, _tier, _xp, _gold, _note, _perks;
        Image _bar, _legendary;
        Button _buy, _tiers, _claimAll;
        RectTransform _list;
        ScrollRect _scroll;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("Pass de saison");
            var body = Body(190, 40, 40);
            UIKit.Column(body, 16);

            // Season, tier and XP bar, next to the season legendary worn by the player's mummy.
            var top = UIKit.Panel(body, "Season"); // noloc
            top.raycastTarget = false;
            UIKit.Size(top, 250);
            var row = top.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(24, 30, 16, 16);
            row.spacing = 22;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            _legendary = UIKit.Image(top.transform, null, Color.white);
            _legendary.preserveAspect = true;
            UIKit.Size(_legendary, 210, 180);
            _legendary.raycastTarget = true;
            _legendary.gameObject.AddComponent<Button>().onClick.AddListener(ZoomLegendary);
            _legendary.gameObject.AddComponent<PressScale>();
            var info = UIKit.Rect("Info", top.transform); // noloc
            UIKit.Size(info, -1, -1, 1);
            UIKit.Column(info, 6, 0, TextAnchor.MiddleLeft);
            _season = UIKit.Label(info, "", 34, UIKit.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.FitText(_season, 20);
            UIKit.Size(_season, 46);
            _tier = UIKit.Title(info, "", 50, UIKit.Sand, TextAnchor.MiddleLeft);
            UIKit.FitText(_tier, 28);
            UIKit.Size(_tier, 62);
            var track = UIKit.Plate(info, new Color(0, 0, 0, 0.5f), 14, UIKit.Rim, false, "Bar"); // noloc
            UIKit.Size(track, 28);
            _bar = UIKit.Image(track.transform, UIKit.Art.White, UIKit.Gold, false, "Fill"); // noloc
            UIKit.Stretch(_bar.rectTransform, 4, 4, 4, 4);
            _bar.type = Image.Type.Filled;
            _bar.fillMethod = Image.FillMethod.Horizontal;
            var line = UIKit.Row(info, 48, 12);
            line.childAlignment = TextAnchor.MiddleLeft;
            _xp = UIKit.Label(line.transform, "", 24, UIKit.Dim, TextAnchor.MiddleLeft);
            UIKit.FitText(_xp, 16);
            UIKit.Size(_xp, -1, -1, 1);
            _gold = UIKit.Chip(line.transform, UIKit.Art.GoldScarab, "", TreasureScreen.GoldColor, 48);

            _perks = UIKit.Label(body, "", 24, UIKit.Sand);
            UIKit.FitText(_perks, 16);
            UIKit.Size(_perks, 70);

            var buttons = UIKit.Row(body, 96, 14);
            _buy = UIKit.Button(buttons.transform, "Pass premium", BuyPass, 28, ButtonStyle.Primary);
            UIKit.FitText(_buy.GetComponentInChildren<Text>(), 16);
            UIKit.Size(_buy, -1, -1, 1.3f);
            _tiers = UIKit.Button(buttons.transform, "Paliers", BuyTiers, 28);
            UIKit.FitText(_tiers.GetComponentInChildren<Text>(), 16);
            UIKit.Size(_tiers, -1, -1, 1);
            _claimAll = UIKit.Button(buttons.transform, "Tout récupérer", ClaimAll, 28);
            UIKit.FitText(_claimAll.GetComponentInChildren<Text>(), 16);
            UIKit.Size(_claimAll, -1, -1, 1);

            _note = UIKit.Label(body, "", 26, UIKit.Sand);
            UIKit.FitText(_note, 18);
            UIKit.Size(_note, 40);

            // Column titles over the two tracks.
            var heads = UIKit.Row(body, 40, 14);
            UIKit.Size(UIKit.Rect("Tier", heads.transform), -1, 96, 0); // noloc
            UIKit.Size(UIKit.Label(heads.transform, "Gratuit", 26, UIKit.Sand, TextAnchor.MiddleCenter, FontStyle.Bold), -1, -1, 1);
            UIKit.Size(UIKit.Label(heads.transform, "Premium", 26, PremiumColor, TextAnchor.MiddleCenter, FontStyle.Bold), -1, -1, 1);

            _list = UIKit.Scroll(body, out _scroll);
            UIKit.Size(_scroll, -1, -1, -1, 1);
            _list.GetComponent<VerticalLayoutGroup>().spacing = 10;
        }

        public override void OnShow()
        {
            _note.text = "";
            Refresh();
            // Opens on the current tier.
            Canvas.ForceUpdateCanvases();
            int tier = Mathf.Clamp(App.Save.PassTier, 1, BattlePass.Tiers);
            _scroll.verticalNormalizedPosition = 1f - (tier - 1) / (float)(BattlePass.Tiers - 1);
            SyncWallet();
        }

        /// <summary>The pass and its claims as the server has them (another phone may have bought or collected).</summary>
        async void SyncWallet()
        {
            await GoldWallet.RefreshAsync(App);
            if (this != null && gameObject.activeInHierarchy) Refresh();
        }

        void Refresh()
        {
            var save = App.Save;
            var season = BattlePass.Current;
            int xp = save.PassXp, tier = save.PassTier;
            bool max = tier >= BattlePass.Tiers;
            var end = BattlePass.EndOf(season);
            _season.text = Loc.T(season.Name) + (end.HasValue ? "  ·  " + Loc.F("fin le {0}", end.Value.ToLocalTime().ToString("dd/MM")) : ""); // noloc
            _tier.text = Loc.F("Palier {0} / {1}", tier, BattlePass.Tiers);
            _bar.fillAmount = max ? 1f : xp % BattlePass.XpPerTier / (float)BattlePass.XpPerTier;
            _xp.text = max ? Loc.T("Pass terminé !") : Loc.F("{0} / {1} XP vers le palier {2}", xp % BattlePass.XpPerTier, BattlePass.XpPerTier, tier + 1);
            _gold.text = save.Gold.ToString();
            ItemPreview.Show(_legendary, SkinCatalog.Get(season.Legendary));
            _perks.text = save.HasPass
                ? Loc.T("Pass premium actif : parties illimitées, et toutes les récompenses premium à récupérer.")
                : Loc.F("Premium : le skin légendaire « {0} » tout de suite, un set de 10 pièces (une tous les 10 paliers), {1} scarabées dorés (de quoi prendre le pass suivant) et des parties illimitées.",
                        Loc.T(SkinCatalog.Get(season.Legendary).Name), BattlePass.PremiumGoldTotal)
                  + "\n" + Loc.F("XP : partie solo {0} à {1}, duel ou 2v2 {2} ({3} en cas de victoire).",
                                 BattlePass.SoloXp(false, 0), BattlePass.SoloXp(true, 3), BattlePass.MatchXp, BattlePass.MatchXp + BattlePass.WinBonusXp);

            UIKit.SetLabel(_buy, save.HasPass ? Loc.T("Pass premium actif") : Loc.F("Pass premium · {0} dorés", GoldShop.PassPrice));
            _buy.interactable = !save.HasPass;
            UIKit.SetLabel(_tiers, Loc.F("+{0} paliers · {1} dorés", GoldShop.TierBundleSize, GoldShop.TierBundlePrice));
            _tiers.interactable = !max;
            int claimable = save.ClaimableCount;
            UIKit.SetLabel(_claimAll, claimable > 0 ? Loc.F("Tout récupérer ({0})", claimable) : Loc.T("Tout récupérer"));
            _claimAll.interactable = claimable > 0;

            UIKit.ClearChildren(_list);
            for (int t = 1; t <= BattlePass.Tiers; t++) TierRow(season, t, tier);
        }

        void TierRow(PassSeason season, int t, int reached)
        {
            var row = UIKit.Row(_list, RowHeight, 14);
            var badge = UIKit.Image(row.transform, UISprites.Circle, t <= reached ? UIKit.Gold : UIKit.SurfaceHi, false, "Tier"); // noloc
            UIKit.Size(badge, 96, 96, 0);
            var number = UIKit.Title(badge.transform, t.ToString(), 40, t <= reached ? UIKit.Ink : UIKit.Dim);
            UIKit.Stretch(number.rectTransform);
            Cell(row.transform, t, false, BattlePass.Free(t), reached);
            Cell(row.transform, t, true, BattlePass.Premium(season, t), reached);
        }

        void Cell(Transform parent, int t, bool premium, PassReward reward, int reached)
        {
            var save = App.Save;
            bool claimed = save.IsClaimed(t, premium);
            bool locked = premium && !save.HasPass;
            bool can = save.CanClaim(t, premium);
            Color fill = claimed ? ShopScreen.WornFill : premium ? new Color32(64, 34, 60, 255) : UIKit.SurfaceHi;
            var cell = UIKit.Plate(parent, fill, 22, can ? (Color?)UIKit.Gold : premium ? new Color(1f, 0.38f, 0.86f, 0.35f) : UIKit.Rim, false, premium ? "Premium" : "Free"); // noloc
            UIKit.Size(cell, -1, 0, 1); // equal halves whatever the text
            var h = cell.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(14, 14, 10, 10);
            h.spacing = 12;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;

            var icon = UIKit.Image(cell.transform, null, Color.white);
            icon.preserveAspect = true;
            UIKit.Size(icon, 110, 100, 0);
            string label;
            if (reward.Kind == PassRewardKind.Skin)
            {
                var def = SkinCatalog.Get(reward.SkinId);
                ItemPreview.Show(icon, def);
                label = Loc.T(def.Name);
                // The skin cell opens it up close (the OK button still claims it straight away).
                int tier = t;
                cell.raycastTarget = true;
                var zoom = cell.gameObject.AddComponent<Button>();
                zoom.targetGraphic = cell;
                zoom.onClick.AddListener(() => ZoomTier(def, tier, premium));
                cell.gameObject.AddComponent<PressScale>().Amount = 0.98f;
            }
            else
            {
                icon.sprite = reward.Kind == PassRewardKind.Gold ? UIKit.Art.GoldScarab : UIKit.Art.Scarab;
                label = reward.Kind == PassRewardKind.Gold ? Loc.F("{0} dorés", reward.Amount) : "+" + reward.Amount;
            }
            if (locked || t > reached) icon.color *= new Color(0.55f, 0.5f, 0.5f, 1f);
            var text = UIKit.Label(cell.transform, "", reward.Kind == PassRewardKind.Skin ? 22 : 32,
                                   reward.Kind == PassRewardKind.Skin ? PremiumColor : UIKit.Sand, TextAnchor.MiddleLeft, FontStyle.Bold);
            text.text = label;
            UIKit.FitText(text, 14);
            UIKit.Size(text, -1, -1, 1);

            if (claimed)
            {
                var check = UIKit.Image(cell.transform, UISprites.Check, UIKit.Success);
                UIKit.Size(check, 50, 50, 0);
            }
            else if (locked)
            {
                var padlock = UIKit.Image(cell.transform, UIKit.Art.Lock, Color.white);
                padlock.preserveAspect = true;
                UIKit.Size(padlock, 50, 50, 0);
            }
            else if (can)
            {
                int tier = t;
                var btn = UIKit.Button(cell.transform, "OK", () => Claim(tier, premium), 26, ButtonStyle.Primary); // noloc
                UIKit.Size(btn, 80, 96, 0);
            }
        }

        /// <summary>A skin of the pass up close: worn if owned, claimed if its tier is reached, otherwise what it takes.</summary>
        void ZoomTier(SkinDef def, int tier, bool premium)
        {
            var save = App.Save;
            bool owned = save.Data.OwnedSkins.Contains(def.Id);
            bool worn = save.IsWorn(def.Id);
            string action; System.Action onAction = null;
            if (worn) action = Loc.T("Équipé");
            else if (owned) { action = Loc.T("Équiper"); onAction = () => { save.SelectSkin(def.Id); Refresh(); }; }
            else if (save.CanClaim(tier, premium)) { action = Loc.T("Récupérer"); onAction = () => Claim(tier, premium); }
            else if (premium && !save.HasPass) { action = Loc.F("Pass premium · {0} dorés", GoldShop.PassPrice); onAction = BuyPass; }
            else action = Loc.F("Palier {0}", tier);
            Router.Open<ItemZoomScreen>().Show(def, premium ? Loc.T("Pass premium") : Loc.T("Pass de saison"), PremiumColor,
                Loc.F("Récompense du palier {0} du {1}.", tier, Loc.T(BattlePass.Current.Name)), action, onAction);
        }

        /// <summary>The season legendary up close: given with the premium pass.</summary>
        void ZoomLegendary()
        {
            var save = App.Save;
            var def = SkinCatalog.Get(BattlePass.Current.Legendary);
            bool owned = save.Data.OwnedSkins.Contains(def.Id);
            bool worn = save.IsWorn(def.Id);
            string action = worn ? Loc.T("Équipé") : owned ? Loc.T("Équiper") : Loc.F("Pass premium · {0} dorés", GoldShop.PassPrice);
            System.Action onAction = worn ? null : owned ? () => { save.SelectSkin(def.Id); Refresh(); } : (System.Action)BuyPass;
            Router.Open<ItemZoomScreen>().Show(def, Loc.T("Légendaire"), CasinoScreen.LegendaryColor,
                Loc.F("Le légendaire du {0} : offert dès l'achat du Pass premium.", Loc.T(BattlePass.Current.Name)), action, onAction);
        }

        void Claim(int tier, bool premium) => _ = ClaimAsync(new List<(int, bool)> { (tier, premium) });

        void ClaimAll()
        {
            var picks = new List<(int, bool)>();
            for (int tier = 1; tier <= App.Save.PassTier; tier++)
                foreach (bool premium in new[] { false, true })
                    if (App.Save.CanClaim(tier, premium)) picks.Add((tier, premium));
            if (picks.Count > 0) _ = ClaimAsync(picks);
        }

        /// <summary>Scarab rewards are collected here; golden scarabs and skins come from the server's wallet.</summary>
        async System.Threading.Tasks.Task ClaimAsync(List<(int tier, bool premium)> picks)
        {
            if (_busy) return;
            var rewards = new List<PassReward>();
            var free = new List<int>();
            var paid = new List<int>();
            foreach (var (tier, premium) in picks)
            {
                if (!App.Save.CanClaim(tier, premium)) continue;
                var reward = SaveService.RewardOf(tier, premium);
                if (SaveService.FromServer(reward)) (premium ? paid : free).Add(tier);
                else rewards.Add(App.Save.Claim(tier, premium));
            }
            string error = null;
            if (free.Count + paid.Count > 0)
            {
                _busy = true;
                error = await GoldWallet.RunAsync(App, p => p.ClaimPassRewardsAsync(BattlePass.Current.Id, free, paid));
                _busy = false;
                if (this == null) return;
                if (error == null)
                {
                    foreach (int t in free) rewards.Add(SaveService.RewardOf(t, false));
                    foreach (int t in paid) rewards.Add(SaveService.RewardOf(t, true));
                }
            }
            if (rewards.Count > 0) Collected(rewards);
            if (error != null) _note.text = GoldWallet.ErrorText(error);
            Refresh();
        }

        void Collected(List<PassReward> rewards)
        {
            int scarabs = 0, gold = 0;
            var skins = new List<string>();
            foreach (var r in rewards)
            {
                if (r.Kind == PassRewardKind.Scarabs) scarabs += r.Amount;
                else if (r.Kind == PassRewardKind.Gold) gold += r.Amount;
                else if (r.Kind == PassRewardKind.Skin) skins.Add(Loc.T(SkinCatalog.Get(r.SkinId).Name));
            }
            var parts = new List<string>();
            if (scarabs > 0) parts.Add(Loc.F("+{0} scarabées", scarabs));
            if (gold > 0) parts.Add(Loc.F("+{0} scarabées dorés", gold));
            if (skins.Count > 0) parts.Add(string.Join(", ", skins));
            _note.text = string.Join(" · ", parts);
            App.Audio.Play(skins.Count > 0 ? Sfx.Win : Sfx.Coin);
            Refresh();
        }

        void BuyPass()
        {
            var save = App.Save;
            if (save.HasPass || !TreasureScreen.EnoughGold(App, Router, GoldShop.PassPrice, () => Router.Open<TreasureScreen>())) return;
            var season = BattlePass.Current;
            Router.Open<OfferDialog>().Configure("Pass premium",
                Loc.F("Débloquer le pass premium de « {0} » pour {1} scarabées dorés ?", Loc.T(season.Name), GoldShop.PassPrice),
                (Loc.F("Débloquer · {0} dorés", GoldShop.PassPrice), ButtonStyle.Primary, async () =>
                {
                    if (_busy) return;
                    _busy = true;
                    string error = await GoldWallet.RunAsync(App, p => p.BuyPassAsync());
                    _busy = false;
                    if (this == null) return;
                    if (error != null) { _note.text = GoldWallet.ErrorText(error); Refresh(); return; }
                    save.SelectSkin(season.Legendary);
                    App.Audio.Play(Sfx.Win);
                    _note.text = Loc.F("Pass premium débloqué : {0} est à toi !", Loc.T(SkinCatalog.Get(season.Legendary).Name));
                    Refresh();
                }),
                ("Annuler", ButtonStyle.Ghost, null));
        }

        void BuyTiers()
        {
            var save = App.Save;
            if (!TreasureScreen.EnoughGold(App, Router, GoldShop.TierBundlePrice, () => Router.Open<TreasureScreen>())) return;
            Router.Open<OfferDialog>().Configure("Paliers",
                Loc.F("Avancer de {0} paliers pour {1} scarabées dorés ?", GoldShop.TierBundleSize, GoldShop.TierBundlePrice),
                (Loc.F("Avancer · {0} dorés", GoldShop.TierBundlePrice), ButtonStyle.Primary, async () =>
                {
                    if (_busy || save.PassTier >= BattlePass.Tiers) return;
                    _busy = true;
                    string error = await GoldWallet.RunAsync(App, p => p.BuyTiersAsync());
                    _busy = false;
                    if (this == null) return;
                    if (error != null) { _note.text = GoldWallet.ErrorText(error); return; }
                    save.SkipTiers();
                    App.Audio.Play(Sfx.Coin);
                    OnShow();
                    _note.text = Loc.F("Palier {0} atteint !", save.PassTier);
                }),
                ("Annuler", ButtonStyle.Ghost, null));
        }
    }
}
