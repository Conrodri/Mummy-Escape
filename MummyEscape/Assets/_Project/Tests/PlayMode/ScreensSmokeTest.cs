using System.Collections;
using System.Collections.Generic;
using System.Linq;
using MummyEscape.App;
using MummyEscape.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MummyEscape.Tests
{
    /// <summary>
    /// Builds and shows every menu screen once, in play mode, so a Build() or OnShow() that throws is caught here
    /// rather than on a phone. The game boots as it does for a player (the editor's save and privacy choices are used).
    /// </summary>
    public class ScreensSmokeTest
    {
        [UnityTest]
        public IEnumerator EveryScreen_BuildsAndShows()
        {
            yield return SceneManager.LoadSceneAsync("Main");
            float until = Time.realtimeSinceStartup + 15f;
            while ((GameApp.I == null || GameApp.I.UI == null) && Time.realtimeSinceStartup < until) yield return null;
            Assert.IsNotNull(GameApp.I, "GameApp did not boot");
            var router = GameApp.I.UI;
            var open = typeof(UIRouter).GetMethods().First(m => m.Name == "Open" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
            var screens = typeof(UIScreen).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(UIScreen)) && !t.IsAbstract).OrderBy(t => t.Name).ToList();
            Assert.Greater(screens.Count, 30, "the screens were not found");
            var failures = new List<string>();
            foreach (var type in screens)
            {
                try { open.MakeGenericMethod(type).Invoke(router, null); }
                catch (System.Exception e) { failures.Add(type.Name + ": " + (e.InnerException ?? e).Message); }
                yield return null; // layouts, coroutines and the first frame of the screen
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }
    }
}
