using System.Collections.Generic;
using System.Text.RegularExpressions;
using MummyEscape.Services;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.UI
{
    /// <summary>
    /// Keeps the legendary titles moving: every few frames, redraws each one written by <see cref="TitleBook.Shimmer"/>
    /// in the texts of the canvas (profile, friends, VS screen, HUD...), so no screen has to animate them itself.
    /// </summary>
    public sealed class TitleShimmer : MonoBehaviour
    {
        const float Step = 0.08f, Rescan = 0.5f;
        static readonly Regex Tags = new Regex("<[^>]*>");

        readonly List<Text> _texts = new List<Text>();
        readonly List<Text> _scan = new List<Text>();
        float _nextStep, _nextScan;

        void Update()
        {
            float now = Time.unscaledTime;
            if (now >= _nextScan)
            {
                _nextScan = now + Rescan;
                _texts.Clear();
                GetComponentsInChildren(false, _scan);
                foreach (var t in _scan)
                    if (t.supportRichText && t.text != null && t.text.Contains(TitleBook.ShimmerOpen)) _texts.Add(t);
            }
            if (now < _nextStep || _texts.Count == 0) return;
            _nextStep = now + Step;
            foreach (var t in _texts)
                if (t != null && t.isActiveAndEnabled) t.text = Redraw(t.text, now);
        }

        static string Redraw(string text, float time)
        {
            int from = 0;
            while (true)
            {
                int open = text.IndexOf(TitleBook.ShimmerOpen, from, System.StringComparison.Ordinal);
                if (open < 0) return text;
                int start = open + TitleBook.ShimmerOpen.Length;
                int close = text.IndexOf(TitleBook.ShimmerClose, start, System.StringComparison.Ordinal);
                if (close < 0) return text;
                string plain = Tags.Replace(text.Substring(start, close - start), "");
                string fresh = TitleBook.Shimmer(plain, time);
                text = text.Substring(0, open) + fresh + text.Substring(close + TitleBook.ShimmerClose.Length);
                from = open + fresh.Length;
            }
        }
    }
}
