using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace MummyEscape.EditorTools
{
    /// <summary>
    /// Finds player-facing French texts that have no translation. Scans the string literals of the game scripts
    /// (constant concatenations "a" + "b" are joined, like the compiler does), keeps those that look like sentences
    /// (a space or an accent), and checks them against every translation table. Also lists interpolated strings
    /// ($"…") that should go through Loc.F, and table entries no longer used.
    /// </summary>
    public static class LocCheck
    {
        static readonly string[] Roots = { "Runtime", "Online.UGS" };
        // Their texts are not keys: legal documents have per-language copies, the tables are the translations.
        static readonly string[] SkipFiles = { "LegalTexts.cs", "LegalTexts.En.cs", "Loc.En.cs", "Loc.cs", "MusicComposer.cs" };
        static readonly string[] SkipLineMarkers = { "Debug.Log", "Shader.Find", "///", "[CliCommand", "PlayerPrefs.", "Resources.Load", "AndroidJava", "CallStatic", "throw new", "Rect(\"", "GameObject(\"", "Find(", "nameof(", "AddComponent",
            "Child<", "Image(", "Panel(parent, \"", "Create(\"", "string name =", "[Tooltip", "[Header(", "noloc" };
        // A capitalised word ("Jouer"), several words, or an accent; identifiers (ids, paths, keys) are left out.
        static readonly Regex Sentence = new Regex(@"^[A-ZÀ-Ý«]|[a-zà-ÿ].*\s.*[a-zà-ÿ]|[À-ÿ]", RegexOptions.Compiled);
        static readonly Regex Words = new Regex(@"[a-zà-ÿ]{2,}[\s,'][a-zà-ÿ]", RegexOptions.Compiled); // real words around the holes
        static readonly Regex Code = new Regex(@"^[A-Z]{1,3}$", RegexOptions.Compiled); // country codes, "OK", "II"
        static readonly Regex Identifier = new Regex(@"^[a-z0-9_./#:-]*$|^[A-Za-z0-9]*[_./#:-][A-Za-z0-9_./#:-]*$", RegexOptions.Compiled);

        public struct Found { public string Text, Where; }

        [MenuItem("Mummy Escape/Localization/Check missing translations")]
        public static void Menu() => Debug.Log(Report());

        public static string Report()
        {
            var keys = new List<Found>();
            var interpolations = new List<Found>();
            string scripts = Path.Combine(Application.dataPath, "_Project", "Scripts");
            foreach (var root in Roots)
                foreach (var file in Directory.GetFiles(Path.Combine(scripts, root), "*.cs", SearchOption.AllDirectories))
                    if (!SkipFiles.Contains(Path.GetFileName(file))) Scan(file, scripts, keys, interpolations);
            ScanCore(Path.Combine(scripts, "Core"), scripts, keys);

            var sb = new StringBuilder();
            var used = new HashSet<string>(keys.Select(k => k.Text));
            foreach (var lang in Loc.Languages)
            {
                if (lang.Table == null) continue;
                var missing = keys.Where(k => !lang.Table.ContainsKey(k.Text)).GroupBy(k => k.Text).ToList();
                sb.Append($"[{lang.Code}] {missing.Count} missing translation(s) out of {used.Count} texts\n");
                foreach (var g in missing) sb.Append($"  {g.First().Where}  \"{Escape(g.Key)}\"\n");
                var unused = lang.Table.Keys.Where(k => !used.Contains(k)).ToList();
                if (unused.Count > 0)
                {
                    sb.Append($"[{lang.Code}] {unused.Count} entr(y/ies) not found in the code (dynamic keys are fine):\n");
                    foreach (var k in unused) sb.Append($"  \"{Escape(k)}\"\n");
                }
            }
            sb.Append($"{interpolations.Count} interpolated string(s) with words (wrap them in Loc.F):\n");
            foreach (var i in interpolations) sb.Append($"  {i.Where}  $\"{Escape(i.Text)}\"\n");
            return sb.ToString();
        }

        static string Escape(string s) => s.Replace("\n", "\\n");

        /// <summary>Core cannot see Loc: only its CoreText calls and the act names are texts.</summary>
        static void ScanCore(string dir, string scripts, List<Found> keys)
        {
            var call = new Regex(@"CoreText\.[TF]\(\s*""((?:[^""\\]|\\.)*)""");
            var name = new Regex(@"\bName\s*=\s*""((?:[^""\\]|\\.)*)""");
            foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length; i++)
                    foreach (var re in new[] { call, name })
                        foreach (Match m in re.Matches(lines[i]))
                            keys.Add(new Found { Text = Regex.Unescape(m.Groups[1].Value), Where = Where(file, scripts, i) });
            }
        }

        static string Where(string file, string scripts, int line) =>
            file.Substring(scripts.Length + 1).Replace('\\', '/') + ":" + (line + 1);

        /// <summary>Minimal C# lexer: comments, verbatim, interpolated and regular strings, chars.</summary>
        static void Scan(string file, string scripts, List<Found> keys, List<Found> interpolations)
        {
            string src = File.ReadAllText(file);
            var lineStarts = new List<int> { 0 };
            for (int i = 0; i < src.Length; i++) if (src[i] == '\n') lineStarts.Add(i + 1);
            int LineOf(int pos) { int l = lineStarts.BinarySearch(pos); return l >= 0 ? l : ~l - 1; }
            string LineText(int l) { int s = lineStarts[l], e = l + 1 < lineStarts.Count ? lineStarts[l + 1] : src.Length; return src.Substring(s, e - s); }
            bool Skipped(int pos) { string t = LineText(LineOf(pos)); return SkipLineMarkers.Any(t.Contains); }

            var literals = new List<(int start, int end, string text, bool interpolated)>();
            int p = 0;
            while (p < src.Length)
            {
                char c = src[p];
                if (c == '/' && p + 1 < src.Length && src[p + 1] == '/') { while (p < src.Length && src[p] != '\n') p++; continue; }
                if (c == '/' && p + 1 < src.Length && src[p + 1] == '*') { int e = src.IndexOf("*/", p + 2); p = e < 0 ? src.Length : e + 2; continue; }
                if (c == '\'') { p++; while (p < src.Length && src[p] != '\'') p += src[p] == '\\' ? 2 : 1; p++; continue; }
                if (c == '"' || ((c == '$' || c == '@') && p + 1 < src.Length && (src[p + 1] == '"' || src[p + 1] == '$' || src[p + 1] == '@')))
                {
                    int start = p;
                    bool interp = false, verbatim = false;
                    while (src[p] != '"') { if (src[p] == '$') interp = true; if (src[p] == '@') verbatim = true; p++; }
                    p++;
                    var sb = new StringBuilder();
                    int depth = 0;
                    while (p < src.Length)
                    {
                        char d = src[p];
                        if (interp && d == '{') { if (p + 1 < src.Length && src[p + 1] == '{') { sb.Append('{'); p += 2; continue; } depth++; sb.Append('{'); p++; continue; }
                        if (interp && d == '}' && depth > 0) { depth--; sb.Append('}'); p++; continue; }
                        if (depth > 0 && d == '"') { p++; while (p < src.Length && src[p] != '"') p += src[p] == '\\' ? 2 : 1; p++; continue; }
                        if (!verbatim && d == '\\') { sb.Append(d).Append(src[p + 1]); p += 2; continue; }
                        if (d == '"') { if (verbatim && p + 1 < src.Length && src[p + 1] == '"') { sb.Append('"'); p += 2; continue; } break; }
                        sb.Append(d);
                        p++;
                    }
                    p++;
                    string text = verbatim ? sb.ToString() : Unescape(sb.ToString());
                    literals.Add((start, p, text, interp));
                    continue;
                }
                p++;
            }

            // Join constant concatenations: "a" + "b" (only whitespace and + between them).
            for (int i = 0; i < literals.Count; i++)
            {
                var (start, end, text, interp) = literals[i];
                while (!interp && i + 1 < literals.Count && !literals[i + 1].interpolated &&
                       src.Substring(end, literals[i + 1].start - end).Trim() == "+")
                {
                    i++;
                    text += literals[i].text;
                    end = literals[i].end;
                }
                if (!Sentence.IsMatch(text) || Identifier.IsMatch(text) || Code.IsMatch(text) || Skipped(start) || text.Contains("\"") || text.Contains("://")) continue;
                var found = new Found { Text = text, Where = Where(file, scripts, LineOf(start)) };
                if (interp) { if (Words.IsMatch(Regex.Replace(text, @"\{[^}]*\}|<[^>]*>", "|"))) interpolations.Add(found); }
                else keys.Add(found);
            }
        }

        static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
                char n = s[++i];
                switch (n)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case '0': sb.Append('\0'); break;
                    case 'u': sb.Append((char)System.Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; break;
                    default: sb.Append(n); break;
                }
            }
            return sb.ToString();
        }
    }
}
