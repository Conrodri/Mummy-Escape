using System.Collections.Generic;
using MummyEscape.Pvp;
using MummyEscape.Services;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// The casino, a tab of its own: two wheels, one paid in scarabs (drawn here, the scarabs live in the save), one in
    /// seals (drawn by the server). Each turn has a 0.5 % chance of an exclusive animated legendary colour; the odds are
    /// shown in full. The wheel sits under blinking bulbs and a turning glow; a legendary opens in the zoom.
    /// </summary>
    public sealed class CasinoScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Casino;

        internal static readonly Color LegendaryColor = new Color32(255, 96, 220, 255);
        const float WheelSize = 620;
        const float SpinSeconds = 4f;
        const int Bulbs = 20;

        Text _coins, _seals, _title, _caption, _legend, _odds, _result;
        UIKit.Segmented _tabs;
        RectTransform _disc, _legendaries, _resultPop;
        Image _glow;
        Button _spin;
        Text _spinLabel;
        int _tab;
        int _request;
        readonly System.Random _rng = new System.Random();
        /// <summary>A wheel is turning: no other spin until it stops.</summary>
        bool _spinning;
        /// <summary>Last outcome of each wheel, kept across rebuilds.</summary>
        readonly Dictionary<string, string> _wheelNote = new Dictionary<string, string>();
        /// <summary>Where each wheel stopped, so it still shows its last prize.</summary>
        readonly Dictionary<string, float> _wheelAngle = new Dictionary<string, float>();

        WheelDef Wheel => _tab == 0 ? Casino.Scarabs : Casino.Seals;
        bool Seals => _tab == 1;

        protected override void Build()
        {
            var back = UIKit.Backdrop(Root);
            // A casino glow: pink at the top, gold under the wheel.
            var pink = UIKit.Image(back.transform, UISprites.RadialGlow, new Color(1f, 0.3f, 0.8f, 0.14f), false, "Pink"); // noloc
            pink.preserveAspect = false;
            UIKit.Place(pink.rectTransform, 0.5f, 1f, 2000, 1500, 0, -500);
            Header("Casino");
            var body = Body(190, 40, 40);
            UIKit.Column(body, 16);

            var wallet = UIKit.Row(body, 72, 14);
            wallet.childAlignment = TextAnchor.MiddleCenter;
            _coins = UIKit.Chip(wallet.transform, UIKit.Art.Scarab, "", UIKit.Gold, 68);
            _seals = UIKit.Chip(wallet.transform, UISprites.Seal, "", UIKit.Turquoise, 68);

            _tabs = new UIKit.Segmented(body, new[] { "Roue des scarabées", "Roue des sceaux" }, i =>
            {
                if (_spinning) { _tabs.Select(_tab); return; }
                _tab = i;
                Refresh();
            }, 88);

            _title = UIKit.Title(body, "", 50, UIKit.Gold);
            UIKit.FitText(_title, 28);
            UIKit.Size(_title, 70);

            // The wheel takes the room left; FitInParent shrinks it on short screens.
            var holder = UIKit.Rect("Holder", body); // noloc
            UIKit.Size(holder, WheelSize * 0.6f, -1, -1, 1);
            var stage = UIKit.Place(UIKit.Rect("Stage", holder), 0.5f, 0.5f, WheelSize + 90, WheelSize + 90); // noloc
            UIKit.FitInParent(stage, 0);
            _glow = UIKit.Image(stage, UISprites.RadialGlow, new Color(1f, 0.75f, 0.35f, 0.5f), false, "Glow"); // noloc
            _glow.preserveAspect = false;
            UIKit.Place(_glow.rectTransform, 0.5f, 0.5f, WheelSize * 1.5f, WheelSize * 1.5f);
            UIFx.Pulse(_glow, 0.05f, 2.4f);
            // Bulbs around the rim, blinking in two alternating groups.
            var bulbs = UIKit.Stretch(UIKit.Rect("Bulbs", stage)); // noloc
            for (int i = 0; i < Bulbs; i++)
            {
                float a = i * Mathf.PI * 2f / Bulbs;
                var bulb = UIKit.Image(bulbs, UISprites.Circle, new Color(1f, 0.92f, 0.6f, 1f), false, "Bulb"); // noloc
                float r = WheelSize / 2f + 30f;
                UIKit.Place(bulb.rectTransform, 0.5f, 0.5f, 22, 22, Mathf.Sin(a) * r, Mathf.Cos(a) * r);
                UIFx.Blink(bulb, 0.9f, i % 2 * 0.5f, 0.2f);
            }
            _disc = UIKit.Place(UIKit.Rect("Disc", stage), 0.5f, 0.5f, WheelSize, WheelSize); // noloc
            var pointer = UIKit.Image(stage, UIKit.Art.White, UIKit.Danger, false, "Pointer"); // noloc
            UIKit.Place(pointer.rectTransform, 0.5f, 1f, 50, 50, 0, -30);
            pointer.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            UIKit.DropShadow(pointer, 4, 0.5f);

            // The legendaries of the wheel (a check on those already won); a tap shows one up close.
            _caption = UIKit.Label(body, "À gagner", 24, LegendaryColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.FitText(_caption, 16);
            UIKit.Size(_caption, 32);
            _legendaries = UIKit.Row(body, 140, 10).GetComponent<RectTransform>();
            _legend = UIKit.Label(body, "", 22, LegendaryColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.FitText(_legend, 14);
            UIKit.Size(_legend, 34);
            _odds = UIKit.Label(body, "", 20, UIKit.Dim);
            UIKit.FitText(_odds, 14);
            UIKit.Size(_odds, 56);

            var resultRow = UIKit.Rect("Result", body); // noloc
            UIKit.Size(resultRow, 60);
            _resultPop = UIKit.Stretch(UIKit.Rect("Pop", resultRow)); // noloc
            _result = UIKit.Label(_resultPop, "", 34, UIKit.Sand, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.FitText(_result, 18);
            UIKit.Stretch(_result.rectTransform);

            var spinRow = UIKit.Rect("SpinRow", body); // noloc
            UIKit.Size(spinRow, 120);
            var pulse = UIKit.Stretch(UIKit.Rect("Pulse", spinRow), 40, 0, 40, 0); // noloc
            UIFx.Pulse(pulse, 0.025f, 1.4f);
            _spin = UIKit.Button(pulse, "Lancer", OnSpin, 40, ButtonStyle.Primary);
            UIKit.Stretch((RectTransform)_spin.transform);
            _spinLabel = _spin.GetComponentInChildren<Text>();
            UIKit.FitText(_spinLabel, 22);
        }

        public override void OnShow()
        {
            _spinning = false; // a spin cut short by leaving the screen was already paid and cashed in
            // Where paid random draws are banned (Belgium), only the seal wheel: seals are won, never bought.
            bool banned = CountryService.LootBoxesBanned(App.Save.Country);
            _tabs.Root.SetActive(!banned);
            if (banned) _tab = 1;
            _tabs.Select(_tab);
            Refresh();
            LoadSeals();
        }

        public override void OnHide() => App.PublishLookIfChanged();

        /// <summary>The seals live on the server: fetched once if the duel home was never opened.</summary>
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
            var d = App.PvpProfile?.Data;
            var wheel = Wheel;
            bool seals = Seals;
            _coins.text = save.Data.Coins.ToString();
            _seals.transform.parent.gameObject.SetActive(App.Pvp != null);
            _seals.text = (d?.Seals ?? 0).ToString();
            _title.text = Loc.T(seals ? "Roue des sceaux" : "Roue des scarabées");
            UIKit.TintTitle(_title, seals ? UIKit.Turquoise : UIKit.Gold);
            _glow.color = seals ? new Color(0.3f, 1f, 0.9f, 0.4f) : new Color(1f, 0.75f, 0.35f, 0.5f);

            UIKit.ClearChildren(_disc);
            BuildWheel(_disc, wheel, seals);
            _disc.localRotation = Quaternion.Euler(0, 0, _wheelAngle.TryGetValue(wheel.Id, out float angle) ? angle : 0f);

            UIKit.ClearChildren(_legendaries);
            var owned = seals ? (ICollection<string>)(d?.UnlockedRewards ?? new List<string>()) : save.Data.OwnedSkins;
            var names = new List<string>();
            int k = 0;
            foreach (var id in wheel.Legendaries)
            {
                var def = SkinCatalog.Get(id);
                names.Add(Loc.T(def.Name));
                bool has = owned.Contains(id) || save.Data.OwnedSkins.Contains(id);
                var slot = UIKit.Plate(_legendaries, new Color(0, 0, 0, 0.35f), 24, has ? (Color?)UIKit.Success : new Color(1f, 0.4f, 0.85f, 0.4f), false, id);
                UIKit.Size(slot, 140, -1, 1); // a height of its own: the row would give an empty rect none
                slot.raycastTarget = true;
                var btn = slot.gameObject.AddComponent<Button>();
                btn.targetGraphic = slot;
                btn.onClick.AddListener(() => Zoom(def, has));
                slot.gameObject.AddComponent<PressScale>();
                var preview = ItemPreview.Create(slot.transform, def);
                UIKit.Stretch(preview.rectTransform, 8, 8, 8, 8);
                UIFx.PopIn(slot, k++ * 0.05f);
                if (has)
                {
                    var check = UIKit.Image(slot.transform, UISprites.Check, UIKit.Success, false, "Owned"); // noloc
                    UIKit.Place(check.rectTransform, 1f, 0f, 36, 36, -18, 18);
                }
            }
            _caption.text = Loc.T(wheel.DirectPrice > 0 ? "À gagner ou à acheter" : "À gagner").ToUpperInvariant();
            _legend.text = string.Join(" · ", names);
            _odds.text = OddsText(wheel);

            bool online = !seals || App.Pvp != null;
            bool can = !_spinning && online && (seals ? d != null && d.Seals >= wheel.Price : save.Data.Coins >= wheel.Price);
            _spinLabel.text = !online ? Loc.T("Duels hors ligne")
                            : seals ? Loc.F("Lancer · {0} sceaux", wheel.Price) : Loc.F("Lancer · {0} scarabées", wheel.Price);
            _spin.interactable = can;
            _wheelNote.TryGetValue(wheel.Id, out var note);
            _result.text = note ?? "";
        }

        void Zoom(SkinDef def, bool owned)
        {
            bool worn = App.Save.IsWorn(def.Id);
            int price = Seals ? 0 : Wheel.DirectPrice;
            if (owned || price <= 0)
            {
                Router.Open<ItemZoomScreen>().Show(def, Loc.T("Légendaire"), LegendaryColor,
                    Loc.T("Exclusivité du casino : 0,5 % de chance à chaque tour de roue. Bandages animés."),
                    worn ? Loc.T("Équipé") : owned ? Loc.T("Équiper") : Loc.T("À gagner sur la roue"),
                    owned && !worn ? () => { App.Save.SelectSkin(def.Id); Refresh(); } : (System.Action)null);
                return;
            }
            // Players who would rather not gamble buy it outright, at what it costs on average at the wheel.
            bool afford = App.Save.Data.Coins >= price;
            Router.Open<ItemZoomScreen>().Show(def, Loc.T("Légendaire"), LegendaryColor,
                Loc.F("Exclusivité du casino : 0,5 % de chance à chaque tour de roue, ou achat direct pour {0} scarabées. Bandages animés.", price),
                afford ? Loc.F("Acheter · {0} scarabées", price) : Loc.F("Il te faut {0} scarabées", price),
                afford ? () => BuyLegendary(def) : (System.Action)null);
        }

        void BuyLegendary(SkinDef def)
        {
            if (!App.Save.BuyWheelLegendary(def.Id)) return;
            App.Save.SelectSkin(def.Id);
            App.Audio.Play(Services.Sfx.Coin);
            Refresh();
            Router.Open<ItemZoomScreen>().Show(def, Loc.T("Légendaire"), LegendaryColor,
                Loc.F("{0} est à toi !", Loc.T(def.Name)), Loc.T("Équipé"), null);
        }

        /// <summary>Equal wedges clockwise from the top, the legendary one in pink; a hub with the wheel's currency.</summary>
        static void BuildWheel(RectTransform disc, WheelDef wheel, bool seals)
        {
            int n = wheel.Segments.Length;
            float step = 360f / n;
            var rim = UIKit.Image(disc, UISprites.Circle, UIKit.Gold, false, "Rim"); // noloc
            UIKit.Stretch(rim.rectTransform, -12, -12, -12, -12);
            UIFx.Gradient(rim, new Color(1f, 0.95f, 0.75f), new Color(0.75f, 0.55f, 0.2f));
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
                var label = UIKit.Label(arm, text, seg.Kind == PrizeKind.Legendary ? 32 : 42, seg.Kind == PrizeKind.Legendary ? Color.white : UIKit.Sand,
                                        TextAnchor.MiddleCenter, FontStyle.Bold);
                UIKit.Place(label.rectTransform, 0.5f, 0.5f, 190, 60, 0, WheelSize * 0.33f);
                UIKit.DropShadow(label, 3, 0.6f);
            }
            var hub = UIKit.Image(disc, UISprites.Circle, UIKit.Surface, false, "Hub"); // noloc
            UIKit.Place(hub.rectTransform, 0.5f, 0.5f, 160, 160);
            var hubRim = UIKit.Image(hub.transform, UISprites.Ring, UIKit.Gold, false, "HubRim"); // noloc
            UIKit.Stretch(hubRim.rectTransform);
            var icon = UIKit.Image(hub.transform, seals ? UISprites.Seal : UIKit.Art.Scarab, seals ? UIKit.Turquoise : Color.white, false, "Icon"); // noloc
            icon.preserveAspect = true;
            UIKit.Place(icon.rectTransform, 0.5f, 0.5f, 96, 96);
        }

        static Color WedgeColor(WheelSegment seg, int i)
        {
            if (seg.Kind == PrizeKind.Legendary) return LegendaryColor;
            if (seg.Kind == PrizeKind.Nothing) return new Color32(40, 30, 24, 255);
            if (seg.Amount >= 250) return new Color32(196, 150, 50, 255);
            return i % 2 == 0 ? new Color32(96, 70, 44, 255) : new Color32(132, 98, 58, 255);
        }

        static string OddsText(WheelDef wheel)
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

        void OnSpin()
        {
            if (_spinning) return;
            var wheel = Wheel;
            if (Seals) { SpinSeals(wheel); return; }
            var r = App.Save.SpinScarabWheel(_rng);
            if (r == null) return;
            Started();
            // The price leaves the wallet now, the prize lands when the wheel stops.
            _coins.text = (App.Save.Data.Coins - (r.Kind == PrizeKind.Currency ? r.Amount : 0)).ToString();
            StartCoroutine(Turn(wheel, r, () => Landed(wheel, r, false)));
        }

        async void SpinSeals(WheelDef wheel)
        {
            if (App.Pvp == null) return;
            Started();
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
            StartCoroutine(Turn(wheel, r.Result, () => Landed(wheel, r.Result, true)));
        }

        void Started()
        {
            _spinning = true;
            _spin.interactable = false;
            _result.text = "";
        }

        /// <summary>Spins the disc several turns and slows it down onto the drawn wedge, ticking at every wedge.</summary>
        System.Collections.IEnumerator Turn(WheelDef wheel, SpinResult r, System.Action done)
        {
            float step = 360f / wheel.Segments.Length;
            float start = _disc.localEulerAngles.z;
            float target = (r.Segment + 0.5f + Random.Range(-0.35f, 0.35f)) * step;
            float end = start - Mathf.Repeat(start, 360f) + 360f * 6f + target;
            int lastTick = (int)(start / step);
            for (float t = 0f; t < SpinSeconds; t += Time.unscaledDeltaTime)
            {
                float k = 1f - Mathf.Pow(1f - t / SpinSeconds, 3f);
                float z = Mathf.Lerp(start, end, k);
                _disc.localRotation = Quaternion.Euler(0, 0, z);
                int tick = (int)(z / step);
                if (tick != lastTick)
                {
                    lastTick = tick;
                    App.Audio.Play(Sfx.Click);
                }
                yield return null;
            }
            _disc.localRotation = Quaternion.Euler(0, 0, end);
            _wheelAngle[wheel.Id] = Mathf.Repeat(end, 360f);
            done();
        }

        void Landed(WheelDef wheel, SpinResult r, bool seals)
        {
            _spinning = false;
            string note;
            SkinDef won = null;
            if (r.Legendary != null)
            {
                // Wear it right away: that is what the player wants to see.
                App.Save.GrantSkins(new[] { r.Legendary });
                App.Save.SelectSkin(r.Legendary);
                App.Audio.Play(Sfx.Win);
                won = SkinCatalog.Get(r.Legendary);
                note = $"<color=#{ColorUtility.ToHtmlStringRGB(LegendaryColor)}>" + Loc.F("LÉGENDAIRE ! {0} est à toi !", Loc.T(won.Name)) + "</color>"; // noloc
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
            UIFx.PopIn(_resultPop, 0f, 0.5f);
            if (won != null)
                Router.Open<ItemZoomScreen>().Show(won, Loc.T("Légendaire gagné !"), LegendaryColor,
                    Loc.T("Il est déjà sur ta momie."), Loc.T("Génial !"), () => { });
        }
    }
}
