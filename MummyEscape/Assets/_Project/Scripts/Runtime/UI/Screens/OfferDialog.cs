using System;
using System.Collections;
using System.Threading.Tasks;
using MummyEscape.Monetization;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI.Screens
{
    /// <summary>Modal with a title, a text and a column of choices (out of games, purchases, rewards collected).</summary>
    public sealed class OfferDialog : UIScreen
    {
        public override bool IsModal => true;

        Text _title, _text;
        RectTransform _buttons;

        protected override void Build()
        {
            var shade = UIKit.Image(Root, UIKit.Art.White, new Color(0, 0, 0, 0.85f), true, "Shade"); // noloc
            UIKit.Stretch(shade.rectTransform, -600, -600, -600, -600);
            shade.gameObject.AddComponent<Button>().onClick.AddListener(() => Router.Close(this)); // tap outside = later
            var panel = UIKit.Card(Root, 44, 22);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(960, 0);
            UIKit.FitInParent(panel);
            panel.GetComponent<Image>().raycastTarget = true;
            panel.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _title = UIKit.Label(panel, "", 48, UIKit.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIKit.Size(_title, 110);
            _text = UIKit.Label(panel, "", 32, UIKit.Sand);
            _text.lineSpacing = 1.1f;
            _buttons = UIKit.Rect("Buttons", panel); // noloc
            UIKit.Column(_buttons, 16, 0);
            _buttons.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        /// <summary>Each choice closes the dialog, then runs (a null action only closes). Texts are translated here.</summary>
        public OfferDialog Configure(string title, string text, params (string label, ButtonStyle style, Action action)[] choices)
        {
            _title.text = Loc.T(title);
            _text.text = text;
            _dismissed = null;
            UIKit.ClearChildren(_buttons);
            foreach (var c in choices)
            {
                var action = c.action;
                var b = UIKit.Button(_buttons, c.label, () =>
                {
                    _dismissed = null; // a choice was made
                    Router.Close(this);
                    action?.Invoke();
                }, UIKit.TextSize, c.style);
                UIKit.FitText(b.GetComponentInChildren<Text>(), 20);
                UIKit.Size(b, UIKit.ButtonHeight);
            }
            return this;
        }

        Action _dismissed;

        /// <summary>Called when the dialog closes without a choice (tap outside, back button).</summary>
        public OfferDialog OnDismissed(Action dismissed)
        {
            _dismissed = dismissed;
            return this;
        }

        public override void OnHide()
        {
            var dismissed = _dismissed;
            _dismissed = null;
            dismissed?.Invoke();
        }
    }

    /// <summary>
    /// The ad of the simulated ad network (editor and development builds): a 5-second countdown, then the reward.
    /// Closing it before the end gives nothing, like a real rewarded ad.
    /// </summary>
    public sealed class TestAdScreen : UIScreen
    {
        public override bool IsModal => true;
        const float Seconds = 5f;

        Text _count;
        Button _close;
        TaskCompletionSource<bool> _done;

        protected override void Build()
        {
            var shade = UIKit.Image(Root, UIKit.Art.White, new Color(0.02f, 0.02f, 0.04f, 1f), true, "Shade"); // noloc
            UIKit.Stretch(shade.rectTransform, -600, -600, -600, -600);
            var column = UIKit.Rect("Column", Root); // noloc
            UIKit.Stretch(column, 60, 300, 60, 300);
            UIKit.Column(column, 30, 0, TextAnchor.MiddleCenter);
            UIKit.Size(UIKit.Title(column, "PUBLICITÉ", 80, UIKit.Gold), 120);
            UIKit.Size(UIKit.Label(column, "Pub de test : ici s'affichera la vidéo de la régie publicitaire.", 34, UIKit.Dim), 120);
            _count = UIKit.Title(column, "", 140, UIKit.Sand);
            UIKit.Size(_count, 200);
            _close = UIKit.Button(column, "Fermer", Close, UIKit.TextSize, ButtonStyle.Secondary);
            UIKit.Size(_close, UIKit.ButtonHeight, 420);
        }

        bool _watched;
        Coroutine _countdown;

        /// <summary>Plays the ad; true once it ran to the end and the player closed it.</summary>
        public Task<bool> Play()
        {
            _done?.TrySetResult(false);
            _done = new TaskCompletionSource<bool>();
            _watched = false;
            // Only its own countdown: the router fades the screen in with another coroutine.
            if (_countdown != null) StopCoroutine(_countdown);
            _countdown = StartCoroutine(Countdown());
            return _done.Task;
        }

        IEnumerator Countdown()
        {
            UIKit.SetLabel(_close, "Fermer (sans récompense)");
            for (float t = Seconds; t > 0; t -= Time.unscaledDeltaTime)
            {
                _count.text = Mathf.CeilToInt(t).ToString();
                yield return null;
            }
            _count.text = "✓"; // noloc
            _watched = true;
            UIKit.SetLabel(_close, "Récupérer la récompense");
        }

        void Close() => Router.Close(this);

        public override void OnHide() => _done?.TrySetResult(_watched);
    }

    /// <summary>Stand-in ad network for the editor and development builds.</summary>
    public sealed class SimulatedAds : IAdProvider
    {
        readonly UIRouter _router;
        public SimulatedAds(UIRouter router) => _router = router;
        public bool IsReady => true;

        public Task<bool> ShowRewardedAsync() => _router.Open<TestAdScreen>().Play();
    }

    /// <summary>Stand-in store for the editor and development builds: asks for a confirmation, charges nothing.</summary>
    public sealed class SimulatedStore : IStoreProvider
    {
        readonly UIRouter _router;
        public SimulatedStore(UIRouter router) => _router = router;
        public bool IsReady => true;
        public string LocalizedPrice(string productId) => null;

        public Task<PurchaseOutcome> BuyAsync(string productId)
        {
            var done = new TaskCompletionSource<PurchaseOutcome>();
            var pack = GoldShop.Pack(productId);
            _router.Open<OfferDialog>().Configure("Achat de test",
                Loc.F("{0} scarabées dorés pour {1}.\n\nVersion de développement : aucun paiement réel.", pack?.Gold + pack?.Bonus, pack?.FallbackPrice),
                ("Acheter (test)", ButtonStyle.Primary, () => done.TrySetResult(new PurchaseOutcome { Ok = true, TransactionId = "test-" + Guid.NewGuid().ToString("N") })), // noloc
                ("Annuler", ButtonStyle.Secondary, () => done.TrySetResult(new PurchaseOutcome { Cancelled = true })))
                // Tapping outside the dialog closes it without a choice: a cancel.
                .OnDismissed(() => done.TrySetResult(new PurchaseOutcome { Cancelled = true }));
            return done.Task;
        }
    }
}
