using System.Collections.Generic;
using MummyEscape.Core;
using MummyEscape.Pvp;
using MummyEscape.Services;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// Cosmetics bought with scarabs (earned by collecting new stars): one tab per slot (mummy, colour, torch, hat,
    /// shoes) plus the act collections, sold whole for less, and the duel tab where Maât's seals (won in duels) buy
    /// the league skins. Every card previews the item on the current outfit. IAP can be plugged in later.
    /// </summary>
    public sealed class ShopScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Shop;

        const int Columns = 4;
        const float CardHeight = 340;

        static readonly string[] Tabs = { "Thèmes", "Momies", "Couleurs", "Torches", "Chapeaux", "Pieds", "Duel", "Casino" };
        static readonly CosmeticSlot[] TabSlots = { CosmeticSlot.Mummy, CosmeticSlot.Mummy, CosmeticSlot.Color, CosmeticSlot.Torch, CosmeticSlot.Hat, CosmeticSlot.Shoes, CosmeticSlot.Mummy, CosmeticSlot.Color };
        const int DuelTab = 6;
        const int CasinoTab = 7;
        const float WheelSize = 540;
        const float SpinSeconds = 4f;
        static readonly Color LegendaryColor = new Color32(255, 96, 220, 255);

        internal static readonly Color WornFill = new Color32(30, 92, 84, 255);
        internal static readonly Color WornRim = new Color(0.25f, 0.88f, 0.8f, 0.6f);

        Text _coins, _stars, _seals, _note;
        Image _outfit;
        UIKit.Segmented _tabs;
        RectTransform _list;
        ScrollRect _scroll;
        int _tab = 1;
        bool _busy;
        int _request;
        readonly System.Random _rng = new System.Random();
        /// <summary>A wheel is turning: no other spin until it stops.</summary>
        bool _spinning;
        /// <summary>Last outcome of each wheel, kept across rebuilds of the tab.</summary>
        readonly Dictionary<string, string> _wheelNote = new Dictionary<string, string>();
        /// <summary>Where each wheel stopped, so a rebuilt tab shows it still on its last prize.</summary>
        readonly Dictionary<string, float> _wheelAngle = new Dictionary<string, float>();

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("Boutique");
            var body = Body(190, 40, 40);
            UIKit.Column(body, 20);

            // The current outfit, the wallet and how to fill it.
            var top = UIKit.Panel(body, "Outfit"); // noloc
            top.raycastTarget = false;
            UIKit.Size(top, 230);
            var row = top.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(24, 30, 15, 15);
            row.spacing = 24;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            _outfit = UIKit.Image(top.transform, null, Color.white);
            _outfit.preserveAspect = true;
            UIKit.Size(_outfit, 200, 170);
            var side = UIKit.Rect("Side", top.transform);
            UIKit.Size(side, 200, 0, 1);
            UIKit.Column(side, 10, 0, TextAnchor.MiddleLeft);
            // Stars unlock the act collections, scarabs pay for them.
            var wallet = UIKit.Row(side, 72, 14);
            wallet.childAlignment = TextAnchor.MiddleLeft;
            _stars = UIKit.Chip(wallet.transform, UIKit.Art.Star, "", null, 72);
            _coins = UIKit.Chip(wallet.transform, UIKit.Art.Scarab, "", UIKit.Gold, 72);
            _seals = UIKit.Chip(wallet.transform, UISprites.Seal, "", UIKit.Turquoise, 72);
            var hint = UIKit.Label(side, "Les étoiles débloquent les collections des actes, les scarabées les paient.", 26, UIKit.Dim, TextAnchor.MiddleLeft);
            UIKit.FitText(hint, 18);
            UIKit.Size(hint, 80);

            _tabs = new UIKit.Segmented(body, Tabs, i => { _tab = i; Refresh(); _scroll.verticalNormalizedPosition = 1f; }, 80);
            _list = UIKit.Scroll(body, out _scroll);
            UIKit.Size(_scroll, -1, -1, -1, 1);
            _list.GetComponent<VerticalLayoutGroup>().spacing = 16;
        }

        public override void OnShow()
        {
            _busy = false;
            _spinning = false; // a spin cut short by leaving the screen was already paid and cashed in
            _tabs.Select(_tab);
            Refresh();
            _scroll.verticalNormalizedPosition = 1f;
            LoadSeals();
        }

        /// <summary>The seals and duel rewards live on the server: fetched once if the duel home was never opened.</summary>
        async void LoadSeals()
        {
            if (App.Pvp == null || App.PvpProfile != null) return;
            int request = ++_request;
            await App.RefreshPvpProfile();
            if (request == _request && this != null && isActiveAndEnabled) Refresh();
        }

        void Refresh()
        {
            var save = App.Save;
            _coins.text = save.Data.Coins.ToString();
            _stars.text = save.TotalStars.ToString();
            var pvp = App.PvpProfile?.Data;
            _seals.transform.parent.gameObject.SetActive(App.Pvp != null);
            _seals.text = (pvp?.Seals ?? 0).ToString();
            MummyAnimator.Show(_outfit, App.Art, save.Loadout);
            UIKit.ClearChildren(_list);
            _note = null;
            if (_tab == 0) { FillSets(); return; }
            if (_tab == DuelTab) { FillDuel(pvp); return; }
            if (_tab == CasinoTab) { FillCasino(pvp); return; }

            // One continuous grid, classics first then act after act (each card wears its act's tag):
            // per-act sections would leave a single card per row.
            var items = new List<SkinDef>(SkinCatalog.InSlot(TabSlots[_tab]));
            items.Sort((a, b) => a.Theme != b.Theme ? a.Theme.CompareTo(b.Theme) : a.Price.CompareTo(b.Price));
            // Duel rewards the player owns come last, ready to wear (they are never sold here).
            items.AddRange(PvpSkins.Owned(save.Data.OwnedSkins, TabSlots[_tab]));
            // So do the casino's legendaries.
            if (TabSlots[_tab] == CosmeticSlot.Color) items.AddRange(LegendarySkins.OwnedScarabLegendaries(save.Data.OwnedSkins));
            for (int i = 0; i < items.Count; i += Columns)
            {
                var cards = UIKit.Row(_list, CardHeight, 14);
                for (int k = 0; k < Columns; k++)
                {
                    // Every column gets the same share of the width, whatever the names in it.
                    if (i + k < items.Count) ItemCard(cards.transform, items[i + k]);
                    else UIKit.Size(UIKit.Rect("Spacer", cards.transform), -1, 0, 1);
                }
            }
        }

        static string ThemeName(int act) => Loc.F("Acte {0} — {1}", act, Loc.T(DifficultyTable.GetAct(act).Name));

        static Color ThemeColor(int act) => Color.Lerp(TombTheme.ForAct(act).Accent, UIKit.Sand, 0.25f);

        void ItemCard(Transform parent, SkinDef item)
        {
            var save = App.Save;
            bool owned = save.Data.OwnedSkins.Contains(item.Id);
            bool worn = save.IsWorn(item.Id);
            bool locked = !owned && save.TotalStars < item.MinStars;
            bool affordable = !locked && save.Data.Coins >= item.Price;

            var card = UIKit.Plate(parent, worn ? WornFill : UIKit.SurfaceHi, 28, worn ? WornRim : UIKit.Rim, false, item.Id);
            UIKit.Size(card, -1, 0, 1);
            UIKit.Column(card.transform, 2, 12, TextAnchor.MiddleCenter);
            var tag = UIKit.Label(card.transform, item.Legendary ? Loc.T("Légendaire").ToUpperInvariant()
                                  : item.Pvp ? Loc.T("Duel").ToUpperInvariant() : item.Theme == 0 ? Loc.T("Classique") : Loc.F("Acte {0}", item.Theme).ToUpperInvariant(), 20,
                                  item.Legendary ? LegendaryColor : item.Pvp ? UIKit.Turquoise : item.Theme == 0 ? UIKit.Dim : ThemeColor(item.Theme), TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.FitText(tag, 14);
            UIKit.Size(tag, 28, 0);
            var preview = Portrait(card.transform, save.Loadout.With(item));
            UIKit.Size(preview, 150, 0);
            if (locked) Lock(preview, 64);
            var name = UIKit.Label(card.transform, item.Name, 22, UIKit.Sand, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.FitText(name, 15);
            UIKit.Size(name, 58, 0);

            string label = worn ? Loc.T("Équipé") : owned ? Loc.T("Équiper")
                         : locked ? Loc.F("{0} étoiles requises", item.MinStars) : Loc.F("{0} scarabées", item.Price);
            var btn = UIKit.Button(card.transform, label, () => OnItem(item), 22,
                                   !worn && (owned || affordable) ? ButtonStyle.Primary : ButtonStyle.Secondary);
            btn.interactable = !worn && (owned || affordable);
            UIKit.FitText(btn.GetComponentInChildren<Text>(), 16);
            UIKit.Size(btn, 66, 0);
        }

        /// <summary>Greys out a preview and puts a padlock over it (not enough stars yet).</summary>
        static void Lock(Image preview, float size)
        {
            preview.color = new Color(0.35f, 0.32f, 0.3f, 1f);
            var padlock = UIKit.Image(preview.transform, UIKit.Art.Lock, Color.white, false, "Lock"); // noloc
            padlock.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            UIKit.Place(padlock.rectTransform, 0.5f, 0.5f, size, size);
        }

        void OnItem(SkinDef item)
        {
            var save = App.Save;
            if (!save.Data.OwnedSkins.Contains(item.Id))
            {
                if (!save.TryBuySkin(item.Id, item.Price)) return;
                App.Audio.Play(Sfx.Coin);
            }
            save.SelectSkin(item.Id);
            Refresh();
        }

        /// <summary>One wide card per act: the whole collection worn, bought at once for less, or put on in one tap.</summary>
        void FillSets()
        {
            var save = App.Save;
            var intro = UIKit.Label(_list, "Chaque acte a sa collection : momie, torche, chapeau et chaussures, débloquée par tes étoiles (25 par acte). L'ensemble complet coûte 20 % de moins.", 26, UIKit.Dim);
            UIKit.FitText(intro, 18);
            UIKit.Size(intro, 84);
            for (int act = 1; act <= DifficultyTable.ActCount; act++)
            {
                var set = SkinCatalog.ThemeSet(act);
                var look = save.Loadout;
                int missingPrice = 0;
                bool worn = true;
                foreach (var s in set)
                {
                    look = look.With(s);
                    if (!save.Data.OwnedSkins.Contains(s.Id)) missingPrice += s.Price;
                    if (!save.IsWorn(s.Id)) worn = false;
                }
                int price = SkinCatalog.SetPrice(missingPrice);
                int minStars = act * SkinCatalog.StarsPerTheme;
                bool locked = missingPrice > 0 && save.TotalStars < minStars;
                bool affordable = !locked && save.Data.Coins >= price;

                var card = UIKit.Plate(_list, worn ? WornFill : UIKit.SurfaceHi, 28, worn ? WornRim : UIKit.Rim, false, "Set" + act); // noloc
                UIKit.Size(card, 280);
                var row = card.gameObject.AddComponent<HorizontalLayoutGroup>();
                row.padding = new RectOffset(20, 26, 18, 18);
                row.spacing = 20;
                row.childAlignment = TextAnchor.MiddleLeft;
                row.childControlWidth = row.childControlHeight = true;
                row.childForceExpandWidth = row.childForceExpandHeight = false;

                var preview = Portrait(card.transform, look);
                UIKit.Size(preview, 230, 190);
                if (locked) Lock(preview, 80);
                var info = UIKit.Rect("Info", card.transform);
                UIKit.Size(info, -1, 0, 1);
                UIKit.Column(info, 6, 0, TextAnchor.MiddleLeft);

                var title = UIKit.Label(info, ThemeName(act), 30, ThemeColor(act), TextAnchor.MiddleLeft, FontStyle.Bold);
                UIKit.FitText(title, 18);
                UIKit.Size(title, 48);
                var names = new List<string>();
                foreach (var s in set) names.Add(Loc.T(s.Name));
                var pieces = UIKit.Label(info, string.Join(" · ", names), 22, UIKit.Dim, TextAnchor.MiddleLeft);
                UIKit.FitText(pieces, 16);
                UIKit.Size(pieces, 76);

                int a = act;
                string label = worn ? Loc.T("Équipé") : missingPrice == 0 ? Loc.T("Tout équiper")
                             : locked ? Loc.F("Débloqué à {0} étoiles", minStars) : Loc.F("Ensemble : {0} scarabées", price);
                bool usable = !worn && (missingPrice == 0 || affordable);
                var btn = UIKit.Button(info, label, () => OnSet(a, price), 26, usable ? ButtonStyle.Primary : ButtonStyle.Secondary);
                btn.interactable = usable;
                UIKit.FitText(btn.GetComponentInChildren<Text>(), 16);
                UIKit.Size(btn, 76);
            }
        }

        void OnSet(int act, int price)
        {
            var save = App.Save;
            var set = SkinCatalog.ThemeSet(act);
            bool missing = false;
            foreach (var s in set) if (!save.Data.OwnedSkins.Contains(s.Id)) missing = true;
            if (missing)
            {
                if (!save.TryBuyAll(set, price)) return;
                App.Audio.Play(Sfx.Coin);
            }
            foreach (var s in set) save.SelectSkin(s.Id);
            Refresh();
        }

        /// <summary>
        /// Maât's seal shop: skins bought with seals (first win of the day, daily chest), each from a league up.
        /// The purchase goes through the server, which holds the seals.
        /// </summary>
        void FillDuel(PlayerPvpData d)
        {
            var intro = UIKit.Label(_list, "Les sceaux de Maât se gagnent en duel (1re victoire du jour, coffre quotidien). Chaque article demande d'avoir atteint sa ligue.", 26, UIKit.Dim);
            UIKit.FitText(intro, 18);
            UIKit.Size(intro, 84);
            _note = UIKit.Label(_list, "", 26, UIKit.Sand);
            UIKit.FitText(_note, 18);
            UIKit.Size(_note, 44);
            if (App.Pvp == null) _note.text = Loc.T("Les duels se jouent en ligne : active le mode en ligne (Paramètres › Confidentialité).");
            else if (d == null) _note.text = Loc.T("Chargement…");

            var items = new List<(SealItem item, SkinDef def)>();
            foreach (var item in SealShop.Items)
            {
                var def = PvpSkins.ShopItem(item.Id);
                if (def != null) items.Add((item, def));
            }
            for (int i = 0; i < items.Count; i += Columns)
            {
                var cards = UIKit.Row(_list, CardHeight, 14);
                for (int k = 0; k < Columns; k++)
                {
                    if (i + k < items.Count) SealCard(cards.transform, items[i + k].item, items[i + k].def, d);
                    else UIKit.Size(UIKit.Rect("Spacer", cards.transform), -1, 0, 1);
                }
            }
        }

        void SealCard(Transform parent, SealItem item, SkinDef def, PlayerPvpData d)
        {
            var save = App.Save;
            bool owned = save.Data.OwnedSkins.Contains(item.Id) || (d != null && d.UnlockedRewards.Contains(item.Id));
            bool worn = save.IsWorn(item.Id);
            bool leagueOk = d != null && d.HighestLeague >= item.MinLeague;
            bool affordable = leagueOk && d.Seals >= item.Price;

            var card = UIKit.Plate(parent, worn ? WornFill : UIKit.SurfaceHi, 28, worn ? WornRim : UIKit.Rim, false, item.Id);
            UIKit.Size(card, -1, 0, 1);
            UIKit.Column(card.transform, 2, 12, TextAnchor.MiddleCenter);
            var tag = UIKit.Label(card.transform, Loc.F("Ligue {0}", Loc.T(PvpSkins.LeagueName(item.MinLeague))).ToUpperInvariant(), 20,
                                  owned || leagueOk ? PvpSkins.LeagueColor(item.MinLeague) : UIKit.Danger, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.FitText(tag, 14);
            UIKit.Size(tag, 28, 0);
            var preview = Portrait(card.transform, save.Loadout.With(def));
            UIKit.Size(preview, 150, 0);
            if (!owned && !leagueOk) Lock(preview, 64);
            var name = UIKit.Label(card.transform, def.Name, 22, UIKit.Sand, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.FitText(name, 15);
            UIKit.Size(name, 58, 0);

            string label = worn ? Loc.T("Équipé") : owned ? Loc.T("Équiper") : Loc.F("{0} sceaux", item.Price);
            bool usable = !_busy && !worn && (owned || affordable);
            string id = item.Id;
            var btn = UIKit.Button(card.transform, label, () => OnSealItem(id, owned), 22, usable ? ButtonStyle.Primary : ButtonStyle.Secondary);
            btn.interactable = usable;
            UIKit.FitText(btn.GetComponentInChildren<Text>(), 16);
            UIKit.Size(btn, 66, 0);
        }

        async void OnSealItem(string id, bool owned)
        {
            if (owned)
            {
                App.Save.GrantSkins(new[] { id });
                App.Save.SelectSkin(id);
                Refresh();
                return;
            }
            if (_busy || App.Pvp == null) return;
            _busy = true;
            Refresh();
            var r = await App.Pvp.BuyWithSealsAsync(id);
            if (this == null) return;
            _busy = false;
            string note;
            if (r != null && r.Ok)
            {
                App.UpdatePvpWallet(r.Seals, r.UnlockedRewards);
                if (App.PvpProfile?.Data != null) App.PvpProfile.Data.UnlockedRewards = r.UnlockedRewards;
                App.Save.SelectSkin(id);
                App.Audio.Play(Sfx.Coin);
                note = Loc.F("{0} : à toi !", Loc.T(PvpSkins.ShopItem(id).Name));
            }
            else note = PvpScreen.ErrorText(r?.Error);
            Refresh();
            if (_note != null) _note.text = note;
        }

        /// <summary>A mummy preview; legendary colours play their animation.</summary>
        Image Portrait(Transform parent, Loadout look)
        {
            var img = UIKit.Image(parent, null, Color.white);
            MummyAnimator.Show(img, App.Art, look);
            return img;
        }

        // ------------------------------------------------------------------ casino

        /// <summary>
        /// The casino: two wheels, one paid in scarabs (drawn here, the scarabs live in the save), one in seals (drawn by
        /// the server). Each turn has a 0.5 % chance of an exclusive animated legendary colour; the odds are shown in full.
        /// </summary>
        void FillCasino(PlayerPvpData d)
        {
            var intro = UIKit.Label(_list, "Chaque tour de roue a 0,5 % de chance de donner un skin LÉGENDAIRE animé, introuvable ailleurs. Les autres cases rendent des scarabées ou des sceaux.", 26, UIKit.Dim);
            UIKit.FitText(intro, 18);
            UIKit.Size(intro, 84);
            WheelCard(Casino.Scarabs, false, d);
            WheelCard(Casino.Seals, true, d);
        }

        void WheelCard(WheelDef wheel, bool seals, PlayerPvpData d)
        {
            var save = App.Save;
            var card = UIKit.Plate(_list, UIKit.SurfaceHi, 28, UIKit.Rim, false, "Wheel " + wheel.Id); // noloc
            UIKit.Size(card, 1150);
            UIKit.Column(card.transform, 14, 24, TextAnchor.UpperCenter);

            var title = UIKit.Title(card.transform, seals ? "Roue des sceaux" : "Roue des scarabées", 48, seals ? UIKit.Turquoise : UIKit.Gold);
            UIKit.FitText(title, 28);
            UIKit.Size(title, 64);

            var holder = UIKit.Rect("Holder", card.transform); // noloc
            UIKit.Size(holder, WheelSize + 20);
            var disc = UIKit.Place(UIKit.Rect("Disc", holder), 0.5f, 0.5f, WheelSize, WheelSize); // noloc
            BuildWheel(disc, wheel, seals);
            if (_wheelAngle.TryGetValue(wheel.Id, out float angle)) disc.localRotation = Quaternion.Euler(0, 0, angle);
            var pointer = UIKit.Image(holder, UIKit.Art.White, UIKit.Danger, false, "Pointer"); // noloc
            UIKit.Place(pointer.rectTransform, 0.5f, 1f, 46, 46, 0, -16);
            pointer.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            UIKit.DropShadow(pointer, 4, 0.5f);

            // The legendaries of this wheel, worn by the player's mummy (a check on those already won).
            var row = UIKit.Row(card.transform, 150, 6);
            var owned = seals ? (ICollection<string>)(d?.UnlockedRewards ?? new List<string>()) : save.Data.OwnedSkins;
            foreach (var id in wheel.Legendaries)
            {
                var def = SkinCatalog.Get(id);
                var slot = UIKit.Rect(id, row.transform);
                UIKit.Size(slot, -1, -1, 1);
                var preview = Portrait(slot, save.Loadout.With(def));
                preview.preserveAspect = true;
                UIKit.Stretch(preview.rectTransform);
                if (owned.Contains(id) || save.Data.OwnedSkins.Contains(id))
                {
                    var check = UIKit.Image(slot, UISprites.Check, UIKit.Success, false, "Owned"); // noloc
                    UIKit.Place(check.rectTransform, 1f, 0f, 40, 40, -20, 20);
                }
            }
            var names = new List<string>();
            foreach (var id in wheel.Legendaries) names.Add(Loc.T(SkinCatalog.Get(id).Name));
            var legend = UIKit.Label(card.transform, string.Join(" · ", names), 22, LegendaryColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.FitText(legend, 14);
            UIKit.Size(legend, 60);

            var odds = UIKit.Label(card.transform, OddsText(wheel, seals), 22, UIKit.Dim);
            UIKit.FitText(odds, 14);
            UIKit.Size(odds, 70);

            bool online = !seals || App.Pvp != null;
            bool can = !_spinning && online && (seals ? d != null && d.Seals >= wheel.Price : save.Data.Coins >= wheel.Price);
            string label = !online ? Loc.T("Duels hors ligne")
                         : seals ? Loc.F("Lancer · {0} sceaux", wheel.Price) : Loc.F("Lancer · {0} scarabées", wheel.Price);
            var result = UIKit.Label(card.transform, "", 30, UIKit.Sand, TextAnchor.MiddleCenter, FontStyle.Bold);
            Button btn = null;
            btn = UIKit.Button(card.transform, label, () => OnSpin(wheel, seals, disc, result, btn), 36, can ? ButtonStyle.Primary : ButtonStyle.Secondary);
            btn.interactable = can;
            UIKit.Rounded(btn.image, 50);
            UIKit.Size(btn, 104);
            result.transform.SetAsLastSibling();
            UIKit.FitText(result, 18);
            UIKit.Size(result, 56);
            _wheelNote.TryGetValue(wheel.Id, out var note);
            result.text = note ?? "";
        }

        /// <summary>Equal wedges clockwise from the top, the legendary one in pink; a hub with the wheel's currency.</summary>
        void BuildWheel(RectTransform disc, WheelDef wheel, bool seals)
        {
            int n = wheel.Segments.Length;
            float step = 360f / n;
            var rim = UIKit.Image(disc, UISprites.Circle, UIKit.Gold, false, "Rim"); // noloc
            UIKit.Stretch(rim.rectTransform, -10, -10, -10, -10);
            for (int i = 0; i < n; i++)
            {
                var seg = wheel.Segments[i];
                var wedge = UIKit.Image(disc, UISprites.Circle, WedgeColor(seg, i), false, "Wedge" + i); // noloc
                UIKit.Stretch(wedge.rectTransform);
                wedge.type = Image.Type.Filled;
                wedge.fillMethod = Image.FillMethod.Radial360;
                wedge.fillOrigin = (int)Image.Origin360.Top;
                wedge.fillClockwise = true;
                wedge.fillAmount = 1f / n;
                wedge.rectTransform.localRotation = Quaternion.Euler(0, 0, -i * step);

                // The label sits along the wedge's middle, reading from the rim.
                var arm = UIKit.Rect("Arm" + i, disc); // noloc
                UIKit.Stretch(arm);
                arm.localRotation = Quaternion.Euler(0, 0, -(i + 0.5f) * step);
                string text = seg.Kind == PrizeKind.Legendary ? Loc.T("LÉGENDE") : seg.Kind == PrizeKind.Nothing ? Loc.T("Rien") : "+" + seg.Amount;
                var label = UIKit.Label(arm, text, seg.Kind == PrizeKind.Legendary ? 30 : 38, seg.Kind == PrizeKind.Legendary ? Color.white : UIKit.Sand,
                                        TextAnchor.MiddleCenter, FontStyle.Bold);
                UIKit.Place(label.rectTransform, 0.5f, 0.5f, 170, 56, 0, WheelSize * 0.33f);
                UIKit.DropShadow(label, 3, 0.6f);
            }
            var hub = UIKit.Image(disc, UISprites.Circle, UIKit.Surface, false, "Hub"); // noloc
            UIKit.Place(hub.rectTransform, 0.5f, 0.5f, 150, 150);
            var icon = UIKit.Image(hub.transform, seals ? UISprites.Seal : UIKit.Art.Scarab, seals ? UIKit.Turquoise : UIKit.Gold, false, "Icon"); // noloc
            icon.preserveAspect = true;
            UIKit.Place(icon.rectTransform, 0.5f, 0.5f, 92, 92);
        }

        static Color WedgeColor(WheelSegment seg, int i)
        {
            if (seg.Kind == PrizeKind.Legendary) return LegendaryColor;
            if (seg.Kind == PrizeKind.Nothing) return new Color32(40, 30, 24, 255);
            if (seg.Amount >= 250) return new Color32(196, 150, 50, 255);
            return i % 2 == 0 ? new Color32(96, 70, 44, 255) : new Color32(132, 98, 58, 255);
        }

        static string OddsText(WheelDef wheel, bool seals)
        {
            var parts = new List<string>();
            foreach (var seg in wheel.Segments)
            {
                string what = seg.Kind == PrizeKind.Legendary ? Loc.T("légendaire")
                            : seg.Kind == PrizeKind.Nothing ? Loc.T("rien") : "+" + seg.Amount;
                parts.Add($"{what} {Percent(seg.Weight)}"); // noloc
            }
            return Loc.T("Chances :") + " " + string.Join(" · ", parts);
        }

        static string Percent(int weight)
        {
            string s = (weight * 100f / Casino.WeightTotal).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            return (Loc.Current == Loc.Lang.En ? s : s.Replace('.', ',')) + " %"; // noloc
        }

        void OnSpin(WheelDef wheel, bool seals, RectTransform disc, Text result, Button btn)
        {
            if (_spinning) return;
            if (seals) { SpinSeals(wheel, disc, result, btn); return; }
            var r = App.Save.SpinScarabWheel(_rng);
            if (r == null) return;
            _spinning = true;
            btn.interactable = false;
            result.text = "";
            // The price leaves the wallet now, the prize lands when the wheel stops.
            _coins.text = (App.Save.Data.Coins - (r.Kind == PrizeKind.Currency ? r.Amount : 0)).ToString();
            StartCoroutine(Turn(disc, wheel, r, () => Landed(wheel, r, false)));
        }

        async void SpinSeals(WheelDef wheel, RectTransform disc, Text result, Button btn)
        {
            if (App.Pvp == null) return;
            _spinning = true;
            btn.interactable = false;
            result.text = "";
            var r = await App.Pvp.SpinSealWheelAsync();
            if (this == null) return;
            if (r == null || !r.Ok || r.Result == null)
            {
                _spinning = false;
                _wheelNote[wheel.Id] = PvpScreen.ErrorText(r?.Error);
                Refresh();
                return;
            }
            App.UpdatePvpWallet(r.Seals, r.UnlockedRewards);
            if (App.PvpProfile?.Data != null) App.PvpProfile.Data.UnlockedRewards = r.UnlockedRewards;
            _seals.text = (r.Seals - (r.Result.Kind == PrizeKind.Currency ? r.Result.Amount : 0)).ToString();
            StartCoroutine(Turn(disc, wheel, r.Result, () => Landed(wheel, r.Result, true)));
        }

        /// <summary>Spins the disc several turns and slows it down onto the drawn wedge, ticking at every wedge.</summary>
        System.Collections.IEnumerator Turn(RectTransform disc, WheelDef wheel, SpinResult r, System.Action done)
        {
            float step = 360f / wheel.Segments.Length;
            float start = disc != null ? disc.localEulerAngles.z : 0f;
            float target = (r.Segment + 0.5f + Random.Range(-0.35f, 0.35f)) * step;
            float end = start - Mathf.Repeat(start, 360f) + 360f * 6f + target;
            int lastTick = (int)(start / step);
            for (float t = 0f; t < SpinSeconds; t += Time.unscaledDeltaTime)
            {
                if (disc == null) break; // the tab changed: the prize is already in
                float k = 1f - Mathf.Pow(1f - t / SpinSeconds, 3f);
                float z = Mathf.Lerp(start, end, k);
                disc.localRotation = Quaternion.Euler(0, 0, z);
                int tick = (int)(z / step);
                if (tick != lastTick)
                {
                    lastTick = tick;
                    App.Audio.Play(Sfx.Click);
                }
                yield return null;
            }
            if (disc != null) disc.localRotation = Quaternion.Euler(0, 0, end);
            _wheelAngle[wheel.Id] = Mathf.Repeat(end, 360f);
            done();
        }

        void Landed(WheelDef wheel, SpinResult r, bool seals)
        {
            _spinning = false;
            string note;
            if (r.Legendary != null)
            {
                // Wear it right away: that is what the player wants to see.
                App.Save.GrantSkins(new[] { r.Legendary });
                App.Save.SelectSkin(r.Legendary);
                App.Audio.Play(Sfx.Win);
                note = $"<color=#{ColorUtility.ToHtmlStringRGB(LegendaryColor)}>" + Loc.F("LÉGENDAIRE ! {0} est à toi !", Loc.T(SkinCatalog.Get(r.Legendary).Name)) + "</color>"; // noloc
            }
            else if (r.Kind == PrizeKind.Currency)
            {
                App.Audio.Play(Sfx.Coin);
                bool jackpot = wheel.Segments[r.Segment].Kind == PrizeKind.Legendary;
                note = jackpot ? Loc.F("Case légendaire ! Tu as déjà tous ses skins : +{0}", r.Amount)
                     : seals ? Loc.F("+{0} sceaux", r.Amount) : Loc.F("+{0} scarabées", r.Amount);
            }
            else note = Loc.T("Pas de chance… retente !");
            _wheelNote[wheel.Id] = note;
            Refresh();
        }
    }
}
