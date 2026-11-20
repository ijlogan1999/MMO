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
    /// Controls: SPACE pull from forge / hold to quench / sharpen. A S D (or J K L) strike Tip, Body, Edge.
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
        private string _lastPull = "";
        private string _lastQuench = "";
        private Vector2 _scroll;

        private readonly HashSet<KeyCode> _held = new HashSet<KeyCode>();
        private readonly List<Popup> _popups = new List<Popup>();

        private struct Popup
        {
            public string Text;
            public Color Color;
            public float Born;
            public int Lane; // -1 = centre of screen
        }

        // Layout of the strike track.
        private const float LaneX = 300f, LaneRight = 1240f, HammerX = 420f, PixelsPerSecond = 320f;
        private static readonly float[] LaneY = { 392f, 452f, 512f };
        private const float LaneH = 50f;
        private static readonly string[] LaneNames = { "Tip", "Body", "Edge" };
        private static readonly string[] LaneKeys = { "A", "S", "D" };
        private static readonly Color[] LaneColors =
        {
            new Color(0.55f, 0.80f, 1.00f),
            new Color(1.00f, 0.80f, 0.35f),
            new Color(0.70f, 1.00f, 0.60f),
        };

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
            _lastPull = _lastQuench = "";
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
                    case ForgeEventKind.Strike:
                        AddPopup(e.Message, e.Judgement == Judgement.Perfect ? new Color(1f, 0.9f, 0.3f) : new Color(0.7f, 1f, 0.7f), (int)e.Lane);
                        break;
                    case ForgeEventKind.Miss:
                        AddPopup("Miss", new Color(0.7f, 0.7f, 0.7f), (int)e.Lane);
                        break;
                    case ForgeEventKind.OffBeat:
                        AddPopup("Off-beat", new Color(1f, 0.5f, 0.4f), (int)e.Lane);
                        break;
                    case ForgeEventKind.ColdStrike:
                        AddPopup("Too cold", new Color(0.6f, 0.7f, 1f), (int)e.Lane);
                        break;
                    case ForgeEventKind.Pulled:
                        _lastPull = e.Message;
                        AddPopup(e.Message, Color.white, -1);
                        break;
                    case ForgeEventKind.Cracked:
                    case ForgeEventKind.Burned:
                    case ForgeEventKind.QuenchCrack:
                        AddPopup(e.Message, new Color(1f, 0.35f, 0.3f), -1);
                        break;
                    case ForgeEventKind.QuenchEnd:
                        _lastQuench = e.Message;
                        AddPopup(e.Message, Color.white, -1);
                        break;
                    case ForgeEventKind.FinishStroke:
                        AddPopup(e.Message, Color.white, -1);
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
                $"{recipe.Description}\n\n<b>Steps:</b> {steps}\n<b>Strikes:</b> {recipe.TotalStrikes} at {recipe.Bpm:0} BPM\n<b>Needs:</b> {recipe.Rank} ({recipe.RequiredSkill}+)",
                15, new Color(0.85f, 0.82f, 0.78f));

            // Skill
            float x = 460;
            Text(new Rect(x, 80, 700, 30), $"Blacksmithing skill: <b>{_skill}</b> ({ForgeTuning.RankFor(_skill)})", 18, Color.white);
            int newSkill = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x, 115, 500, 20), _skill, 1, 100));
            if (newSkill != _skill) { _skill = newSkill; PlayerPrefs.SetInt(SkillPref, _skill); }
            var pw = ForgeTuning.PerfectWindow(_skill, setup.Hammer) * 1000;
            Text(new Rect(x, 135, 780, 40),
                $"Test slider (in game this is your character's skill; crafting raises it). Perfect window ±{pw:0} ms, quality floor {ForgeTuning.SkillFloor(_skill)}.",
                13, new Color(0.65f, 0.62f, 0.6f));
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

            // Quality range preview
            int mean = ForgeMinigame.ComputeCaps(setup, out int floor, out int ceiling, out string reason);
            float py = 540;
            Fill(new Rect(x, py, 780, 72), new Color(0.15f, 0.14f, 0.13f));
            Text(new Rect(x + 14, py + 8, 760, 60),
                $"Quality range <b>{floor} – {ceiling}</b>   (inputs average {mean}; ceiling set by <b>{reason}</b>)\n" +
                "<size=13>Q = floor + (ceiling − floor) × your score, ±0.1 roll. A score under 0.15 is a botch (Q 1–10).</size>",
                16, Color.white);

            if (Button(new Rect(x, 630, 300, 56), "<b>Start forging</b>  <size=13>(Enter)</size>", true)) StartCraft();
            Text(new Rect(x + 320, 630, 470, 60),
                "Controls: SPACE pull / quench / sharpen · A S D strike tip, body, edge · R reheat · 1 2 3 quench oil · ESC back",
                13, new Color(0.65f, 0.62f, 0.6f));
        }

        // ------------------------------------------------------------------ play screen

        private void DrawPlay()
        {
            var g = _game;
            var recipe = g.Recipe;

            Text(new Rect(30, 14, 900, 40), $"<b>{recipe.Name}</b>  <size=16><color=#b9a99a>{recipe.Rank} · skill {g.Skill} · {g.Setup.Station.Name} · {g.Setup.Hammer.Name}</color></size>", 26, new Color(1f, 0.8f, 0.5f));
            Text(new Rect(30, 50, 1220, 34), Instruction(g), 20, Color.white);
            if (Button(new Rect(1130, 16, 120, 32), "Back (Esc)")) { _mode = Mode.Setup; return; }

            DrawHeatGauge(g);
            DrawForgeAndAnvil(g);

            switch (g.Stage)
            {
                case ForgeStage.Heat:
                case ForgeStage.Strike:
                    DrawTrack(g);
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

            // Footer
            string reheatNote = g.Reheats <= ForgeTuning.FreeReheats ? $"{ForgeTuning.FreeReheats - g.Reheats} free reheats left" : "each reheat now costs quality";
            Text(new Rect(30, 680, 1220, 30),
                $"Reheats: {g.Reheats} ({reheatNote})    {_lastPull}    {_lastQuench}", 14, new Color(0.7f, 0.66f, 0.62f));
        }

        private static string Instruction(ForgeMinigame g)
        {
            switch (g.Stage)
            {
                case ForgeStage.Heat:
                    return g.IsReheating
                        ? "Reheating. Press <b>SPACE</b> (or click the forge) to pull it back out at the target colour."
                        : $"Heat the bar. Press <b>SPACE</b> (or click the forge) when it reaches <b>{ForgeTuning.BandFor(g.Recipe.TargetHeat)}</b>. Too cold cracks it, too hot burns it.";
                case ForgeStage.Strike:
                    return g.CanStrike
                        ? "Strike on the beat: <b>A</b> tip, <b>S</b> body, <b>D</b> edge when a marker reaches the hammer line. <b>R</b> reheats."
                        : "<color=#9fb6ff><b>Too cold!</b> Strikes don't count. Press <b>R</b> to reheat.</color>";
                case ForgeStage.QuenchChoose:
                    return "Quench: press <b>1</b> water, <b>2</b> oil or <b>3</b> brine.";
                case ForgeStage.QuenchHold:
                    return "Hold <b>SPACE</b> (or hold the mouse on the bucket) and let go inside the green zone.";
                case ForgeStage.Finish:
                    return $"Sharpen: press <b>SPACE</b> when the stone is in the bright zone ({g.FinishStrokesDone + 1}/{ForgeTuning.FinishStrokes}).";
            }
            return "";
        }

        private void DrawHeatGauge(ForgeMinigame g)
        {
            var r = new Rect(60, 110, 46, 460);
            Text(new Rect(30, 86, 120, 24), "Heat", 16, new Color(0.8f, 0.75f, 0.7f));
            // Gradient, 0 at the bottom, 1.1 at the top.
            const int steps = 46;
            for (int i = 0; i < steps; i++)
            {
                float t = (i + 0.5f) / steps * 1.1f;
                float y = r.yMax - (i + 1) * r.height / steps;
                Fill(new Rect(r.x, y, r.width, r.height / steps + 1), HeatColor(t));
            }
            Outline(r, new Color(0.45f, 0.4f, 0.38f));

            float Y(double t) => r.yMax - (float)(t / 1.1) * r.height;

            // Target band and tolerance.
            double tol = ForgeTuning.HeatTolerance(g.Skill);
            float yTop = Y(g.Recipe.TargetHeat + tol), yBot = Y(g.Recipe.TargetHeat - tol);
            Outline(new Rect(r.x - 6, yTop, r.width + 12, yBot - yTop), new Color(0.3f, 1f, 0.4f), 3);
            Text(new Rect(r.xMax + 8, Y(g.Recipe.TargetHeat) - 10, 120, 20), "target", 13, new Color(0.4f, 1f, 0.5f));

            // Work minimum.
            Fill(new Rect(r.x - 8, Y(ForgeTuning.WorkMin) - 1, r.width + 16, 2), new Color(0.6f, 0.7f, 1f));
            Text(new Rect(r.xMax + 8, Y(ForgeTuning.WorkMin) - 10, 140, 20), "too cold", 13, new Color(0.6f, 0.7f, 1f));
            Text(new Rect(r.xMax + 8, Y(1.0) - 10, 140, 20), "burns", 13, new Color(1f, 0.6f, 0.5f));

            // Current temperature.
            float cy = Y(Mathf.Min(1.1f, (float)g.Temperature));
            Fill(new Rect(r.x - 14, cy - 2, r.width + 28, 4), Color.white);
            Text(new Rect(10, cy - 11, 44, 22), ">", 20, Color.white, TextAnchor.MiddleRight);
            Text(new Rect(30, r.yMax + 6, 200, 24), g.Band.ToString(), 15, HeatColor(g.Temperature), bold: true);
        }

        private void DrawForgeAndAnvil(ForgeMinigame g)
        {
            // Forge on the left.
            var forge = new Rect(260, 110, 230, 220);
            Fill(forge, new Color(0.18f, 0.15f, 0.14f));
            Outline(forge, new Color(0.35f, 0.3f, 0.28f), 3);
            var mouth = new Rect(forge.x + 25, forge.y + 60, forge.width - 50, 120);
            Fill(mouth, new Color(0.05f, 0.03f, 0.03f));
            float flicker = 0.75f + 0.25f * Mathf.PerlinNoise(Time.time * 4f, 0.3f);
            for (int i = 0; i < 6; i++)
            {
                float h = (30 + 50 * Mathf.PerlinNoise(Time.time * 3f + i, i)) * flicker;
                Fill(new Rect(mouth.x + 8 + i * 28, mouth.yMax - h, 22, h), new Color(1f, 0.35f + 0.1f * i, 0.05f, 0.85f));
            }
            Text(new Rect(forge.x, forge.y + 6, forge.width, 26), "FORGE", 15, new Color(0.7f, 0.6f, 0.55f), TextAnchor.MiddleCenter);
            if (g.Stage == ForgeStage.Heat)
            {
                // The bar sits in the fire.
                Fill(new Rect(mouth.x + 10, mouth.y + 50, mouth.width + 60, 16), HeatColor(g.Temperature));
                if (Pressed(forge)) g.PullFromForge();
            }

            // Anvil and blade on the right.
            var anvil = new Rect(640, 250, 480, 70);
            Fill(anvil, new Color(0.28f, 0.29f, 0.31f));
            Fill(new Rect(anvil.x + 120, anvil.yMax, 240, 40), new Color(0.22f, 0.23f, 0.25f));
            Fill(new Rect(anvil.x + 60, anvil.yMax + 40, 360, 18), new Color(0.2f, 0.21f, 0.22f));

            DrawBlade(g, anvil.x + 20, anvil.y - 6);
        }

        /// <summary>Ghost outline of the target shape, plus the metal as it is now. Each lane reshapes its own part.</summary>
        private void DrawBlade(ForgeMinigame g, float x, float baseY)
        {
            Color metal = g.Stage == ForgeStage.Heat || g.Stage == ForgeStage.Strike ? HeatColor(g.Temperature) : new Color(0.72f, 0.74f, 0.78f);
            if (g.Stage == ForgeStage.Heat && !g.IsReheating) metal = new Color(0.25f, 0.22f, 0.22f); // still in the forge
            Color ghost = new Color(1f, 1f, 1f, 0.25f);

            float p(int lane) => Mathf.Min(1.25f, (float)(g.Progress[lane] / g.Target[lane]));
            float tip = p(0), body = p(1), edge = p(2);

            // Grip and guard (not forged here).
            Fill(new Rect(x, baseY - 26, 60, 14), new Color(0.4f, 0.26f, 0.15f));
            Fill(new Rect(x + 60, baseY - 44, 14, 50), new Color(0.45f, 0.45f, 0.48f));

            float bodyX = x + 74, bodyLen = 260, bodyH = 22;
            // Ghost target.
            Outline(new Rect(bodyX, baseY - 30, bodyLen, bodyH), ghost, 1);
            Outline(new Rect(bodyX, baseY - 8, bodyLen, 6), ghost, 1);
            for (int i = 0; i < 6; i++) Outline(new Rect(bodyX + bodyLen + i * 12, baseY - 28 + i * 2.6f, 12, bodyH + 2 - i * 5.2f), ghost, 1);

            // Body: unworked metal is short and thick; worked metal is long and thin.
            float len = bodyLen * Mathf.Lerp(0.55f, 1f, Mathf.Min(1, body)) * (body > 1.05f ? 1 + (body - 1) * 0.6f : 1);
            float thick = bodyH * Mathf.Lerp(1.6f, 1f, Mathf.Min(1, body));
            Fill(new Rect(bodyX, baseY - 8 - thick, len, thick), metal);
            // Edge: a bright bevel appears along the bottom as it is drawn out.
            if (edge > 0) Fill(new Rect(bodyX, baseY - 8, len * Mathf.Min(1, edge), 6), Color.Lerp(metal, Color.white, 0.35f));
            // Tip: tapers as it is worked.
            float tipEnd = bodyX + len;
            int tipSteps = Mathf.RoundToInt(6 * Mathf.Min(1, tip));
            for (int i = 0; i < tipSteps; i++)
                Fill(new Rect(tipEnd + i * 12, baseY - 8 - thick + i * thick * 0.08f, 12, thick - i * thick * 0.16f), metal);

            // Overworked warnings.
            for (int lane = 0; lane < 3; lane++)
                if (p(lane) > 1.05f)
                    Text(new Rect(x + 120 + lane * 110, baseY - 80, 160, 22), $"{LaneNames[lane]} overworked", 13, new Color(1f, 0.5f, 0.4f));

            // Part progress.
            for (int lane = 0; lane < 3; lane++)
            {
                float bx = x + lane * 150, by = baseY + 100;
                Text(new Rect(bx, by, 140, 20), $"{LaneNames[lane]}  {g.Progress[lane]:0.#}/{g.Target[lane]}", 13, LaneColors[lane]);
                Fill(new Rect(bx, by + 20, 130, 6), new Color(0.25f, 0.24f, 0.23f));
                Fill(new Rect(bx, by + 20, 130 * Mathf.Min(1, p(lane)), 6), LaneColors[lane]);
            }
        }

        private void DrawTrack(ForgeMinigame g)
        {
            bool paused = g.Stage == ForgeStage.Heat;
            bool cold = g.Stage == ForgeStage.Strike && !g.CanStrike;

            for (int lane = 0; lane < 3; lane++)
            {
                var r = new Rect(LaneX, LaneY[lane], LaneRight - LaneX, LaneH);
                Fill(r, cold ? new Color(0.12f, 0.13f, 0.18f) : new Color(0.15f, 0.14f, 0.13f));
                var label = new Rect(LaneX, r.y, 100, LaneH);
                Fill(label, new Color(0.2f, 0.19f, 0.18f));
                Text(label, $"<b>{LaneKeys[lane]}</b>  {LaneNames[lane]}", 16, LaneColors[lane], TextAnchor.MiddleCenter);
                if (!paused && Pressed(r)) g.Strike((Lane)lane);
            }

            // Hit windows at the hammer line.
            float good = (float)g.GoodWindow * PixelsPerSecond, perfect = (float)g.PerfectWindow * PixelsPerSecond;
            float top = LaneY[0], height = LaneY[2] + LaneH - LaneY[0];
            Fill(new Rect(HammerX - good, top, good * 2, height), new Color(1f, 1f, 1f, 0.06f));
            Fill(new Rect(HammerX - perfect, top, perfect * 2, height), new Color(1f, 0.85f, 0.4f, 0.12f));
            Fill(new Rect(HammerX - 1.5f, top - 8, 3, height + 16), new Color(1f, 0.85f, 0.5f));
            Text(new Rect(HammerX - 60, top - 30, 120, 22), "hammer", 13, new Color(1f, 0.85f, 0.5f), TextAnchor.MiddleCenter);

            // Beat grid lines.
            double beat = g.BeatInterval;
            double firstBeat = System.Math.Ceiling((g.SongTime - 0.5) / beat) * beat;
            for (double t = firstBeat; ; t += beat)
            {
                float x = HammerX + (float)((t - g.SongTime) * PixelsPerSecond);
                if (x > LaneRight) break;
                if (x > LaneX + 100) Fill(new Rect(x, top, 1, height), new Color(1f, 1f, 1f, 0.07f));
            }

            // Markers.
            foreach (var n in g.Notes)
            {
                if (n.Judged) continue;
                float x = HammerX + (float)((n.Time - g.SongTime) * PixelsPerSecond);
                if (x > LaneRight - 10) continue;
                if (x < LaneX + 100) continue;
                int lane = (int)n.Lane;
                var c = LaneColors[lane];
                if (cold || paused) c = Color.Lerp(c, Color.gray, 0.6f);
                Fill(new Rect(x - 9, LaneY[lane] + 6, 18, LaneH - 12), c);
            }

            if (paused && g.IsReheating)
                Text(new Rect(LaneX, LaneY[1], LaneRight - LaneX, LaneH), "<b>Paused: reheating</b>", 22, Color.white, TextAnchor.MiddleCenter);
            if (cold)
                Text(new Rect(LaneX, LaneY[2] + LaneH + 6, LaneRight - LaneX, 30), "<b>Too cold, press R to reheat</b>", 20, new Color(0.65f, 0.75f, 1f), TextAnchor.MiddleCenter);
            if (g.Stage == ForgeStage.Strike && g.SongTime < 0)
                Text(new Rect(LaneX, LaneY[0] - 34, LaneRight - LaneX, 30), $"Get ready… {Mathf.CeilToInt((float)(-g.SongTime / beat))}", 20, Color.white, TextAnchor.MiddleCenter);
        }

        private void DrawQuench(ForgeMinigame g)
        {
            var area = new Rect(300, 380, 940, 280);
            Fill(area, new Color(0.13f, 0.13f, 0.14f));

            if (g.Stage == ForgeStage.QuenchChoose)
            {
                var media = new[] { QuenchMedium.Water, QuenchMedium.Oil, QuenchMedium.Brine };
                for (int i = 0; i < 3; i++)
                {
                    var r = new Rect(area.x + 20 + i * 305, area.y + 30, 290, 70);
                    if (Button(r, $"<b>{i + 1}</b>  {media[i]}")) g.ChooseQuench(media[i]);
                    Text(new Rect(r.x, r.yMax + 8, r.width, 80), ForgeTuning.QuenchDescription(media[i]), 14, new Color(0.8f, 0.78f, 0.75f));
                }
                return;
            }

            double target = ForgeTuning.QuenchTargetSeconds(g.Medium);
            double tol = ForgeTuning.QuenchTolerance(g.Medium, g.Skill);
            double max = target * 2;
            var bar = new Rect(area.x + 30, area.y + 60, area.width - 60, 46);
            Fill(bar, new Color(0.2f, 0.2f, 0.22f));
            float X(double s) => bar.x + (float)(System.Math.Min(s, max) / max) * bar.width;
            Fill(new Rect(X(target - tol), bar.y, X(target + tol) - X(target - tol), bar.height), new Color(0.25f, 0.8f, 0.35f, 0.55f));
            Fill(new Rect(bar.x, bar.y + 12, X(g.QuenchSeconds) - bar.x, bar.height - 24), new Color(0.75f, 0.85f, 1f));
            Outline(bar, new Color(0.45f, 0.45f, 0.5f));
            Text(new Rect(bar.x, bar.y - 30, bar.width, 26), $"{g.Medium}: {g.QuenchSeconds:0.0} s  (aim for {target:0.0} s)", 18, Color.white);

            var bucket = new Rect(area.x + area.width / 2 - 110, area.y + 130, 220, 120);
            Fill(bucket, g.QuenchHeld ? new Color(0.3f, 0.45f, 0.6f) : new Color(0.22f, 0.3f, 0.38f));
            Outline(bucket, new Color(0.5f, 0.6f, 0.7f));
            Text(bucket, g.QuenchHeld ? "<b>in the quench…</b>\nlet go in the green zone" : "<b>hold here</b>\nor hold SPACE", 16, Color.white, TextAnchor.MiddleCenter);
            if (Pressed(bucket)) g.BeginQuench();
        }

        private void DrawFinish(ForgeMinigame g)
        {
            var bar = new Rect(360, 440, 820, 50);
            Fill(bar, new Color(0.2f, 0.19f, 0.18f));
            float X(double v) => bar.x + (float)v * bar.width;
            double c = g.FinishSweetCenter, w = g.FinishSweetWidth;
            Fill(new Rect(X(c - w), bar.y, X(c + w) - X(c - w), bar.height), new Color(1f, 0.8f, 0.4f, 0.2f));
            Fill(new Rect(X(c - w / 2), bar.y, X(c + w / 2) - X(c - w / 2), bar.height), new Color(1f, 0.85f, 0.4f, 0.6f));
            Fill(new Rect(X(g.FinishCursor) - 5, bar.y - 10, 10, bar.height + 20), new Color(0.8f, 0.8f, 0.85f));
            Outline(bar, new Color(0.45f, 0.42f, 0.4f));
            Text(new Rect(bar.x, bar.y - 40, bar.width, 30), $"Whetstone  ·  stroke {g.FinishStrokesDone + 1} of {ForgeTuning.FinishStrokes}", 18, Color.white);
            if (Button(new Rect(bar.x + bar.width / 2 - 100, bar.yMax + 30, 200, 50), "<b>Stroke</b> (SPACE)")) g.FinishStroke();
        }

        private void DrawPopups()
        {
            const float life = 0.7f;
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                var p = _popups[i];
                float age = Time.time - p.Born;
                if (age > (p.Lane < 0 ? life * 2.5f : life)) { _popups.RemoveAt(i); continue; }
                var c = p.Color;
                c.a = 1f - age / (p.Lane < 0 ? life * 2.5f : life);
                if (p.Lane >= 0)
                    Text(new Rect(HammerX + 20, LaneY[p.Lane] + 8 - age * 30, 200, 30), $"<b>{p.Text}</b>", 18, c);
                else
                    Text(new Rect(260, 340 - age * 20, 900, 34), $"<b>{p.Text}</b>", 22, c, TextAnchor.MiddleCenter);
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
                $"<b>Strikes</b>  perfect {r.Perfect} · good {r.Good} · missed {r.Missed} · off-beat {r.OffBeat} · cold {r.Cold} · reheats {r.Reheats}\n" +
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
