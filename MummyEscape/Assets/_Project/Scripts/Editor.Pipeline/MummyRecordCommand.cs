using System.Collections;
using System.IO;
using MummyEscape.App;
using Unity.Pipeline.Commands;
using UnityEngine;

namespace MummyEscape.EditorTools
{
    /// <summary>
    /// Records the Game view as numbered JPGs at a fixed frame rate (trailer / ad footage), game time locked to the frames
    /// so the clip is smooth however slow the disk is:
    ///   unity command mummy_record --dir C:/clips/duel --seconds 12 --autoplay true
    /// Then: ffmpeg -framerate 30 -i C:/clips/duel/%05d.jpg -c:v libx264 -pix_fmt yuv420p duel.mp4
    /// The resolution is the Game view's (set it to 1080x1920 first).
    /// </summary>
    public static class MummyRecordCommand
    {
        [CliCommand("mummy_record", "Mummy Rush (Play mode): record the Game view as JPG frames; optionally play the optimal route at full speed.", Tags = new[] { "mummy" })]
        public static string Record(
            [CliArg("dir", "Output folder (absolute), emptied first")] string dir = "",
            [CliArg("seconds", "Game seconds to record")] float seconds = 10f,
            [CliArg("fps", "Frames per second")] int fps = 30,
            [CliArg("autoplay", "Also play the optimal route, each step as soon as the last one is shown")] bool autoplay = false,
            [CliArg("stop", "Stop the recording in progress")] bool stop = false)
        {
            if (!Application.isPlaying || GameApp.I == null) return "Enter Play mode first.";
            Application.runInBackground = true;
            if (!GameApp.I.gameObject.TryGetComponent<Recorder>(out var rec)) rec = GameApp.I.gameObject.AddComponent<Recorder>();
            rec.StopAllCoroutines();
            rec.Restore();
            if (stop) return $"Stopped after {rec.Frames} frames.";
            if (string.IsNullOrEmpty(dir)) return "Pass --dir.";
            if (Directory.Exists(dir)) foreach (var f in Directory.GetFiles(dir, "*.jpg")) File.Delete(f);
            Directory.CreateDirectory(dir);
            rec.StartCoroutine(rec.Run(dir, Mathf.RoundToInt(seconds * fps), fps));
            if (autoplay) rec.StartCoroutine(rec.Autoplay());
            return $"Recording {seconds}s at {fps} fps, {Screen.width}x{Screen.height}, to {dir}";
        }

        sealed class Recorder : MonoBehaviour
        {
            public int Frames;

            Canvas _canvas;
            RenderMode _mode;
            RenderTexture _rt;

            /// <summary>
            /// The camera renders world + UI (canvas in camera space, like mummy_capture) into a texture every frame: an
            /// unfocused Game view never reaches WaitForEndOfFrame, so ScreenCapture cannot be used.
            /// </summary>
            public IEnumerator Run(string dir, int frames, int fps)
            {
                var cam = GameApp.I.Camera.Cam;
                _canvas = GameApp.I.UI.GetComponentInChildren<Canvas>();
                _mode = _canvas.renderMode;
                // The editor's Screen.safeArea can keep an older Game view size (UI pushed off the frame): full screen
                // for the clip; UIRouter applies the real one again when it changes.
                if (_canvas.transform.Find("SafeArea") is RectTransform safe)
                {
                    safe.anchorMin = Vector2.zero;
                    safe.anchorMax = Vector2.one;
                }
                _rt = new RenderTexture(Screen.width, Screen.height, 24, RenderTextureFormat.ARGB32);
                var tex = new Texture2D(_rt.width, _rt.height, TextureFormat.RGB24, false);
                Time.captureFramerate = fps;
                Frames = 0;
                while (Frames < frames)
                {
                    yield return null;
                    _canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    _canvas.worldCamera = cam;
                    _canvas.planeDistance = 1f;
                    var prev = cam.targetTexture;
                    cam.targetTexture = _rt;
                    cam.Render();
                    cam.targetTexture = prev;
                    var active = RenderTexture.active;
                    RenderTexture.active = _rt;
                    tex.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0);
                    RenderTexture.active = active;
                    File.WriteAllBytes(Path.Combine(dir, $"{Frames:00000}.jpg"), tex.EncodeToJPG(93));
                    Frames++;
                }
                Destroy(tex);
                Restore();
            }

            public void Restore()
            {
                Time.captureFramerate = 0;
                if (_canvas != null) _canvas.renderMode = _mode;
                if (_rt != null) { _rt.Release(); Destroy(_rt); _rt = null; }
            }

            /// <summary>Waits for the next run to start (preview, countdown), then submits the next route step every frame: the game drops it while a step is shown.</summary>
            public IEnumerator Autoplay()
            {
                var game = GameApp.I.Game;
                var previous = game.Session; // the run is started after the recording
                while (game.Session == null || game.Session == previous || game.Previewing || !game.Accepting) yield return null;
                var session = game.Session;
                var actions = session.Level.Solution.Actions;
                while (game.Session == session && session.Moves < actions.Count)
                {
                    if (game.Accepting && !game.DuelWaiting) game.Submit(actions[session.Moves]);
                    yield return null;
                }
            }
        }
    }
}
