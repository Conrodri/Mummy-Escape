using System;
using System.IO;
using System.Linq;
using MummyEscape.App;
using MummyEscape.UI;
using Unity.Pipeline.Commands;
using UnityEngine;
using UnityEngine.UI;

namespace MummyEscape.EditorTools
{
    /// <summary>
    /// Renders the game (world + UI) at a phone resolution into a PNG, independent of the Game view size or focus:
    ///   unity command mummy_capture --screen SettingsScreen --path Logs/shots/settings.png
    /// Play mode only. The UI canvas is switched to camera space for the render, then restored.
    /// </summary>
    public static class MummyCaptureCommand
    {
        [CliCommand("mummy_capture", "Mummy Escape (Play mode): optionally open a screen, then render a 1080x1920 PNG of world + UI.", Tags = new[] { "mummy" })]
        public static string Capture(
            [CliArg("path", "Output PNG path, relative to the project")] string path = "Logs/shots/capture.png",
            [CliArg("screen", "UIScreen type to open first (e.g. SettingsScreen), empty = current")] string screen = "",
            [CliArg("reset", "Reset the history to the main menu before opening the screen")] bool reset = true,
            [CliArg("width", "Width in pixels")] int width = 1080,
            [CliArg("height", "Height in pixels")] int height = 1920)
        {
            if (!Application.isPlaying || GameApp.I == null) return "Enter Play mode first.";
            var app = GameApp.I;
            Application.runInBackground = true; // the Editor otherwise freezes Play mode while its window is unfocused

            if (!string.IsNullOrEmpty(screen))
            {
                var type = typeof(UIScreen).Assembly.GetTypes().FirstOrDefault(t => t.Name == screen && typeof(UIScreen).IsAssignableFrom(t));
                if (type == null) return "Unknown screen " + screen;
                if (reset) app.UI.Reset<UI.Screens.MainMenuScreen>();
                typeof(UIRouter).GetMethod(nameof(UIRouter.Open)).MakeGenericMethod(type).Invoke(app.UI, null);
            }

            var cam = app.Camera.Cam;
            var canvas = app.UI.GetComponentInChildren<Canvas>();
            var scaler = canvas.GetComponent<CanvasScaler>();
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var prevTarget = cam.targetTexture;
            var prevMode = canvas.renderMode;
            try
            {
                cam.targetTexture = rt;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 1f;
                scaler.enabled = false;
                scaler.enabled = true; // re-run the scaler for the render texture size
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)canvas.transform);
                Canvas.ForceUpdateCanvases();
                cam.Render();

                var prevActive = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = prevActive;

                string full = Path.GetFullPath(path);
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                File.WriteAllBytes(full, tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                return full;
            }
            catch (Exception e)
            {
                return "Capture failed: " + e;
            }
            finally
            {
                cam.targetTexture = prevTarget;
                canvas.renderMode = prevMode;
                scaler.enabled = false;
                scaler.enabled = true;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
