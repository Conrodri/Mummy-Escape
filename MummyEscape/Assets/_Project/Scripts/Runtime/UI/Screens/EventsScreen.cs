using MummyEscape.Core;
using MummyEscape.Visual;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>
    /// The campaign's third tab: the seasonal acts, each on a card in its tomb's colours (its reward on the player's
    /// mummy, the dates, the tombs escaped and its play button). Halloween is the only one so far; out of its dates it
    /// says when it comes back.
    /// </summary>
    public sealed class EventsScreen : UIScreen
    {
        public override NavTab Tab => NavTab.Solo;

        UIKit.Segmented _sections;
        RectTransform _content;

        protected override void Build()
        {
            UIKit.Backdrop(Root);
            Header("Campagne");
            var body = Body(190, 40, 40);
            UIKit.Column(body, 16);
            _sections = LevelSelectScreen.Sections(body, Router, 1);
            _content = UIKit.Scroll(body, out var scroll);
            UIKit.Size(scroll, -1, -1, -1, 1);
            _content.GetComponent<VerticalLayoutGroup>().spacing = 20;
        }

        public override void OnShow()
        {
            App.Lighting.SetMood(false);
            _sections.Select(1);
            UIKit.ClearChildren(_content);
            var intro = UIKit.Label(_content, "Des actes le temps d'une saison : leurs propres tombeaux et une récompense à garder pour toujours.", 26, UIKit.Dim);
            UIKit.FitText(intro, 16);
            UIKit.Size(intro, 80);
            HalloweenCard();
        }

        void HalloweenCard()
        {
            var save = App.Save;
            bool open = Halloween.IsOpen(System.DateTime.UtcNow);
            var theme = TombTheme.ForAct(Halloween.Theme);
            var accent = ShopScreen.HalloweenColor;
            var reward = SkinCatalog.Get(Halloween.RewardSkin);

            var card = UIKit.Plate(_content, Color.white, 30, new Color(accent.r, accent.g, accent.b, open ? 0.7f : 0.3f), false, "Halloween"); // noloc
            UIFx.Gradient(card, Color.Lerp(theme.Wall, Color.black, 0.25f), Color.Lerp(theme.FloorDark, Color.black, 0.6f));
            UIKit.Size(card, 420);
            var row = card.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(18, 26, 22, 22);
            row.spacing = 20;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;

            // The act's reward, worn by the player's mummy.
            var stage = UIKit.Rect("Stage", card.transform); // noloc
            UIKit.Size(stage, 300, 280, 0);
            UIFx.Halo(stage, new Color(accent.r, accent.g, accent.b, 0.4f), 360);
            var preview = UIKit.Image(stage, null, Color.white, false, "Mummy"); // noloc
            preview.preserveAspect = true;
            UIKit.Stretch(preview.rectTransform);
            MummyAnimator.Show(preview, App.Art, save.Loadout.With(reward));

            var info = UIKit.Rect("Info", card.transform); // noloc
            UIKit.Size(info, -1, 0, 1);
            UIKit.Column(info, 8, 0, TextAnchor.MiddleLeft);
            var title = UIKit.Title(info, Loc.T("Halloween"), 56, accent, TextAnchor.MiddleLeft);
            UIKit.FitText(title, 30);
            UIKit.Size(title, 70);
            var dates = UIKit.Label(info, open ? Loc.T("Jusqu'au 7 novembre") : Loc.T("De retour le 24 octobre"), 26, open ? UIKit.Sand : UIKit.Dim, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIKit.Size(dates, 36);
            var bar = UIFx.Bar(info, 22, accent);
            bar.fillAmount = save.HalloweenDone ? 1f : save.Data.HalloweenCleared / (float)Halloween.TombCount;
            var count = UIKit.Label(info, save.HalloweenDone ? Loc.T("Acte terminé !") : Loc.F("{0} / {1} tombeaux", save.Data.HalloweenCleared, Halloween.TombCount), 24, UIKit.Sand, TextAnchor.MiddleLeft);
            UIKit.Size(count, 32);
            var prize = UIKit.Label(info, Loc.F("Récompense : « {0} » et le thème d'accueil Halloween", Loc.T(reward.Name)), 22, UIKit.Dim, TextAnchor.MiddleLeft);
            UIKit.FitText(prize, 15);
            UIKit.Size(prize, 56);
            if (open)
            {
                var play = UIKit.Button(info, save.HalloweenDone ? Loc.T("Rejouer") : Loc.F("Jouer · tombeau {0}", save.HalloweenNext), Play, 30, ButtonStyle.Primary);
                UIKit.FitText(play.GetComponentInChildren<Text>(), 18);
                UIKit.Size(play, 90);
            }
            UIFx.PopIn(card, 0f, 0.94f);
        }

        void Play()
        {
            int tomb = App.Save.HalloweenNext;
            PlayGate.HalloweenRun(App, () =>
            {
                Router.Open<HudScreen>();
                _ = App.Game.StartHalloween(tomb);
            });
        }
    }
}
