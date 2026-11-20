using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mmo.Crafting.Blacksmithing.View
{
    /// <summary>
    /// Placeholder front end for the blacksmithing minigame. Everything is drawn with Unity's immediate-mode GUI
    /// (coloured rectangles and text), so there are no sprites, prefabs or packages to set up yet.
    /// The rules live in ForgeMinigame (Core); this class only draws and forwards input.
    ///
    /// Deliberately unguided: no gauges, markers, zones or timers. The player reads the metal colour, hears the beat
    /// and judges the blade against the ghost outline. The results screen explains the outcome.
    /// Controls: SPACE pull from forge / hold to quench / sharpen. A S D (or J K L) strike Tip, Body, Edge. E set hammer down.
    /// R reheat. 1 2 3 pick water, oil, brine. ENTER craft again. ESC back to setup. Mouse works on every button and lane.
    /// </summary>
    public sealed class ForgeView : MonoBehaviour
    {
        private const float W = 1280f, H = 720f;
        private const string SkillPref = "mmo.forge.skill";

        private enum Mode { Setup, Play, Results }

        private Mode _mode = Mode.Setup;
        private int _recipeIndex;
        private readonly ForgeSetup[] _setups = new ForgeSetup[ForgeRecipe.All.Length];
        private int _skill = 1;
        private ForgeMinigame _game;
        private ForgeAudio _audio;
        private int _seed = 1;
        private int _skillBefore;
        private string _lastQuench = "";

        private readonly HashSet<KeyCode> _held = new HashSet<KeyCode>();
        private readonly List<Popup> _popups = new List<Popup>();

        private struct Popup
        {
            public string Text;
            public Color Color;
            public float Born;
            public int Lane; // -1 = centre of screen
        }


        private GUIStyle _style;

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            EnsureCameraAndListener();
            _audio = gameObject.AddComponent<ForgeAudio>();
            _skill = Mathf.Clamp(PlayerPrefs.GetInt(SkillPref, 1), 1, 100);
            for (int i = 0; i < _setups.Length; i++) _setups[i] = new ForgeSetup(ForgeRecipe.All[i]);
            _seed = Random.Range(1, int.MaxValue);
        }

        private static void EnsureCameraAndListener()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.08f, 0.07f, 0.07f);
            if (FindAnyObjectByType<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
        }

        // A key released while the window was unfocused never sends KeyUp; forget held keys so the next press counts.
        private void OnApplicationFocus(bool focused)
        {
            if (!focused) _held.Clear();
        }

        private ForgeSetup CurrentSetup => _setups[_recipeIndex];

        private void StartCraft()
        {
            var setup = CurrentSetup;
            setup.SkillLevel = _skill;
            _game = new ForgeMinigame(setup, _seed++);
            _popups.Clear();
            _lastQuench = "";
            _skillBefore = _skill;
            _mode = Mode.Play;
        }

        private void Update()
        {
            if (_mode != Mode.Play || _game == null) return;
            _game.Update(Time.deltaTime);
            DrainEvents();
            if (_game.Stage == ForgeStage.Done)
            {
                _skill = Mathf.Min(100, _skill + _game.Result.SkillGain);
                PlayerPrefs.SetInt(SkillPref, _skill);
                _mode = Mode.Results;
            }
        }

        private void DrainEvents()
        {
            foreach (var e in _game.Events)
            {
                _audio.Play(e);
                switch (e.Kind)
                {
                    case ForgeEventKind.Cracked:
                    case ForgeEventKind.Burned:
                    case ForgeEventKind.QuenchCrack:
                        AddPopup(e.Message, new Color(1f, 0.35f, 0.3f), -1);
                        break;
                    case ForgeEventKind.QuenchEnd:
                        _lastQuench = e.Message;
                        break;
                }
            }
            _game.Events.Clear();
        }

        private void AddPopup(string text, Color color, int lane) =>
            _popups.Add(new Popup { Text = text, Color = color, Born = Time.time, Lane = lane });

        // ------------------------------------------------------------------ input

        private void HandleInput(Event e)
        {
            if (e.type == EventType.KeyDown && e.keyCode != KeyCode.None)
            {
                bool first = _held.Add(e.keyCode); // ignore key repeat
                if (first && OnKeyDown(e.keyCode)) e.Use();
            }
            else if (e.type == EventType.KeyUp)
            {
                _held.Remove(e.keyCode);
                if (_mode == Mode.Play && e.keyCode == KeyCode.Space) _game.EndQuench();
            }
            else if (e.type == EventType.MouseUp && _mode == Mode.Play)
            {
                _game.EndQuench();
            }
        }

        private bool OnKeyDown(KeyCode key)
        {
            switch (_mode)
            {
                case Mode.Setup:
                    if (key == KeyCode.Return || key == KeyCode.KeypadEnter) { StartCraft(); return true; }
                    return false;
                case Mode.Results:
                    if (key == KeyCode.Return || key == KeyCode.KeypadEnter || key == KeyCode.Space) { StartCraft(); return true; }
                    if (key == KeyCode.Escape) { _mode = Mode.Setup; return true; }
                    return false;
            }

            if (key == KeyCode.Escape) { _mode = Mode.Setup; return true; }
            switch (_game.Stage)
            {
                case ForgeStage.Heat:
                    if (key == KeyCode.Space) { _game.PullFromForge(); return true; }
                    break;
                case ForgeStage.Strike:
                    if (key == KeyCode.A || key == KeyCode.J) { _game.Strike(Lane.Tip); return true; }
                    if (key == KeyCode.S || key == KeyCode.K) { _game.Strike(Lane.Body); return true; }
                    if (key == KeyCode.D || key == KeyCode.L) { _game.Strike(Lane.Edge); return true; }
                    if (key == KeyCode.R) { _game.Reheat(); return true; }
                    if (key == KeyCode.E) { _game.SetDownHammer(); return true; }
                    break;
                case ForgeStage.QuenchChoose:
                    if (key == KeyCode.Alpha1 || key == KeyCode.Keypad1) { _game.ChooseQuench(QuenchMedium.Water); return true; }
                    if (key == KeyCode.Alpha2 || key == KeyCode.Keypad2) { _game.ChooseQuench(QuenchMedium.Oil); return true; }
                    if (key == KeyCode.Alpha3 || key == KeyCode.Keypad3) { _game.ChooseQuench(QuenchMedium.Brine); return true; }
                    break;
                case ForgeStage.QuenchHold:
                    if (key == KeyCode.Space) { _game.BeginQuench(); return true; }
                    break;
                case ForgeStage.Finish:
                    if (key == KeyCode.Space) { _game.FinishStroke(); return true; }
                    break;
            }
            return false;
        }

        /// <summary>True on the mouse press inside r (fires on press, not release, so the beat timing holds).</summary>
        private static bool Pressed(Rect r)
        {
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
            {
                e.Use();
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ drawing

        private void OnGUI()
        {
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
            }

            float scale = Mathf.Min(Screen.width / W, Screen.height / H);
            float ox = (Screen.width - W * scale) / 2f, oy = (Screen.height - H * scale) / 2f;
            GUI.matrix = Matrix4x4.TRS(new Vector3(ox, oy, 0), Quaternion.identity, new Vector3(scale, scale, 1));

            HandleInput(Event.current);
            Fill(new Rect(0, 0, W, H), new Color(0.10f, 0.09f, 0.09f));

            switch (_mode)
            {
                case Mode.Setup: DrawSetup(); break;
                case Mode.Play: DrawPlay(); break;
                case Mode.Results: DrawResults(); break;
            }
        }

        private static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        private static void Outline(Rect r, Color c, float t = 2f)
        {
            Fill(new Rect(r.x, r.y, r.width, t), c);
            Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            Fill(new Rect(r.x, r.y, t, r.height), c);
            Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        private void Text(Rect r, string text, int size, Color color, TextAnchor anchor = TextAnchor.UpperLeft, bool bold = false)
        {
            _style.fontSize = size;
            _style.alignment = anchor;
            _style.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            _style.normal.textColor = color;
            GUI.Label(r, text, _style);
        }

        private bool Button(Rect r, string label, bool selected = false, bool enabled = true)
        {
            Fill(r, !enabled ? new Color(0.18f, 0.17f, 0.17f) : selected ? new Color(0.55f, 0.32f, 0.12f) : new Color(0.24f, 0.22f, 0.21f));
            Outline(r, selected ? new Color(1f, 0.7f, 0.3f) : new Color(0.4f, 0.37f, 0.35f), selected ? 2f : 1f);
            Text(r, label, 16, enabled ? Color.white : new Color(0.5f, 0.5f, 0.5f), TextAnchor.MiddleCenter);
            return enabled && Pressed(r);
        }

        /// <summary>Hot-metal colour for a temperature: dull red, cherry, orange, yellow, white.</summary>
        private static Color HeatColor(double t)
        {
            var stops = new[]
            {
                (0.00f, new Color(0.22f, 0.20f, 0.20f)),
                (0.10f, new Color(0.40f, 0.06f, 0.03f)),
                (0.25f, new Color(0.62f, 0.08f, 0.04f)),
                (0.45f, new Color(0.88f, 0.12f, 0.08f)),
                (0.65f, new Color(1.00f, 0.50f, 0.10f)),
                (0.85f, new Color(1.00f, 0.90f, 0.35f)),
                (1.00f, new Color(1.00f, 1.00f, 0.95f)),
            };
            float x = (float)t;
            if (x <= stops[0].Item1) return stops[0].Item2;
            for (int i = 1; i < stops.Length; i++)
                if (x <= stops[i].Item1)
                    return Color.Lerp(stops[i - 1].Item2, stops[i].Item2, (x - stops[i - 1].Item1) / (stops[i].Item1 - stops[i - 1].Item1));
            return Color.Lerp(Color.white, new Color(0.7f, 0.85f, 1f), Mathf.Clamp01((x - 1f) * 8f)); // burning: blue-white sparks
        }

        private static Color GradeColor(int q)
        {
            if (q >= 81) return new Color(1f, 0.75f, 0.25f);
            if (q >= 61) return new Color(0.45f, 0.75f, 1f);
            if (q >= 41) return new Color(0.85f, 0.85f, 0.85f);
            if (q >= 21) return new Color(0.7f, 0.6f, 0.5f);
            return new Color(0.6f, 0.45f, 0.45f);
        }

        // ------------------------------------------------------------------ setup screen

        private void DrawSetup()
        {
            Text(new Rect(40, 20, 1200, 50), "Forge  <size=20><color=#b9a99a>blacksmithing prototype</color></size>", 36, new Color(1f, 0.75f, 0.4f), bold: true);

            // Recipes
            Text(new Rect(40, 80, 300, 30), "Recipe (iron path, level 1-20)", 18, new Color(0.8f, 0.75f, 0.7f));
            for (int i = 0; i < ForgeRecipe.All.Length; i++)
            {
                var r = ForgeRecipe.All[i];
                if (Button(new Rect(40, 110 + i * 56, 360, 48), $"{r.Name}  <size=13>({r.Rank}, item lvl {r.ItemLevel})</size>", i == _recipeIndex))
                    _recipeIndex = i;
            }

            var setup = CurrentSetup;
            setup.SkillLevel = _skill;
            var recipe = setup.Recipe;
            var steps = "Heat, Strike" + (recipe.HasQuench ? ", Quench" : "") + (recipe.HasFinish ? ", Finish" : "");
            Text(new Rect(40, 290, 360, 150),
                $"{recipe.Description}\n\n<b>Steps:</b> {steps}\n<b>Tempo:</b> {recipe.Bpm:0} BPM\n<b>Needs:</b> {recipe.Rank} ({recipe.RequiredSkill}+)",
                15, new Color(0.85f, 0.82f, 0.78f));

            // Skill
            float x = 460;
            Text(new Rect(x, 80, 700, 30), $"Blacksmithing skill: <b>{_skill}</b> ({ForgeTuning.RankFor(_skill)})", 18, Color.white);
            int newSkill = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x, 115, 500, 20), _skill, 1, 100));
            if (newSkill != _skill) { _skill = newSkill; PlayerPrefs.SetInt(SkillPref, _skill); }
            Text(new Rect(x, 135, 780, 40), "Test slider (in game this is your character's skill; crafting raises it).", 13, new Color(0.65f, 0.62f, 0.6f));
            if (_skill < recipe.RequiredSkill)
                Text(new Rect(x, 160, 780, 24), $"Below this recipe's rank ({recipe.RequiredSkill}). You can still practise it.", 14, new Color(1f, 0.6f, 0.4f));

            // Station
            Text(new Rect(x, 190, 400, 26), "Station", 18, new Color(0.8f, 0.75f, 0.7f));
            for (int i = 0; i < ForgeStation.All.Length; i++)
            {
                var s = ForgeStation.All[i];
                if (Button(new Rect(x + i * 205, 218, 195, 36), s.Name, setup.Station == s)) setup.Station = s;
            }
            Text(new Rect(x, 256, 780, 22), setup.Station.Description, 13, new Color(0.65f, 0.62f, 0.6f));

            // Hammer
            Text(new Rect(x, 282, 400, 26), "Hammer", 18, new Color(0.8f, 0.75f, 0.7f));
            for (int i = 0; i < Hammer.All.Length; i++)
            {
                var h = Hammer.All[i];
                if (Button(new Rect(x + i * 205, 310, 195, 36), h.Name, setup.Hammer == h)) setup.Hammer = h;
            }
            Text(new Rect(x, 348, 780, 22), setup.Hammer.Description, 13, new Color(0.65f, 0.62f, 0.6f));

            // Inputs
            Text(new Rect(x, 376, 600, 26), "Input materials (quality from the miner / hunter)", 18, new Color(0.8f, 0.75f, 0.7f));
            for (int i = 0; i < recipe.Inputs.Count; i++)
            {
                var input = recipe.Inputs[i];
                float y = 408 + i * 40;
                int q = setup.InputQualities[i];
                Text(new Rect(x, y, 330, 30), $"{input.Count}× {input.Name} <size=12>({input.Weight:P0})</size>", 15, Color.white);
                setup.InputQualities[i] = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x + 340, y + 8, 300, 20), q, 1, 100));
                Text(new Rect(x + 655, y, 140, 30), $"{ForgeTuning.GradeWord(q)} {q}", 15, GradeColor(q));
            }

            if (Button(new Rect(x, 630, 300, 56), "<b>Start forging</b>  <size=13>(Enter)</size>", true)) StartCraft();
            Text(new Rect(x + 320, 630, 470, 60),
                "Controls: SPACE pull from the fire, hold to quench, sharpen · A S D strike tip, body, edge on the beat · R reheat · E set the hammer down · 1 2 3 water, oil, brine · ESC leave. Mouse: click the forge, the blade parts, the bucket, the edge.",
                13, new Color(0.65f, 0.62f, 0.6f));
        }

        // ------------------------------------------------------------------ play screen
        // No prompts, gauges, markers or zones: the player reads the metal, hears the beat and judges the shape.
        // The results screen explains what happened afterwards.

        private static readonly Rect AnvilRect = new Rect(560, 380, 520, 80);
        private Rect[] _partRects = new Rect[3];

        private void DrawPlay()
        {
            var g = _game;
            Text(new Rect(30, 14, 900, 40), $"<b>{g.Recipe.Name}</b>", 26, new Color(1f, 0.8f, 0.5f));
            Text(new Rect(1080, 20, 180, 24), "Esc: leave", 13, new Color(0.5f, 0.47f, 0.45f), TextAnchor.UpperRight);

            DrawForge(g);
            DrawAnvil(g);

            switch (g.Stage)
            {
                case ForgeStage.Strike:
                    for (int lane = 0; lane < 3; lane++)
                        if (Pressed(_partRects[lane])) g.Strike((Lane)lane);
                    break;
                case ForgeStage.QuenchChoose:
                case ForgeStage.QuenchHold:
                    DrawQuench(g);
                    break;
                case ForgeStage.Finish:
                    DrawFinish(g);
                    break;
            }

            DrawPopups();
        }

        private void DrawForge(ForgeMinigame g)
        {
            var forge = new Rect(60, 150, 300, 300);
            Fill(forge, new Color(0.18f, 0.15f, 0.14f));
            Outline(forge, new Color(0.35f, 0.3f, 0.28f), 3);
            var mouth = new Rect(forge.x + 30, forge.y + 90, forge.width - 60, 150);
            Fill(mouth, new Color(0.05f, 0.03f, 0.03f));
            float flicker = 0.75f + 0.25f * Mathf.PerlinNoise(Time.time * 4f, 0.3f);
            for (int i = 0; i < 8; i++)
            {
                float h = (40 + 60 * Mathf.PerlinNoise(Time.time * 3f + i, i)) * flicker;
                Fill(new Rect(mouth.x + 6 + i * 30, mouth.yMax - h, 24, h), new Color(1f, 0.3f + 0.06f * i, 0.05f, 0.85f));
            }
            if (g.Stage != ForgeStage.Heat) return;

            // The bar in the fire. Its colour is the only heat reading there is.
            var bar = new Rect(mouth.x + 20, mouth.y + 70, mouth.width + 140, 18);
            Fill(bar, HeatColor(g.Temperature));
            if (g.Temperature > 1.0) DrawSparks(bar);
            if (Pressed(forge) || Pressed(bar)) g.PullFromForge();
        }

        private static void DrawSparks(Rect around)
        {
            for (int i = 0; i < 14; i++)
            {
                float a = Mathf.PerlinNoise(Time.time * 9f + i * 3.1f, i) , b = Mathf.PerlinNoise(i, Time.time * 9f + i);
                Fill(new Rect(around.x + a * around.width, around.y - 30 + b * (around.height + 60), 3, 3), new Color(1f, 1f, 0.8f));
            }
        }

        private void DrawAnvil(ForgeMinigame g)
        {
            var a = AnvilRect;
            // A subtle swell on the beat, like the ring of the anvil. No markers or countdown.
            float pulse = 0f;
            if (g.Stage == ForgeStage.Strike && g.SongTime >= -g.BeatInterval * 0.5 && g.SongTime <= g.TrackEnd + g.BeatInterval * 0.5)
            {
                double phase = g.SongTime / g.BeatInterval - System.Math.Floor(g.SongTime / g.BeatInterval + 0.5);
                pulse = Mathf.Clamp01(1f - Mathf.Abs((float)phase) * 4f) * 0.06f;
            }
            Fill(a, new Color(0.28f + pulse, 0.29f + pulse, 0.31f + pulse));
            Fill(new Rect(a.x + 130, a.yMax, 260, 50), new Color(0.22f, 0.23f, 0.25f));
            Fill(new Rect(a.x + 70, a.yMax + 50, 380, 20), new Color(0.2f, 0.21f, 0.22f));

            if (g.Stage == ForgeStage.Heat) return; // the bar is in the forge
            DrawBlade(g, a.x + 10, a.y - 6);
        }

        /// <summary>Ghost outline of the target shape, plus the metal as it is now. Click a part (or A S D) to strike it.</summary>
        private void DrawBlade(ForgeMinigame g, float x, float baseY)
        {
            bool hot = g.Stage == ForgeStage.Strike;
            Color metal = hot ? HeatColor(g.Temperature) : new Color(0.72f, 0.74f, 0.78f);
            Color ghost = new Color(1f, 1f, 1f, 0.22f);

            float P(int lane) => Mathf.Min(1.6f, (float)(g.Progress[lane] / g.Target[lane]));
            float tip = P(0), body = P(1), edge = P(2);

            // Grip and guard (not forged here).
            Fill(new Rect(x, baseY - 26, 60, 14), new Color(0.4f, 0.26f, 0.15f));
            Fill(new Rect(x + 60, baseY - 44, 14, 50), new Color(0.45f, 0.45f, 0.48f));

            float bodyX = x + 74, bodyLen = 300, bodyH = 22;
            Outline(new Rect(bodyX, baseY - 30, bodyLen, bodyH + 22), ghost, 1);
            for (int i = 0; i < 6; i++) Outline(new Rect(bodyX + bodyLen + i * 14, baseY - 28 + i * 2.6f, 14, bodyH + 2 - i * 5.2f), ghost, 1);

            // Body: unworked metal is short and thick; worked metal is long and thin; overworked runs long and thin.
            float len = bodyLen * Mathf.Lerp(0.55f, 1f, Mathf.Min(1, body)) * (body > 1f ? 1 + (body - 1) * 0.5f : 1);
            float thick = bodyH * Mathf.Lerp(1.6f, 1f, Mathf.Min(1, body)) * (body > 1f ? 1 - (body - 1) * 0.6f : 1);
            Fill(new Rect(bodyX, baseY - 8 - thick, len, thick), metal);
            // Edge: a bevel is drawn out along the bottom; overworked it goes thin and ragged.
            float bevel = 6 * Mathf.Min(1, edge) * (edge > 1f ? 1 - (edge - 1) * 0.8f : 1);
            if (edge > 0) Fill(new Rect(bodyX, baseY - 8, len, Mathf.Max(1, bevel)), Color.Lerp(metal, Color.white, 0.35f));
            // Tip: tapers as it is worked; overworked it turns into a needle.
            float tipEnd = bodyX + len;
            int tipSteps = Mathf.RoundToInt(6 * Mathf.Min(1, tip)) + (tip > 1f ? Mathf.RoundToInt((tip - 1) * 8) : 0);
            for (int i = 0; i < tipSteps; i++)
            {
                float hgt = Mathf.Max(2, thick - i * thick * 0.16f);
                Fill(new Rect(tipEnd + i * 14, baseY - 8 - thick + (thick - hgt) / 2, 14, hgt), metal);
            }

            // Click areas for the three parts (mouse play).
            _partRects[(int)Lane.Body] = new Rect(bodyX, baseY - 50, bodyLen * 0.85f, 36);
            _partRects[(int)Lane.Edge] = new Rect(bodyX, baseY - 14, bodyLen * 0.85f, 26);
            _partRects[(int)Lane.Tip] = new Rect(bodyX + bodyLen * 0.85f, baseY - 50, 140, 62);
        }

        private void DrawQuench(ForgeMinigame g)
        {
            var area = new Rect(360, 520, 860, 170);
            if (g.Stage == ForgeStage.QuenchChoose)
            {
                var media = new[] { QuenchMedium.Water, QuenchMedium.Oil, QuenchMedium.Brine };
                for (int i = 0; i < 3; i++)
                {
                    var r = new Rect(area.x + i * 290, area.y, 270, 60);
                    if (Button(r, $"{i + 1}  {media[i]}")) g.ChooseQuench(media[i]);
                    Text(new Rect(r.x, r.yMax + 6, r.width, 60), ForgeTuning.QuenchDescription(media[i]), 13, new Color(0.75f, 0.72f, 0.7f));
                }
                return;
            }

            // The bucket. No timer and no zone: count it yourself.
            var bucket = new Rect(area.x + area.width / 2 - 120, area.y, 240, 140);
            Fill(bucket, g.Medium == QuenchMedium.Oil ? new Color(0.35f, 0.3f, 0.15f) : new Color(0.22f, 0.3f, 0.38f));
            Outline(bucket, new Color(0.5f, 0.5f, 0.55f));
            if (g.QuenchHeld)
            {
                for (int i = 0; i < 10; i++)
                {
                    float n = Mathf.PerlinNoise(Time.time * 5f + i, i);
                    float h = 20 + 60 * n;
                    Fill(new Rect(bucket.x + 10 + i * 22, bucket.y - h, 14, h), new Color(0.9f, 0.9f, 0.95f, 0.25f));
                }
            }
            if (Pressed(bucket)) g.BeginQuench();
        }

        private void DrawFinish(ForgeMinigame g)
        {
            // The blade edge with one dull spot on it. Stroke the stone across the dull spot.
            var edge = new Rect(360, 560, 820, 26);
            Fill(edge, new Color(0.62f, 0.64f, 0.68f));
            float X(double v) => edge.x + (float)v * edge.width;
            Fill(new Rect(X(g.FinishSweetCenter) - 6, edge.y + 4, 12, edge.height - 8), new Color(0.42f, 0.42f, 0.44f));
            Fill(new Rect(X(g.FinishCursor) - 18, edge.y - 16, 36, edge.height + 32), new Color(0.35f, 0.3f, 0.26f, 0.85f));
            if (Pressed(new Rect(edge.x, edge.y - 30, edge.width, edge.height + 60))) g.FinishStroke();
        }

        private void DrawPopups()
        {
            const float life = 1.8f;
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                var p = _popups[i];
                float age = Time.time - p.Born;
                if (age > life) { _popups.RemoveAt(i); continue; }
                var c = p.Color;
                c.a = 1f - age / life;
                Text(new Rect(260, 300 - age * 20, 900, 34), $"<b>{p.Text}</b>", 22, c, TextAnchor.MiddleCenter);
            }
        }

        // ------------------------------------------------------------------ results screen

        private void DrawResults()
        {
            var r = _game.Result;
            var gc = GradeColor(r.Quality);
            Text(new Rect(40, 30, 1200, 60), $"<b>{r.ItemName}</b>", 40, gc);
            Text(new Rect(40, 84, 1200, 30), $"Quality <b>{r.Quality}</b>  ·  item level {r.ItemLevel}" + (r.Botch ? "  ·  <color=#ff8a70>Botched (still usable and scrappable, no materials lost)</color>" : ""), 20, Color.white);

            string traits = r.Traits.Count > 0 ? string.Join(", ", r.Traits) : "none";
            string flaws = r.Flaws.Count > 0 ? string.Join(", ", r.Flaws) : "none";
            string body =
                $"<b>Score</b>  S = shape {r.Shape:0.00} × accuracy {r.Accuracy:0.00} × heat control {r.HeatControl:0.00} = <b>{r.Score:0.00}</b>\n" +
                $"<size=14><color=#b9a99a>heat control = pulls {r.HeatPullAverage:0.00}" +
                (_game.Recipe.HasQuench ? $" × quench {r.QuenchScore:0.00}" : "") +
                (r.Reheats > ForgeTuning.FreeReheats ? $" × {r.Reheats - ForgeTuning.FreeReheats} extra reheat(s)" : "") + "</color></size>\n\n" +
                $"<b>Quality</b>  floor {r.Floor} + (ceiling {r.Ceiling} − {r.Floor}) × clamp({r.Score:0.00} {(r.Roll >= 0 ? "+" : "−")} {System.Math.Abs(r.Roll):0.00}) = <b>{(r.Botch ? "botch" : r.Quality.ToString())}</b>\n" +
                $"<size=14><color=#b9a99a>ceiling set by {r.CeilingReason}; inputs average {r.InputMeanQuality}</color></size>\n\n" +
                $"<b>Shape</b>  tip {_game.Progress[0]:0.#}/{_game.Target[0]} · body {_game.Progress[1]:0.#}/{_game.Target[1]} · edge {_game.Progress[2]:0.#}/{_game.Target[2]}\n" +
                $"<b>Strikes</b>  perfect {r.Perfect} · good {r.Good} · off-beat {r.OffBeat} · cold {r.Cold} · beats unused {r.BeatsUnused} · reheats {r.Reheats}\n" +
                (_game.Recipe.HasQuench ? $"<b>Quench</b>  {_game.Medium}, held {_game.QuenchSeconds:0.0} s (ideal {ForgeTuning.QuenchTargetSeconds(_game.Medium):0.0} s): {_lastQuench}\n" : "") +
                (_game.Recipe.HasFinish ? $"<b>Finish</b>  {r.FinishScore:0.00}  ·  traits: {traits}\n" : "") +
                $"<b>Flaws</b>  {flaws}\n\n" +
                $"<b>Skill</b>  {_skillBefore} → {_skill}" + (r.SkillGain == 0 ? "  <size=14><color=#b9a99a>(no gain: botched, or this recipe is too easy for you)</color></size>" : "");
            Text(new Rect(40, 130, 1200, 460), body, 18, new Color(0.9f, 0.88f, 0.85f));

            if (Button(new Rect(40, 620, 260, 56), "<b>Craft again</b> (Enter)", true)) StartCraft();
            if (Button(new Rect(320, 620, 260, 56), "Change setup (Esc)")) _mode = Mode.Setup;
        }
    }

    /// <summary>
    /// Safety net: if the Blacksmithing scene opens without its Forge object (for example a broken script link),
    /// add one so Play still works.
    /// </summary>
    internal static class ForgeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureForge()
        {
            if (SceneManager.GetActiveScene().name != "Blacksmithing") return;
            if (Object.FindAnyObjectByType<ForgeView>() != null) return;
            new GameObject("Forge").AddComponent<ForgeView>();
        }
    }
}
