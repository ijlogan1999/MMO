using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mmo.Crafting.Cooking.View
{
    /// <summary>
    /// Placeholder front end for the cooking minigame. Everything is drawn with Unity's immediate-mode GUI
    /// (coloured rectangles and text), so there are no sprites, prefabs or packages to set up yet.
    /// The rules live in CookingMinigame (Core); this class only draws and forwards input.
    ///
    /// Deliberately unguided: no doneness bars, prompts, markers or timers. The player reads the colour of the food,
    /// the smoke, the bubbles and the scum. The results screen explains the outcome.
    /// Controls: 1-5 pick a dish (or click it). W / S closer to or further from the coals. SPACE turn, flip or stir.
    /// Q skim or baste. A add the next ingredient. F take off the fire (again to carve a roast). H herb while seasoning.
    /// SPACE chops on the board. ENTER cook again. ESC back to setup.
    /// </summary>
    public sealed class CookView : MonoBehaviour
    {
        private const float W = 1280f, H = 720f;
        private const string SkillPref = "mmo.cook.skill";
        private const string IntroPref = "mmo.cook.introSeen";

        private enum Mode { Intro, Setup, Play, Results }

        private Mode _mode = Mode.Setup;
        private int _introPage;
        private readonly CookSetup _setup = new CookSetup();
        private int _skill = 1;
        private int _worldSeed = 1;
        private CookingMinigame _game;
        private CookAudio _audio;
        private int _seed = 1;
        private int _skillBefore;
        private int _resultPick;

        private readonly HashSet<KeyCode> _held = new HashSet<KeyCode>();
        private GUIStyle _style;

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            EnsureCameraAndListener();
            _audio = gameObject.AddComponent<CookAudio>();
            _skill = Mathf.Clamp(PlayerPrefs.GetInt(SkillPref, 1), 1, 100);
            _setup.Dishes[0] = CookRecipe.MeatOnAStick;
            _setup.Dishes[1] = CookRecipe.Flatbread;
            _setup.Dishes[2] = CookRecipe.SpitRoast;
            _setup.Dishes[3] = CookRecipe.HuntersStew;
            _setup.Dishes[4] = CookRecipe.BoneBroth;
            _seed = Random.Range(1, int.MaxValue);
            if (PlayerPrefs.GetInt(IntroPref, 0) == 0) _mode = Mode.Intro;
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
            cam.backgroundColor = new Color(0.08f, 0.07f, 0.06f);
            if (FindAnyObjectByType<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
        }

        // A key released while the window was unfocused never sends KeyUp; forget held keys so the next press counts.
        private void OnApplicationFocus(bool focused)
        {
            if (!focused) _held.Clear();
        }

        private bool CanStart()
        {
            int n = CookTuning.StationsFor(_skill);
            for (int i = 0; i < n; i++)
                if (_setup.Dishes[i] != null && _setup.Blocker(_setup.Dishes[i]) == null) return true;
            return false;
        }

        private void StartCook()
        {
            if (!CanStart()) return;
            _setup.SkillLevel = _skill;
            _setup.World = new CookWorld(_worldSeed);
            _game = new CookingMinigame(_setup, _seed++);
            _skillBefore = _skill;
            _resultPick = 0;
            _mode = Mode.Play;
        }

        private void Update()
        {
            _audio.SetFire(_mode == Mode.Play && _game != null && _game.Stage == CookStage.Cook);
            if (_mode != Mode.Play || _game == null) return;
            _game.Update(Time.deltaTime);
            foreach (var e in _game.Events) _audio.Play(e);
            _game.Events.Clear();
            if (_game.Stage == CookStage.Done)
            {
                _skill = Mathf.Min(100, _skill + _game.Result.SkillGain);
                PlayerPrefs.SetInt(SkillPref, _skill);
                _mode = Mode.Results;
            }
        }

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
            }
        }

        private static int NumberKey(KeyCode key)
        {
            if (key >= KeyCode.Alpha1 && key <= KeyCode.Alpha5) return key - KeyCode.Alpha1;
            if (key >= KeyCode.Keypad1 && key <= KeyCode.Keypad5) return key - KeyCode.Keypad1;
            return -1;
        }

        private static bool IsEnter(KeyCode k) => k == KeyCode.Return || k == KeyCode.KeypadEnter;

        private bool OnKeyDown(KeyCode key)
        {
            switch (_mode)
            {
                case Mode.Intro:
                    if (key == KeyCode.Space || IsEnter(key) || key == KeyCode.RightArrow) { NextIntroPage(); return true; }
                    if (key == KeyCode.LeftArrow || key == KeyCode.Backspace) { _introPage = Mathf.Max(0, _introPage - 1); return true; }
                    if (key == KeyCode.Escape) { CloseIntro(); return true; }
                    return false;
                case Mode.Setup:
                    if (IsEnter(key)) { StartCook(); return true; }
                    return false;
                case Mode.Results:
                    if (IsEnter(key) || key == KeyCode.Space) { StartCook(); return true; }
                    if (key == KeyCode.Escape) { _mode = Mode.Setup; return true; }
                    int pick = NumberKey(key);
                    if (pick >= 0 && pick < _game.Result.Dishes.Count) { _resultPick = pick; return true; }
                    return false;
            }

            var g = _game;
            if (key == KeyCode.Escape) { _mode = Mode.Setup; return true; }
            int n = NumberKey(key);
            if (n >= 0 && g.Stage != CookStage.Chop) { g.Select(n); return true; }
            if (key == KeyCode.Tab) { g.SelectNext(); return true; }

            switch (g.Stage)
            {
                case CookStage.Season:
                    if (key == KeyCode.H) { g.CycleHerb(); return true; }
                    if (IsEnter(key) || key == KeyCode.Space) { g.FinishSeasoning(); return true; }
                    break;
                case CookStage.Chop:
                    if (key == KeyCode.Space) { g.Chop(); return true; }
                    break;
                case CookStage.Cook:
                    if (key == KeyCode.W || key == KeyCode.UpArrow) { g.HeatUp(); return true; }
                    if (key == KeyCode.S || key == KeyCode.DownArrow) { g.HeatDown(); return true; }
                    if (key == KeyCode.Space) { g.Tend(); return true; }
                    if (key == KeyCode.Q) { g.SkimOrBaste(); return true; }
                    if (key == KeyCode.A) { g.AddNext(); return true; }
                    if (key == KeyCode.F) { g.TakeOff(); return true; }
                    break;
            }
            return false;
        }

        /// <summary>True on the mouse press inside r.</summary>
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

        // ------------------------------------------------------------------ drawing helpers

        private void OnGUI()
        {
            if (_style == null) _style = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };

            float scale = Mathf.Min(Screen.width / W, Screen.height / H);
            float ox = (Screen.width - W * scale) / 2f, oy = (Screen.height - H * scale) / 2f;
            GUI.matrix = Matrix4x4.TRS(new Vector3(ox, oy, 0), Quaternion.identity, new Vector3(scale, scale, 1));

            HandleInput(Event.current);
            Fill(new Rect(0, 0, W, H), new Color(0.11f, 0.09f, 0.08f));

            switch (_mode)
            {
                case Mode.Intro: DrawIntro(); break;
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
            Fill(r, !enabled ? new Color(0.18f, 0.17f, 0.16f) : selected ? new Color(0.5f, 0.33f, 0.14f) : new Color(0.24f, 0.21f, 0.19f));
            Outline(r, selected ? new Color(1f, 0.75f, 0.35f) : new Color(0.4f, 0.36f, 0.32f), selected ? 2f : 1f);
            Text(r, label, 15, enabled ? Color.white : new Color(0.5f, 0.5f, 0.5f), TextAnchor.MiddleCenter);
            return enabled && Pressed(r);
        }

        private static Color Ramp(float x, (float, Color)[] stops)
        {
            if (x <= stops[0].Item1) return stops[0].Item2;
            for (int i = 1; i < stops.Length; i++)
                if (x <= stops[i].Item1)
                    return Color.Lerp(stops[i - 1].Item2, stops[i].Item2, (x - stops[i - 1].Item1) / (stops[i].Item1 - stops[i - 1].Item1));
            return stops[stops.Length - 1].Item2;
        }

        // Food colours follow absolute doneness, not this world's done point: players learn where "done" sits here.
        private static readonly (float, Color)[] MeatStops =
        {
            (0.00f, new Color(0.80f, 0.30f, 0.34f)),
            (0.45f, new Color(0.78f, 0.55f, 0.50f)),
            (0.80f, new Color(0.74f, 0.52f, 0.30f)),
            (1.00f, new Color(0.66f, 0.40f, 0.18f)),
            (1.25f, new Color(0.45f, 0.25f, 0.11f)),
            (1.60f, new Color(0.16f, 0.10f, 0.07f)),
            (2.20f, new Color(0.06f, 0.05f, 0.05f)),
        };

        private static readonly (float, Color)[] BreadStops =
        {
            (0.00f, new Color(0.93f, 0.89f, 0.78f)),
            (0.60f, new Color(0.92f, 0.80f, 0.55f)),
            (1.00f, new Color(0.82f, 0.56f, 0.26f)),
            (1.25f, new Color(0.58f, 0.34f, 0.14f)),
            (1.60f, new Color(0.20f, 0.12f, 0.07f)),
            (2.20f, new Color(0.06f, 0.05f, 0.05f)),
        };

        private static readonly (float, Color)[] LiquidStops =
        {
            (0.00f, new Color(0.50f, 0.56f, 0.58f)),
            (0.50f, new Color(0.62f, 0.55f, 0.40f)),
            (1.00f, new Color(0.66f, 0.44f, 0.20f)),
            (1.40f, new Color(0.42f, 0.24f, 0.10f)),
            (2.20f, new Color(0.12f, 0.07f, 0.05f)),
        };

        private static readonly (float, Color)[] VegStops =
        {
            (0.00f, new Color(0.98f, 0.55f, 0.12f)),
            (1.00f, new Color(0.88f, 0.52f, 0.25f)),
            (1.40f, new Color(0.62f, 0.48f, 0.33f)),
            (2.00f, new Color(0.40f, 0.33f, 0.26f)),
        };

        private static Color GradeColor(int q)
        {
            if (q >= 81) return new Color(1f, 0.75f, 0.25f);
            if (q >= 61) return new Color(0.45f, 0.75f, 1f);
            if (q >= 41) return new Color(0.85f, 0.85f, 0.85f);
            if (q >= 21) return new Color(0.7f, 0.6f, 0.5f);
            return new Color(0.6f, 0.45f, 0.45f);
        }

        private static float Noise(float a, float b) => Mathf.PerlinNoise(a, b);

        // ------------------------------------------------------------------ intro
        // The old cook explains the hearth once, before the first session. It teaches the craft, never the answer:
        // no colours to aim for, no times, no herb pairings.

        private static readonly (string Title, string Body)[] IntroPages =
        {
            ("The hearth",
             "Sit down. A cook's work is keeping many things right at once, so listen once and then learn by burning a few.\n\n" +
             "Everything on your fire cooks whether you watch it or not. The trick is to know which one needs you next. " +
             "As your hands get surer, you'll find room for more at the fire.\n\n" +
             "Nobody will tell you when something is done. You look at it."),
            ("Heat and sides",
             "Closer to the coals cooks faster, and burns faster. Further away is slow and forgiving.\n\n" +
             "Meat on a spit cooks on the side facing the fire, so turn it. Bread on a stone only cooks underneath: " +
             "peek at the edge and flip it.\n\n" +
             "Pale is raw. Black is ruin. Where 'done' sits between them, every land argues about, and you'll learn yours " +
             "by eating your mistakes."),
            ("Pots",
             "A pot left alone catches on the bottom. You'll see the smoke and smell it; by then it's already costing you. Stir it. " +
             "Low heat never catches, but it is slow.\n\n" +
             "Grey scum rises when a pot first simmers. Skim it off or the broth goes murky.\n\n" +
             "Put things in in their own time. What takes longest goes in first. What goes in too early turns to mush; " +
             "too late, and it's still hard when the rest is done."),
            ("The board",
             "Baste a roast with its own fat or it dries on the spit. When it comes off, let it rest on the board before you carve it.\n\n" +
             "Chop in a steady rhythm. Even pieces cook evenly.\n\n" +
             "Wild herbs are a matter of taste: some suit a food, some fight it, and no two lands agree. Salt and rest always help.\n\n" +
             "What you cook can only be as good as what you cook it from, and what you cook it on. Taste everything afterwards. That's how a cook learns."),
        };

        private void NextIntroPage()
        {
            if (_introPage < IntroPages.Length - 1) _introPage++;
            else CloseIntro();
        }

        private void CloseIntro()
        {
            PlayerPrefs.SetInt(IntroPref, 1);
            _mode = Mode.Setup;
        }

        private void DrawIntro()
        {
            var page = IntroPages[_introPage];
            var card = new Rect(190, 70, 900, 560);
            Fill(card, new Color(0.17f, 0.13f, 0.10f));
            Outline(card, new Color(0.48f, 0.36f, 0.24f), 2);
            Text(new Rect(card.x + 50, card.y + 34, 800, 40), "The old cook", 16, new Color(0.66f, 0.56f, 0.44f));
            Text(new Rect(card.x + 50, card.y + 60, 800, 60), $"<b>{page.Title}</b>", 34, new Color(1f, 0.8f, 0.5f));
            Text(new Rect(card.x + 50, card.y + 130, 800, 360), page.Body, 20, new Color(0.93f, 0.89f, 0.82f));
            Text(new Rect(card.x + 50, card.yMax - 50, 300, 30), $"{_introPage + 1} / {IntroPages.Length}", 15, new Color(0.6f, 0.53f, 0.46f));

            bool last = _introPage == IntroPages.Length - 1;
            if (_introPage > 0 && Button(new Rect(card.xMax - 430, card.yMax - 70, 180, 48), "Back")) _introPage--;
            if (Button(new Rect(card.xMax - 230, card.yMax - 70, 180, 48), last ? "<b>To the hearth</b>" : "<b>Next</b>", true)) NextIntroPage();
            Text(new Rect(0, H - 40, W, 30), "SPACE next · LEFT back · ESC skip", 14, new Color(0.5f, 0.47f, 0.44f), TextAnchor.MiddleCenter);
        }

        // ------------------------------------------------------------------ setup screen

        private void CycleDish(int slot)
        {
            var all = CookRecipe.All;
            var cur = _setup.Dishes[slot];
            int idx = cur == null ? -1 : System.Array.IndexOf(all, cur);
            idx++;
            _setup.Dishes[slot] = idx >= all.Length ? null : all[idx];
        }

        private void DrawSetup()
        {
            Text(new Rect(40, 20, 1200, 50), "Hearth  <size=20><color=#b9a99a>cooking prototype</color></size>", 36, new Color(1f, 0.75f, 0.4f), bold: true);

            // Dishes on each station
            int stations = CookTuning.StationsFor(_skill);
            Text(new Rect(40, 80, 400, 30), $"On the fire  <size=13><color=#a59a90>({stations} stations at your skill; click to change)</color></size>", 18, new Color(0.8f, 0.75f, 0.7f));
            for (int i = 0; i < 5; i++)
            {
                var r = _setup.Dishes[i];
                bool open = i < stations;
                string label = r == null ? "<color=#8c8279>(empty)</color>" : $"{r.Name}  <size=12>({r.Rank}, lvl {r.ItemLevel})</size>";
                if (!open) label = $"<size=13>Station {i + 1}: more skill needed</size>";
                if (Button(new Rect(40, 110 + i * 54, 380, 46), $"{i + 1}.  {label}", false, open)) CycleDish(i);
                string block = open ? _setup.Blocker(r) : null;
                if (block != null) Text(new Rect(430, 120 + i * 54, 260, 30), $"<color=#ff9a70>{block}</color>", 13, Color.white);
            }

            var sel = FirstDish();
            if (sel != null)
                Text(new Rect(40, 388, 640, 250), "<b>On these dishes</b>\n" + DishList(stations), 13, new Color(0.85f, 0.82f, 0.78f));

            float x = 700;
            Text(new Rect(x, 80, 560, 30), $"Cooking skill: <b>{_skill}</b> ({CookTuning.RankFor(_skill)})", 18, Color.white);
            int newSkill = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x, 115, 520, 20), _skill, 1, 100));
            if (newSkill != _skill) { _skill = newSkill; PlayerPrefs.SetInt(SkillPref, _skill); }
            Text(new Rect(x, 132, 560, 22), "Test slider (in game this is your character's skill; cooking raises it).", 13, new Color(0.65f, 0.62f, 0.6f));

            Text(new Rect(x, 160, 300, 26), "Hearth", 18, new Color(0.8f, 0.75f, 0.7f));
            for (int i = 0; i < Hearth.All.Length; i++)
                if (Button(new Rect(x + i * 178, 186, 170, 34), Hearth.All[i].Name, _setup.Hearth == Hearth.All[i])) _setup.Hearth = Hearth.All[i];
            Text(new Rect(x, 222, 560, 22), _setup.Hearth.Description, 13, new Color(0.65f, 0.62f, 0.6f));

            Text(new Rect(x, 246, 300, 26), "Cookware", 18, new Color(0.8f, 0.75f, 0.7f));
            for (int i = 0; i < Cookware.All.Length; i++)
                if (Button(new Rect(x + i * 178, 272, 170, 34), $"<size=13>{Cookware.All[i].Name}</size>", _setup.Cookware == Cookware.All[i])) _setup.Cookware = Cookware.All[i];
            Text(new Rect(x, 308, 560, 22), _setup.Cookware.Description, 13, new Color(0.65f, 0.62f, 0.6f));

            Text(new Rect(x, 332, 300, 26), "World", 18, new Color(0.8f, 0.75f, 0.7f));
            for (int i = 0; i < 3; i++)
                if (Button(new Rect(x + i * 118, 358, 110, 32), $"World {i + 1}", _worldSeed == i + 1)) _worldSeed = i + 1;
            Text(new Rect(x + 360, 352, 210, 44), "Each world rolls its own done points, rest time and herb pairings.", 12, new Color(0.65f, 0.62f, 0.6f));

            // Ingredient qualities, only for what's on the fire
            Text(new Rect(x, 400, 560, 26), "Ingredients (quality from the hunter, farmer or vendor)", 17, new Color(0.8f, 0.75f, 0.7f));
            int row = 0;
            foreach (Ingredient ing in System.Enum.GetValues(typeof(Ingredient)))
            {
                if (!Used(ing, stations)) continue;
                float y = 426 + row * 26;
                int q = _setup.QualityOf(ing);
                Text(new Rect(x, y, 150, 26), CookTuning.IngredientName(ing), 15, Color.white);
                _setup.Quality[ing] = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x + 150, y + 7, 280, 20), q, 1, 100));
                Text(new Rect(x + 445, y, 130, 26), $"{CookTuning.GradeWord(q)} {q}", 15, GradeColor(q));
                row++;
            }

            if (Button(new Rect(x, 650, 260, 50), "<b>Light the fire</b>  <size=13>(Enter)</size>", true, CanStart())) StartCook();
            if (Button(new Rect(40, 650, 300, 50), "Hear the old cook again")) { _introPage = 0; _mode = Mode.Intro; }
            Text(new Rect(360, 646, 330, 64),
                "Keys: 1-5 pick a dish · W S closer or further from the coals · SPACE turn, flip, stir · Q skim or baste · A add · F take off / carve",
                12, new Color(0.65f, 0.62f, 0.6f));
        }

        private CookRecipe FirstDish()
        {
            foreach (var d in _setup.Dishes) if (d != null) return d;
            return null;
        }

        private string DishList(int stations)
        {
            var seen = new HashSet<CookRecipe>();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < stations; i++)
            {
                var r = _setup.Dishes[i];
                if (r == null || !seen.Add(r)) continue;
                sb.Append($"<b>{r.Name}</b>: {r.Description} <color=#a59a90>({r.Effect})</color>\n");
            }
            return sb.ToString();
        }

        private bool Used(Ingredient ing, int stations)
        {
            for (int i = 0; i < stations; i++)
            {
                var r = _setup.Dishes[i];
                if (r == null) continue;
                foreach (var input in r.Inputs) if (input.Ingredient == ing) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ play screen
        // No bars, prompts, markers or timers: the player reads the food and the fire.

        private const float CoalsY = 560f;

        private Rect SlotRect(int slot, int count)
        {
            float width = Mathf.Min(240f, (W - 80f) / count);
            float start = (W - width * count) / 2f;
            return new Rect(start + slot * width, 90, width, CoalsY - 90 + 60);
        }

        private void DrawPlay()
        {
            var g = _game;
            string title = g.Stage == CookStage.Season ? "Season" : g.Stage == CookStage.Chop ? "Chop" : "Cook";
            Text(new Rect(30, 14, 900, 40), $"<b>{title}</b>  <size=16><color=#a59a90>{_setup.Hearth.Name} · World {_worldSeed}</color></size>", 26, new Color(1f, 0.8f, 0.5f));

            switch (g.Stage)
            {
                case CookStage.Season: DrawSeason(g); break;
                case CookStage.Chop: DrawChop(g); break;
                case CookStage.Cook: DrawCook(g); break;
            }
            DrawControls(g);
        }

        /// <summary>Which keys do what right now. Keys only: nothing about when or which is right.</summary>
        private void DrawControls(CookingMinigame g)
        {
            string keys = "";
            if (g.Stage == CookStage.Season) keys = "<b>1-5</b> pick a dish   <b>H</b> change its herb (or click one)   <b>ENTER</b> on to the fire";
            else if (g.Stage == CookStage.Chop) keys = "<b>SPACE</b> chop";
            else if (g.Stage == CookStage.Cook)
            {
                var d = g.SelectedDish;
                string pick = "<b>1-5</b> pick a dish   ";
                if (d.State == DishState.Resting) keys = pick + "<b>F</b> carve";
                else if (d.State == DishState.Served) keys = pick;
                else
                {
                    string heat = "<b>W</b> closer to the coals   <b>S</b> further   ";
                    switch (d.Recipe.Station)
                    {
                        case StationKind.Spit:
                            keys = pick + heat + "<b>SPACE</b> turn   " + (d.Recipe.CanBaste ? "<b>Q</b> baste   " : "") + (d.Recipe.Rests ? "<b>F</b> off to the board" : "<b>F</b> take off");
                            break;
                        case StationKind.Griddle:
                            keys = pick + heat + "<b>SPACE</b> flip   <b>F</b> take off";
                            break;
                        default:
                            keys = pick + heat + "<b>SPACE</b> stir   <b>Q</b> skim   " + (d.AllPartsIn ? "" : $"<b>A</b> add the {d.NextPartName}   ") + "<b>F</b> take off";
                            break;
                    }
                }
            }
            var bar = new Rect(0, H - 40, W, 40);
            Fill(bar, new Color(0.06f, 0.05f, 0.045f));
            Text(new Rect(30, bar.y, W - 60, bar.height), keys + "      <color=#7d746c>ESC leave</color>", 15, new Color(0.86f, 0.82f, 0.78f), TextAnchor.MiddleLeft);
        }

        // ---- season

        private static readonly Herb[] Herbs = { Herb.None, Herb.Thyme, Herb.WildGarlic, Herb.Juniper, Herb.Sage };

        private void DrawSeason(CookingMinigame g)
        {
            Text(new Rect(30, 52, 1000, 30), "Choose a herb for each dish, then take them to the fire.", 15, new Color(0.7f, 0.66f, 0.6f));
            for (int i = 0; i < g.Dishes.Count; i++)
            {
                var d = g.Dishes[i];
                var col = SlotRect(i, g.Dishes.Count);
                var card = new Rect(col.x + 8, 110, col.width - 16, 420);
                Fill(card, new Color(0.17f, 0.14f, 0.12f));
                Outline(card, i == g.Selected ? new Color(0.9f, 0.7f, 0.4f) : new Color(0.32f, 0.28f, 0.25f), i == g.Selected ? 2 : 1);
                if (Pressed(new Rect(card.x, card.y, card.width, 150))) g.Select(i);
                Text(new Rect(card.x, card.y + 10, card.width, 30), $"{i + 1}. <b>{d.Recipe.Name}</b>", 16, Color.white, TextAnchor.UpperCenter);
                DrawRawFood(d, new Rect(card.center.x - 60, card.y + 60, 120, 70));
                for (int h = 0; h < Herbs.Length; h++)
                {
                    var r = new Rect(card.x + 14, card.y + 160 + h * 48, card.width - 28, 40);
                    if (Button(r, CookTuning.HerbName(Herbs[h]), d.Herb == Herbs[h])) { g.Select(i); g.SetHerb(i, Herbs[h]); }
                }
            }
            if (Button(new Rect(W / 2 - 150, 560, 300, 50), g.NeedsChop ? "<b>To the chopping board</b>" : "<b>Onto the fire</b>", true)) g.FinishSeasoning();
        }

        private void DrawRawFood(Dish d, Rect r)
        {
            switch (d.Recipe.Station)
            {
                case StationKind.Griddle:
                    Fill(new Rect(r.x + 10, r.y + 20, r.width - 20, r.height - 30), Ramp(0, BreadStops));
                    break;
                case StationKind.Pot:
                    Fill(new Rect(r.x + 10, r.y + 10, r.width - 20, r.height - 10), new Color(0.25f, 0.25f, 0.27f));
                    Fill(new Rect(r.x + 16, r.y + 14, r.width - 32, 14), Ramp(0, LiquidStops));
                    break;
                default:
                    float s = d.Recipe.Rests ? 1f : 0.6f;
                    Fill(new Rect(r.center.x - 50 * s, r.center.y - 22 * s, 100 * s, 44 * s), Ramp(0, MeatStops));
                    break;
            }
        }

        // ---- chop

        private void DrawChop(CookingMinigame g)
        {
            Text(new Rect(30, 52, 1000, 30), "The vegetables for the pots.", 15, new Color(0.7f, 0.66f, 0.6f));
            var board = new Rect(190, 230, 900, 220);
            Fill(board, new Color(0.45f, 0.32f, 0.2f));
            Outline(board, new Color(0.3f, 0.2f, 0.12f), 3);
            for (int i = 0; i < 6; i++) Fill(new Rect(board.x + 10, board.y + 20 + i * 34, board.width - 20, 1), new Color(0.38f, 0.26f, 0.16f));

            // Each gap between chops becomes a piece of that width: a steady hand makes even pieces.
            const float pxPerSecond = 80f;
            float x = board.x + 60, y = board.y + 90, h = 40;
            var veg = Ramp(0, VegStops);
            foreach (var gap in g.ChopGaps)
            {
                float w = Mathf.Clamp((float)gap * pxPerSecond, 4f, 70f);
                Fill(new Rect(x, y, w - 3, h), veg);
                Fill(new Rect(x, y, w - 3, 5), Color.Lerp(veg, Color.white, 0.25f));
                x += w;
            }
            // The rest of the vegetable, still whole.
            float rest = Mathf.Max(60, board.xMax - 60 - x);
            Fill(new Rect(x, y, rest, h), veg);
            Fill(new Rect(x + rest, y + 6, 18, h - 12), veg);
            Fill(new Rect(x + rest + 18, y + 12, 30, 6), new Color(0.3f, 0.6f, 0.2f));
            Fill(new Rect(x + rest + 18, y + 22, 26, 6), new Color(0.3f, 0.6f, 0.2f));

            // The knife at the cut.
            bool justCut = _held.Contains(KeyCode.Space);
            float ky = justCut ? y - 30 : y - 90;
            Fill(new Rect(x - 4, ky, 6, 90), new Color(0.82f, 0.84f, 0.88f));
            Fill(new Rect(x - 7, ky - 50, 12, 50), new Color(0.3f, 0.2f, 0.12f));

            if (Pressed(board)) g.Chop();
        }

        // ---- cook

        private void DrawCook(CookingMinigame g)
        {
            // The coal bed.
            var coals = new Rect(30, CoalsY, W - 60, 50);
            Fill(coals, new Color(0.14f, 0.08f, 0.06f));
            for (int i = 0; i < 60; i++)
            {
                float n = Noise(Time.time * 0.8f + i * 0.37f, i);
                Fill(new Rect(coals.x + i * (coals.width / 60f), coals.y + 6 + (i % 3) * 10, coals.width / 60f - 2, 10), new Color(0.6f + 0.4f * n, 0.15f + 0.2f * n, 0.04f));
            }

            for (int i = 0; i < g.Dishes.Count; i++)
            {
                var d = g.Dishes[i];
                var col = SlotRect(i, g.Dishes.Count);
                if (Pressed(new Rect(col.x, col.y, col.width, col.height))) g.Select(i);
                if (i == g.Selected)
                {
                    Outline(new Rect(col.x + 4, col.y, col.width - 8, CoalsY - col.y + 54), new Color(0.9f, 0.72f, 0.45f, 0.55f), 2);
                }
                Text(new Rect(col.x, CoalsY + 56, col.width, 24), $"<b>{i + 1}</b>  {d.Recipe.Name}", 14,
                    i == g.Selected ? new Color(1f, 0.85f, 0.6f) : new Color(0.7f, 0.65f, 0.6f), TextAnchor.UpperCenter);
                Text(new Rect(col.x, CoalsY + 74, col.width, 22), CookTuning.HerbName(d.Herb), 12, new Color(0.55f, 0.52f, 0.48f), TextAnchor.UpperCenter);

                if (d.State == DishState.Cooking) DrawFlames(d, col);
                switch (d.State)
                {
                    case DishState.Cooking:
                        float y = HeightFor(d.Heat);
                        if (d.Recipe.Station == StationKind.Spit) DrawSpit(d, col, y);
                        else if (d.Recipe.Station == StationKind.Griddle) DrawGriddle(d, col, y);
                        else DrawPot(d, col, y);
                        break;
                    case DishState.Resting:
                        DrawBoard(d, col, carved: false);
                        break;
                    case DishState.Served:
                        DrawServed(d, col);
                        break;
                }
            }
        }

        /// <summary>The dish hangs lower over the coals the hotter it is. That height is the only heat reading.</summary>
        private static float HeightFor(Heat h) => h == Heat.High ? CoalsY - 70 : h == Heat.Medium ? CoalsY - 150 : CoalsY - 240;

        private void DrawFlames(Dish d, Rect col)
        {
            float strength = (float)(0.6 + 0.6 * d.Wobble / 0.25) * 0.5f + 0.5f; // a wandering fire flares and sinks
            float cx = col.center.x;
            for (int i = 0; i < 7; i++)
            {
                float n = Noise(Time.time * 3f + i * 1.7f + d.Slot * 9, i);
                float h = (20 + 50 * n) * strength;
                Fill(new Rect(cx - 70 + i * 20, CoalsY - h, 14, h), new Color(1f, 0.35f + 0.07f * i, 0.05f, 0.55f));
            }
        }

        private static void Smoke(Rect from, float amount, Color c)
        {
            int n = Mathf.RoundToInt(8 * Mathf.Clamp01(amount));
            for (int i = 0; i < n; i++)
            {
                float t = (Time.time * 0.6f + i * 0.37f) % 1f;
                float wob = (Noise(Time.time + i, i * 3.1f) - 0.5f) * 30;
                var col = c;
                col.a *= 1 - t;
                Fill(new Rect(from.center.x - 10 + wob + (i - n / 2f) * 8, from.y - t * 110, 14 + t * 16, 14 + t * 16), col);
            }
        }

        private void DrawSpit(Dish d, Rect col, float y)
        {
            bool roast = d.Recipe.Rests;
            float w = roast ? 150 : 90, h = roast ? 60 : 30;
            float cx = col.center.x;
            // Posts and rod.
            Fill(new Rect(cx - col.width / 2 + 16, y - 6, 6, CoalsY - y + 6), new Color(0.25f, 0.2f, 0.16f));
            Fill(new Rect(cx + col.width / 2 - 22, y - 6, 6, CoalsY - y + 6), new Color(0.25f, 0.2f, 0.16f));
            Fill(new Rect(cx - col.width / 2 + 14, y - 2, col.width - 28, 4), new Color(0.5f, 0.5f, 0.52f));

            // The meat: the side facing the fire is underneath, the other side on top.
            var body = new Rect(cx - w / 2, y - h / 2, w, h);
            Color top = Ramp((float)d.Sides[1 - d.Facing], MeatStops);
            Color bottom = Ramp((float)d.Sides[d.Facing], MeatStops);
            Fill(new Rect(body.x, body.y, body.width, body.height / 2), top);
            Fill(new Rect(body.x, body.center.y, body.width, body.height / 2), bottom);
            Fill(new Rect(body.x + 6, body.y - 4, body.width - 12, 4), top);
            Fill(new Rect(body.x + 6, body.yMax, body.width - 12, 4), bottom);

            // A roast glistens while it's moist and goes dull and cracked as it dries.
            if (d.Recipe.CanBaste)
            {
                int specks = Mathf.RoundToInt(10 * (float)d.Moisture);
                for (int i = 0; i < specks; i++)
                    Fill(new Rect(body.x + 10 + (i * 37 % (int)(w - 20)), body.y + 4 + (i * 13 % (int)(h / 2 - 6)), 4, 2), new Color(1f, 0.95f, 0.85f, 0.7f));
                if (d.Moisture < 0.45)
                    for (int i = 0; i < 4; i++) Fill(new Rect(body.x + 20 + i * 30, body.y + 8 + (i % 2) * 6, 16, 1), new Color(0.2f, 0.12f, 0.08f));
            }

            float worst = (float)System.Math.Max(d.Sides[0], d.Sides[1]);
            if (worst > CookTuning.CharredAt) Smoke(body, (worst - (float)CookTuning.CharredAt) * 2f, new Color(0.25f, 0.24f, 0.24f, 0.6f));
        }

        private void DrawGriddle(Dish d, Rect col, float y)
        {
            float cx = col.center.x;
            // The stone on its legs.
            var stone = new Rect(cx - 85, y, 170, 16);
            Fill(new Rect(stone.x + 10, stone.yMax, 8, CoalsY - stone.yMax), new Color(0.3f, 0.29f, 0.28f));
            Fill(new Rect(stone.xMax - 18, stone.yMax, 8, CoalsY - stone.yMax), new Color(0.3f, 0.29f, 0.28f));
            Fill(stone, new Color(0.36f, 0.35f, 0.34f));
            // The bread: you see the top; the underside shows only as a thin rim at the edge.
            var bread = new Rect(cx - 60, y - 22, 120, 22);
            Fill(bread, Ramp((float)d.Sides[1 - d.Facing], BreadStops));
            Fill(new Rect(bread.x + 4, bread.yMax - 4, bread.width - 8, 4), Ramp((float)d.Sides[d.Facing], BreadStops));
            Fill(new Rect(bread.x + 8, bread.y - 3, bread.width - 16, 3), Ramp((float)d.Sides[1 - d.Facing], BreadStops));
            float worst = (float)System.Math.Max(d.Sides[0], d.Sides[1]);
            if (worst > CookTuning.CharredAt) Smoke(bread, (worst - (float)CookTuning.CharredAt) * 2f, new Color(0.25f, 0.24f, 0.24f, 0.6f));
        }

        private void DrawPot(Dish d, Rect col, float y)
        {
            float cx = col.center.x;
            // Hook and chain.
            Fill(new Rect(cx - 1, col.y + 10, 3, y - col.y - 10), new Color(0.35f, 0.35f, 0.36f));
            var pot = new Rect(cx - 70, y, 140, 90);
            if (pot.yMax > CoalsY) pot.y = CoalsY - pot.height; // sits on the coals at full heat
            Fill(pot, new Color(0.2f, 0.2f, 0.22f));
            Fill(new Rect(pot.x - 6, pot.y, pot.width + 12, 8), new Color(0.28f, 0.28f, 0.3f));

            // Looking into the pot from just above: the surface.
            var surface = new Rect(pot.x + 8, pot.y - 34, pot.width - 16, 34);
            Color liquid = Ramp((float)d.Parts[0], LiquidStops);
            liquid = Color.Lerp(liquid, new Color(0.1f, 0.07f, 0.05f), (float)d.Scorch * 0.7f);
            Fill(surface, liquid);
            Outline(surface, new Color(0.28f, 0.28f, 0.3f), 3);

            // What's in it: meat chunks and vegetable pieces, coloured by how cooked each is.
            for (int p = 1; p < d.PartsAdded; p++)
            {
                string name = d.Recipe.Parts[p].Name;
                bool meat = name == "meat";
                var c = meat ? Ramp((float)d.Parts[p], MeatStops) : Ramp((float)d.Parts[p], VegStops);
                for (int k = 0; k < 4; k++)
                {
                    float px = surface.x + 12 + ((k * 31 + p * 17) % (int)(surface.width - 30));
                    float py = surface.y + 6 + ((k * 7 + p * 5) % 18);
                    float bob = Mathf.Sin(Time.time * 2f + k + p) * 1.5f;
                    Fill(new Rect(px, py + bob, meat ? 12 : 9, meat ? 9 : 7), c);
                }
            }

            // Scum: grey foam on top.
            int foam = Mathf.RoundToInt(14 * (float)d.Scum);
            for (int k = 0; k < foam; k++)
            {
                float px = surface.x + 6 + (k * 23 % (int)(surface.width - 18));
                float py = surface.y + 4 + (k * 11 % 22);
                Fill(new Rect(px, py, 10, 6), new Color(0.78f, 0.76f, 0.7f, 0.85f));
            }

            // Bubbles: how hard it boils is how hot it is.
            int bubbles = Mathf.RoundToInt(3 + 7 * (float)d.EffectiveHeat);
            for (int k = 0; k < bubbles; k++)
            {
                float t = (Time.time * (0.8f + 0.6f * (float)d.EffectiveHeat) + k * 0.31f) % 1f;
                float px = surface.x + 8 + Noise(k * 1.3f, Mathf.Floor(Time.time + k)) * (surface.width - 16);
                Fill(new Rect(px, surface.y + 4 + (1 - t) * 22, 4 + t * 3, 4 + t * 3), new Color(1f, 1f, 1f, 0.35f * (1 - t)));
            }

            // Steam always; dark smoke when the bottom is catching or it's boiling dry.
            Smoke(surface, 0.4f + 0.2f * (float)d.EffectiveHeat, new Color(0.9f, 0.9f, 0.92f, 0.18f));
            double grace = d.Heat == Heat.High ? CookTuning.ScorchGraceHigh : d.Heat == Heat.Medium ? CookTuning.ScorchGraceMedium : double.MaxValue;
            if (d.SinceStir > grace) Smoke(new Rect(surface.x + 30, surface.y, surface.width - 60, 10), 0.4f + (float)d.Scorch * 2, new Color(0.2f, 0.18f, 0.17f, 0.6f));
            else if (d.Parts[0] > CookTuning.CharredAt) Smoke(surface, (float)(d.Parts[0] - CookTuning.CharredAt) * 2, new Color(0.2f, 0.18f, 0.17f, 0.6f));
        }

        private void DrawBoard(Dish d, Rect col, bool carved)
        {
            float cx = col.center.x;
            var board = new Rect(cx - 90, CoalsY - 40, 180, 26);
            Fill(board, new Color(0.45f, 0.32f, 0.2f));
            var meat = new Rect(cx - 70, board.y - 50, 140, 50);
            Fill(meat, Ramp((float)d.Sides[0], MeatStops));
            Fill(new Rect(meat.x, meat.center.y, meat.width, meat.height / 2), Ramp((float)d.Sides[1], MeatStops));
            // Gentle steam while it rests.
            Smoke(meat, 0.35f, new Color(0.9f, 0.9f, 0.92f, 0.15f));
        }

        private void DrawServed(Dish d, Rect col)
        {
            float cx = col.center.x;
            var plate = new Rect(cx - 80, CoalsY - 30, 160, 18);
            Fill(plate, new Color(0.75f, 0.72f, 0.66f));
            Color food;
            if (d.IsPot) food = Color.Lerp(Ramp((float)d.Parts[0], LiquidStops), Color.black, (float)d.Scorch * 0.7f);
            else if (d.Recipe.Station == StationKind.Griddle) food = Ramp((float)System.Math.Max(d.Sides[0], d.Sides[1]), BreadStops);
            else food = Ramp((float)((d.Sides[0] + d.Sides[1]) / 2), MeatStops);
            Fill(new Rect(cx - 55, plate.y - 22, 110, 22), food);
            Text(new Rect(col.x, plate.y - 60, col.width, 24), d.Ruined ? "ruined" : "served", 13, new Color(0.6f, 0.56f, 0.5f), TextAnchor.UpperCenter);
        }

        // ------------------------------------------------------------------ results screen

        private void DrawResults()
        {
            var res = _game.Result;
            Text(new Rect(40, 20, 1200, 50), "<b>Off the fire</b>", 34, new Color(1f, 0.8f, 0.5f));

            // Left: every dish with its grade. Right: why.
            for (int i = 0; i < res.Dishes.Count; i++)
            {
                var d = res.Dishes[i];
                var r = new Rect(40, 80 + i * 70, 400, 62);
                bool sel = i == _resultPick;
                Fill(r, sel ? new Color(0.24f, 0.19f, 0.15f) : new Color(0.16f, 0.13f, 0.11f));
                Outline(r, sel ? new Color(0.9f, 0.7f, 0.4f) : new Color(0.3f, 0.26f, 0.22f), sel ? 2 : 1);
                Text(new Rect(r.x + 14, r.y + 6, r.width - 20, 30), $"{i + 1}. <b>{d.ItemName}</b>", 17, GradeColor(d.Quality));
                Text(new Rect(r.x + 14, r.y + 32, r.width - 20, 26), $"Quality {d.Quality} · item level {d.Recipe.ItemLevel} · {d.Recipe.Effect}", 13, new Color(0.75f, 0.71f, 0.66f));
                if (Pressed(r)) _resultPick = i;
            }

            var p = res.Dishes[Mathf.Clamp(_resultPick, 0, res.Dishes.Count - 1)];
            string notes = string.Join("\n", p.Notes);
            string flaws = p.Flaws.Count > 0 ? string.Join(", ", p.Flaws) : "none";
            string body =
                $"<b>{p.Recipe.Name}</b>\n\n" +
                $"<b>Score</b>  S = doneness {p.Doneness:0.00} × handling {p.Handling:0.00} × prep {p.Prep:0.00} = <b>{p.Score:0.00}</b>\n" +
                $"<size=14><color=#b9a99a>{HandlingParts(p)}prep = herb {p.HerbScore:0.00}" + (p.Recipe.NeedsChop ? $" × chop (evenness {p.ChopScore:0.00})" : "") + "</color></size>\n\n" +
                $"<b>Quality</b>  " + (p.Botch ? "<color=#ff8a70>botched</color> (still edible, no buff)" :
                    $"floor {p.Floor} + (ceiling {p.Ceiling} − {p.Floor}) × clamp({p.Score:0.00} {(p.Roll >= 0 ? "+" : "−")} {System.Math.Abs(p.Roll):0.00}) = <b>{p.Quality}</b>") + "\n" +
                $"<size=14><color=#b9a99a>ceiling set by {p.CeilingReason}; ingredients average {p.InputMeanQuality}</color></size>\n\n" +
                $"<b>How it turned out</b>\n{notes}\n\n" +
                $"<b>Flaws</b>  {flaws}";
            Text(new Rect(480, 80, 760, 520), body, 17, new Color(0.9f, 0.88f, 0.85f));

            Text(new Rect(40, 80 + res.Dishes.Count * 70 + 10, 400, 60),
                $"<b>Skill</b>  {_skillBefore} → {_skill}" + (res.SkillGain == 0 ? "\n<size=13><color=#b9a99a>(no gain: botched, or these dishes are too easy for you)</color></size>" : ""),
                17, new Color(0.9f, 0.88f, 0.85f));

            if (Button(new Rect(40, 640, 260, 52), "<b>Cook again</b> (Enter)", true)) StartCook();
            if (Button(new Rect(320, 640, 260, 52), "Change setup (Esc)")) _mode = Mode.Setup;
            Text(new Rect(600, 650, 600, 30), "1-5 or click a dish to see why it came out that way.", 13, new Color(0.6f, 0.56f, 0.52f));
        }

        private static string HandlingParts(DishResult p)
        {
            var parts = new List<string>();
            if (p.Recipe.Station == StationKind.Pot)
            {
                parts.Add($"scorch {p.Scorch:0.00}");
                if (p.Recipe.HasScum) parts.Add($"scum left {p.ScumLeft:0.00}");
            }
            if (p.Recipe.CanBaste) parts.Add($"moisture {p.Moisture:0.00}");
            if (p.Recipe.Rests && !p.Ruined) parts.Add($"rest {p.RestScore:0.00} (rested {p.Rested:0.0} s)");
            return parts.Count > 0 ? "handling: " + string.Join(", ", parts) + "\n" : "";
        }
    }

    /// <summary>
    /// Safety net: if the Cooking scene opens without its Hearth object (for example a broken script link),
    /// add one so Play still works.
    /// </summary>
    internal static class CookBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureHearth()
        {
            if (SceneManager.GetActiveScene().name != "Cooking") return;
            if (Object.FindAnyObjectByType<CookView>() != null) return;
            new GameObject("Hearth").AddComponent<CookView>();
        }
    }
}
