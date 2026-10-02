using System.Collections.Generic;
using MummyEscape.Core;
using UnityEditor;
using UnityEngine;

namespace MummyEscape.EditorTools
{
    /// <summary>Design tool: generate any level, see its spec, par, optimal path and map, floor by floor.</summary>
    public sealed class LevelPreviewWindow : EditorWindow
    {
        int _act = 1, _index = 1, _variant;
        Level _level;
        HashSet<Cell> _path = new HashSet<Cell>();
        Vector2 _scroll;
        string _error;

        [MenuItem("Mummy Escape/Level Preview", priority = 20)]
        static void Open() => GetWindow<LevelPreviewWindow>("Level Preview");

        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _act = EditorGUILayout.IntSlider("Acte", _act, 1, DifficultyTable.ActCount);
                _index = EditorGUILayout.IntSlider("Niveau", _index, 1, DifficultyTable.GetAct(_act).Levels);
                _variant = Mathf.Max(0, EditorGUILayout.IntField("Labyrinthe n°", _variant));
                if (GUILayout.Button("Générer", GUILayout.Width(90))) Generate();
            }

            if (_error != null) EditorGUILayout.HelpBox(_error, MessageType.Error);
            if (_level == null) return;

            var sol = _level.Solution;
            EditorGUILayout.HelpBox(
                $"{_level.Spec}\nPar : {sol.Moves} coups · {sol.Interactions} interactions ({sol.ButtonsPressed} boutons, {sol.Disarms} désamorçages) · PV restants {sol.HpLeft}\n" +
                $"Solution : {string.Join(" ", sol.Actions)}", MessageType.Info);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            const float s = 14f;
            for (int f = _level.Floors - 1; f >= 0; f--)
            {
                GUILayout.Label($"Étage {f + 1}", EditorStyles.boldLabel);
                var rect = GUILayoutUtility.GetRect(_level.Width * s, _level.Height * s, GUILayout.ExpandWidth(false));
                for (int y = 0; y < _level.Height; y++)
                    for (int x = 0; x < _level.Width; x++)
                    {
                        var c = new Cell(f, x, y);
                        var r = new Rect(rect.x + x * s, rect.y + (_level.Height - 1 - y) * s, s - 1, s - 1);
                        EditorGUI.DrawRect(r, ColorFor(c));
                    }
            }
            EditorGUILayout.EndScrollView();
        }

        void Generate()
        {
            _error = null;
            try
            {
                _level = LevelGenerator.Generate(new LevelId(_act, _index), _variant);
                _path.Clear();
                var st = Rules.Initial(_level);
                foreach (var a in _level.Solution.Actions)
                {
                    var r = Rules.Step(_level, st, a);
                    _path.Add(r.SteppedOn);
                    st = r.State;
                }
            }
            catch (System.Exception e)
            {
                _level = null;
                _error = e.Message;
            }
        }

        Color ColorFor(Cell c)
        {
            if (c == _level.Start) return Color.green;
            var t = _level[c];
            switch (t.Type)
            {
                case TileType.Wall: return new Color(0.25f, 0.2f, 0.15f);
                case TileType.Exit: return new Color(1f, 0.85f, 0.2f);
                case TileType.Door: return new Color(0.85f, 0.25f, 0.2f);
                case TileType.Button: return new Color(0.2f, 0.4f, 1f);
                case TileType.Trap: return t.Trap == TrapKind.Spikes ? new Color(0.7f, 0.7f, 0.75f) : new Color(0.5f, 0.2f, 0.7f);
                case TileType.Teleporter: return t.Teleporter == TeleporterKind.Cursed ? new Color(0.5f, 0.9f, 0.2f) : new Color(0.2f, 0.9f, 0.9f);
                case TileType.BreakableFloor: return new Color(0.45f, 0.3f, 0.15f);
                case TileType.LadderUp:
                case TileType.LadderDown: return new Color(0.6f, 0.4f, 0.2f);
                case TileType.Dust: return new Color(0.6f, 0.58f, 0.55f);
                case TileType.WallTorch: return new Color(1f, 0.55f, 0.15f);
            }
            return _path.Contains(c) ? new Color(0.95f, 0.8f, 0.55f) : new Color(0.75f, 0.62f, 0.42f);
        }
    }
}
