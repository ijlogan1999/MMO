using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mmo.Crafting.Skinning.View
{
    /// <summary>
    /// Placeholder front end for the skinning minigame, drawn with Unity's immediate-mode GUI (no sprites or packages).
    /// The rules live in SkinMinigame (Core); this class only draws and forwards input.
    ///
    /// A knowledge game with no timing or aiming. The player chooses how to open the animal, feels how the hide holds,
    /// pulls or cuts each patch free working out from the cuts, guts it before the belly sours, hones the knife when it
    /// drags, cleans the hide on the beam and decides how to keep it. Nothing on screen says how a patch holds or what to
    /// do; the steam fading off the body, the belly swelling and the look of the flesh side are the only readings.
    /// The results screen explains what happened.
    /// </summary>
    public sealed class SkinView : MonoBehaviour
    {
        private const float W = 1280f, H = 720f;
        private const string SkillPref = "mmo.skin.skill";
        private const string IntroPref = "mmo.skin.introSeen";

        // The hide grid (GUI units).
        private const float GridX = 160, GridY = 96, CellW = 116, CellH = 86;
        private static float GridW => CellW * SkinTuning.Columns;
        private static float GridH => CellH * SkinTuning.Rows;

        private enum Mode { Intro, Setup, Play, Results }

        private Mode _mode = Mode.Setup;
        private int _introPage;
        private int _speciesIndex = 1;
        private readonly SkinSetup _setup = new SkinSetup();
        private int _skill = 1;
        private SkinMinigame _game;
        private SkinAudio _audio;
        private int _seed = 1;
        private int _gameSeed;
        private int _skillBefore;

        private SkinTool _tool = SkinTool.Feel;
        private FleshTool _fleshTool = FleshTool.Scrape;
        private int _selRow = 2, _selCol = 2;
        private string _caption = "";
        private float _captionAt = -10f;
        private Vector2 _lastMouse = new Vector2(-1, -1);

        private readonly HashSet<KeyCode> _held = new HashSet<KeyCode>();
        private GUIStyle _style;

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            EnsureCameraAndListener();
            _audio = gameObject.AddComponent<SkinAudio>();
            _skill = Mathf.Clamp(PlayerPrefs.GetInt(SkillPref, 1), 1, 100);
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
            cam.backgroundColor = new Color(0.08f, 0.09f, 0.07f);
            if (FindAnyObjectByType<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) _held.Clear();
        }

        private void StartSkinning()
        {
            _setup.Species = Species.All[_speciesIndex];
            _setup.SkillLevel = _skill;
            _gameSeed = _seed++;
            _game = new SkinMinigame(_setup, _gameSeed);
            _tool = SkinTool.Feel;
            _fleshTool = FleshTool.Scrape;
            _selRow = 2;
            _selCol = 2;
            _caption = "";
            _skillBefore = _skill;
            _mode = Mode.Play;
        }

        private void Update()
        {
            if (_mode != Mode.Play || _game == null) return;
            DrainEvents();
            if (_game.Stage == SkinStage.Done) Finish();
        }

        private void Finish()
        {
            var r = _game.Result;
            _skill = Mathf.Min(100, _skill + r.SkillGain);
            PlayerPrefs.SetInt(SkillPref, _skill);
            _mode = Mode.Results;
        }

        private void DrainEvents()
        {
            foreach (var e in _game.Events)
            {
                _audio.Play(e);
                if (e.Kind == SkinEventKind.StageChanged || e.Kind == SkinEventKind.Done) continue;
                Say(e.Message + ".");
            }
            _game.Events.Clear();
        }

        private void Say(string text)
        {
            _caption = text;
            _captionAt = Time.time;
        }

        // ------------------------------------------------------------------ input

        private void HandleInput(Event e)
        {
            if (e.type == EventType.KeyDown && e.keyCode != KeyCode.None)
            {
                bool first = _held.Add(e.keyCode);
                if (first && OnKeyDown(e.keyCode)) e.Use();
            }
            else if (e.type == EventType.KeyUp)
            {
                _held.Remove(e.keyCode);
            }

            if (_mode != Mode.Play || _game == null) return;
            if (_game.Stage != SkinStage.Skin && _game.Stage != SkinStage.Flesh) return;

            var m = e.mousePosition;
            bool moved = m != _lastMouse;
            _lastMouse = m;
            var hit = PatchAtPoint(m);
            if (moved && hit != null) { _selRow = hit.Row; _selCol = hit.Col; }
            if (e.type == EventType.MouseDown && e.button == 0 && hit != null)
            {
                _selRow = hit.Row;
                _selCol = hit.Col;
                UseTool();
                e.Use();
            }
        }

        private Patch PatchAtPoint(Vector2 m)
        {
            if (m.x < GridX || m.y < GridY || m.x >= GridX + GridW || m.y >= GridY + GridH) return null;
            int col = (int)((m.x - GridX) / CellW), row = (int)((m.y - GridY) / CellH);
            return _game.PatchAt(row, col);
        }

        private bool OnKeyDown(KeyCode key)
        {
            switch (_mode)
            {
                case Mode.Intro:
                    if (key == KeyCode.Space || key == KeyCode.Return || key == KeyCode.KeypadEnter || key == KeyCode.RightArrow) { NextIntroPage(); return true; }
                    if (key == KeyCode.LeftArrow || key == KeyCode.Backspace) { _introPage = Mathf.Max(0, _introPage - 1); return true; }
                    if (key == KeyCode.Escape) { CloseIntro(); return true; }
                    return false;
                case Mode.Setup:
                    if (key == KeyCode.Return || key == KeyCode.KeypadEnter) { StartSkinning(); return true; }
                    return false;
                case Mode.Results:
                    if (key == KeyCode.Return || key == KeyCode.KeypadEnter || key == KeyCode.Space) { StartSkinning(); return true; }
                    if (key == KeyCode.Escape) { _mode = Mode.Setup; return true; }
                    return false;
            }

            if (key == KeyCode.Escape) { _mode = Mode.Setup; return true; }
            var g = _game;
            bool k1 = key == KeyCode.Alpha1 || key == KeyCode.Keypad1;
            bool k2 = key == KeyCode.Alpha2 || key == KeyCode.Keypad2;
            bool k3 = key == KeyCode.Alpha3 || key == KeyCode.Keypad3;

            switch (g.Stage)
            {
                case SkinStage.Open:
                    if (k1) { g.ChooseStyle(SkinStyle.Open); DrainEvents(); return true; }
                    if (k2) { g.ChooseStyle(SkinStyle.Cased); DrainEvents(); return true; }
                    return false;
                case SkinStage.Skin:
                    if (k1) { _tool = SkinTool.Feel; return true; }
                    if (k2) { _tool = SkinTool.Pull; return true; }
                    if (k3) { _tool = SkinTool.Knife; return true; }
                    if (key == KeyCode.H) { g.Hone(); DrainEvents(); return true; }
                    if (key == KeyCode.G) { g.Gut(); DrainEvents(); return true; }
                    if (key == KeyCode.E) { g.StopSkinning(); DrainEvents(); return true; }
                    break;
                case SkinStage.Flesh:
                    if (k1) { _fleshTool = FleshTool.Scrape; return true; }
                    if (k2) { _fleshTool = FleshTool.Trim; return true; }
                    if (key == KeyCode.Return || key == KeyCode.KeypadEnter) { g.FinishFleshing(); return true; }
                    break;
                case SkinStage.Preserve:
                    if (k1) { g.Keep(Preserve.Salt); return true; }
                    if (k2) { g.Keep(Preserve.Stretch); return true; }
                    if (k3) { g.Keep(Preserve.Green); return true; }
                    return false;
            }

            if (key == KeyCode.Space) { UseTool(); return true; }
            if (key == KeyCode.LeftArrow || key == KeyCode.A) { MoveSelection(-1, 0); return true; }
            if (key == KeyCode.RightArrow || key == KeyCode.D) { MoveSelection(1, 0); return true; }
            if (key == KeyCode.UpArrow || key == KeyCode.W) { MoveSelection(0, -1); return true; }
            if (key == KeyCode.DownArrow || key == KeyCode.S) { MoveSelection(0, 1); return true; }
            return false;
        }

        /// <summary>Arrow keys step to the next patch in that direction, skipping the gaps around the legs.</summary>
        private void MoveSelection(int dx, int dy)
        {
            int r = _selRow, c = _selCol;
            for (int i = 0; i < 6; i++)
            {
                r += dy;
                c += dx;
                if (r < 0 || r >= SkinTuning.Rows || c < 0 || c >= SkinTuning.Columns) return;
                if (_game.PatchAt(r, c) != null) { _selRow = r; _selCol = c; return; }
                // Moving up or down into a gap: slide toward the middle column.
                if (dy != 0)
                {
                    int toward = c < 2 ? 1 : c > 2 ? -1 : 0;
                    for (int cc = c; cc >= 0 && cc < SkinTuning.Columns; cc += toward == 0 ? 1 : toward)
                    {
                        if (_game.PatchAt(r, cc) != null) { _selRow = r; _selCol = cc; return; }
                        if (toward == 0) break;
                    }
                }
            }
        }

        private void UseTool()
        {
            var p = _game.PatchAt(_selRow, _selCol);
            if (p == null) return;
            if (_game.Stage == SkinStage.Skin) _game.Work(_tool, p.Id);
            else if (_game.Stage == SkinStage.Flesh) _game.FleshWork(_fleshTool, p.Id);
            DrainEvents();
        }

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
            Fill(new Rect(0, 0, W, H), new Color(0.1f, 0.11f, 0.085f));

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

        private static void Dots(Vector2 a, Vector2 b, Color c, float size = 2f, float spacing = 2f)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / spacing));
            for (int i = 0; i <= n; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)n);
                Fill(new Rect(p.x - size / 2, p.y - size / 2, size, size), c);
            }
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
            Fill(r, !enabled ? new Color(0.18f, 0.18f, 0.17f) : selected ? new Color(0.33f, 0.33f, 0.2f) : new Color(0.21f, 0.22f, 0.19f));
            Outline(r, selected ? new Color(0.9f, 0.8f, 0.45f) : new Color(0.4f, 0.4f, 0.35f), selected ? 2f : 1f);
            Text(r, label, 16, enabled ? Color.white : new Color(0.5f, 0.5f, 0.5f), TextAnchor.MiddleCenter);
            return enabled && Pressed(r);
        }

        private static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        private static Color GradeColor(int q)
        {
            if (q >= 80) return new Color(1f, 0.75f, 0.25f);
            if (q >= 60) return new Color(0.45f, 0.75f, 1f);
            if (q >= 40) return new Color(0.85f, 0.85f, 0.85f);
            if (q >= 20) return new Color(0.7f, 0.6f, 0.5f);
            return new Color(0.6f, 0.45f, 0.45f);
        }

        private static (Color Back, Color Belly) FurColors(Species sp)
        {
            if (sp == Species.Hare) return (new Color(0.58f, 0.48f, 0.36f), new Color(0.86f, 0.83f, 0.77f));
            if (sp == Species.Deer) return (new Color(0.55f, 0.37f, 0.22f), new Color(0.88f, 0.85f, 0.78f));
            if (sp == Species.Boar) return (new Color(0.27f, 0.22f, 0.18f), new Color(0.38f, 0.32f, 0.27f));
            if (sp == Species.Wolf) return (new Color(0.47f, 0.46f, 0.44f), new Color(0.8f, 0.76f, 0.66f));
            return (new Color(0.24f, 0.16f, 0.11f), new Color(0.3f, 0.21f, 0.15f));
        }

        private static readonly Color Muscle = new Color(0.55f, 0.17f, 0.15f);

        private static string SeasonName(Season s) => s.ToString().ToLowerInvariant();

        // ------------------------------------------------------------------ intro
        // The old hunter teaches the craft (how hide behaves, what each tool is for), never a recipe per animal.

        private static readonly (string Title, string Body)[] IntroPages =
        {
            ("Before the knife",
             "You've made the kill. The work starts now, and the body won't wait for you.\n\n" +
             "Warm hide comes away easy. Every moment you spend, the body cools and the hide holds harder. Watch the " +
             "steam coming off it: when it's gone, you're late. Cold meat comes off with the hide and has to be cut away after.\n\n" +
             "And the guts. Left in, they sour the meat. Big bodies keep their heat and sour fastest; small ones cool " +
             "quick and keep. If the belly starts to swell, you've waited too long. Gutting takes a while, mind."),
            ("Opening",
             "Two ways to open an animal.\n\n" +
             "Open: slit the belly from throat to tail and down the inside of each leg. The hide comes off flat. That's " +
             "how you take anything big; there's no other way to get it off.\n\n" +
             "Cased: cut only across the hind legs, under the tail, and peel the skin forward like pulling off a sleeve. " +
             "A furrier pays more for a pelt that's still a whole tube. Try it on anything big and it tears. And if you " +
             "slit the belly to gut it before the skin is off, the tube's gone."),
            ("Working it off",
             "Work from an open edge: where you cut, or where hide is already off. Anywhere else you're tearing or cutting blind.\n\n" +
             "Feel it first if you're unsure. Hide that slides, take off with your fist. Hide that clings stretches if you " +
             "pull it; cut it free. Hide that's held tight tears; cut it free. Fat holds a hide like glue, and only lets go to the knife. " +
             "But don't take a blade to loose hide: it flaps against the edge and you score it.\n\n" +
             "Cut around a wound; pull it and it rips wide open. Some hide under the fur.\n\n" +
             "Fat and hair dull an edge fast. When the knife starts to drag, hone it. A dull blade skips and scores."),
            ("On the beam",
             "Lay the hide flesh side up and clean it.\n\n" +
             "Scrape off the film and the fat. Trim meat away with the knife; a scraper just skids over it. " +
             "Don't scrape hide that's already clean, you'll thin it, and where the skin is thin you'll go straight through. " +
             "Hold it to the light: thin skin glows. Don't take the knife to film, it bites the hide.\n\n" +
             "Anything left on rots, and the tan won't take."),
            ("Keeping it",
             "A hide for leather wants salt; it keeps supple until the tanner gets it. A pelt wants the stretcher, fur in, " +
             "to dry; salt stiffens fur.\n\n" +
             "In summer nothing dries clean: flies and slipping hair. In real cold, you can roll a hide green and carry it " +
             "to the tanner as it is. Any warmer and it turns.\n\n" +
             "That's all I'll tell you. The animals will teach you the rest. Go on."),
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
            var card = new Rect(190, 50, 900, 610);
            Fill(card, new Color(0.14f, 0.14f, 0.12f));
            Outline(card, new Color(0.42f, 0.4f, 0.3f), 2);
            Text(new Rect(card.x + 50, card.y + 30, 800, 40), "The old hunter", 16, new Color(0.62f, 0.6f, 0.5f));
            Text(new Rect(card.x + 50, card.y + 56, 800, 60), $"<b>{page.Title}</b>", 34, new Color(0.95f, 0.82f, 0.5f));
            Text(new Rect(card.x + 50, card.y + 120, 800, 420), page.Body, 20, new Color(0.92f, 0.9f, 0.84f));
            Text(new Rect(card.x + 50, card.yMax - 50, 300, 30), $"{_introPage + 1} / {IntroPages.Length}", 15, new Color(0.58f, 0.56f, 0.48f));

            bool last = _introPage == IntroPages.Length - 1;
            if (_introPage > 0 && Button(new Rect(card.xMax - 430, card.yMax - 70, 180, 48), "Back")) _introPage--;
            if (Button(new Rect(card.xMax - 230, card.yMax - 70, 180, 48), last ? "<b>To the kill</b>" : "<b>Next</b>", true)) NextIntroPage();
            Text(new Rect(0, H - 40, W, 30), "SPACE next · LEFT back · ESC skip", 14, new Color(0.5f, 0.48f, 0.45f), TextAnchor.MiddleCenter);
        }

        // ------------------------------------------------------------------ setup screen

        private void DrawSetup()
        {
            Text(new Rect(40, 20, 1200, 50), "Skin  <size=20><color=#a9a294>skinning prototype</color></size>", 36, new Color(0.95f, 0.82f, 0.5f), bold: true);

            Text(new Rect(40, 80, 360, 30), "The kill", 18, new Color(0.8f, 0.76f, 0.7f));
            for (int i = 0; i < Species.All.Length; i++)
            {
                var s = Species.All[i];
                if (Button(new Rect(40, 110 + i * 54, 360, 46), $"{s.Name}  <size=13>(skill {s.RequiredSkill}+)</size>", i == _speciesIndex))
                    _speciesIndex = i;
            }
            var sp = Species.All[_speciesIndex];
            string gives = sp.HideName + (sp.Meat > 0 ? $", {sp.Meat} {sp.MeatName.ToLowerInvariant()}" : "") + (sp.Extras.Length > 0 ? $", {sp.Extras}" : "");
            Text(new Rect(40, 386, 360, 90), $"{sp.Description}\n<b>Gives:</b> {gives}", 15, new Color(0.85f, 0.82f, 0.78f));

            float x = 460;
            Text(new Rect(x, 80, 700, 30), $"Skinning skill: <b>{_skill}</b> ({SkinTuning.RankFor(_skill)})", 18, Color.white);
            int newSkill = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x, 115, 500, 20), _skill, 1, 100));
            if (newSkill != _skill) { _skill = newSkill; PlayerPrefs.SetInt(SkillPref, _skill); }
            Text(new Rect(x, 135, 780, 40), "Test slider. Skill sharpens your senses (more feels, more wounds spotted, earlier warnings) and raises the quality cap.", 13, new Color(0.65f, 0.62f, 0.6f));
            if (_skill < sp.RequiredSkill)
                Text(new Rect(x, 172, 780, 24), $"Below this animal's rank ({sp.RequiredSkill}). You can still practise on it.", 14, new Color(1f, 0.6f, 0.4f));

            Text(new Rect(x, 200, 400, 26), "Skinning knife", 18, new Color(0.8f, 0.76f, 0.7f));
            for (int i = 0; i < Knife.All.Length; i++)
            {
                var k = Knife.All[i];
                if (Button(new Rect(x + i * 260, 228, 250, 36), k.Name, _setup.Knife == k)) _setup.Knife = k;
            }
            Text(new Rect(x, 266, 780, 22), _setup.Knife.Description, 13, new Color(0.65f, 0.62f, 0.6f));

            Text(new Rect(x, 300, 200, 26), "Season", 18, new Color(0.8f, 0.76f, 0.7f));
            var seasons = new[] { Season.Spring, Season.Summer, Season.Autumn, Season.Winter };
            for (int i = 0; i < seasons.Length; i++)
                if (Button(new Rect(x + i * 195, 328, 185, 34), seasons[i].ToString(), _setup.Season == seasons[i])) _setup.Season = seasons[i];

            Text(new Rect(x, 382, 200, 30), "Animal grade", 15, Color.white);
            _setup.AnimalGrade = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x + 200, 390, 300, 20), _setup.AnimalGrade, 1, 100));
            Text(new Rect(x + 520, 382, 300, 30), $"{_setup.AnimalGrade}  <size=13><color=#a6a09a>age and health; hidden in game</color></size>", 15, GradeColor(_setup.AnimalGrade));

            Text(new Rect(x, 420, 200, 30), "Wounds from the fight", 15, Color.white);
            _setup.Wounds = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x + 200, 428, 300, 20), _setup.Wounds, 0, 6));
            Text(new Rect(x + 520, 420, 300, 30), $"{_setup.Wounds}  <size=13><color=#a6a09a>test slider; comes from combat in game</color></size>", 15, Color.white);

            Text(new Rect(x, 470, 780, 120),
                "Hunting is ordinary combat. This is what happens after: the animal is down and the hide, meat and the rest " +
                "are yours if you take them well. Hides go to the tanner (2 hides and 1 bark make leather; a hare skin counts half, " +
                "a bear pelt double).", 14, new Color(0.75f, 0.72f, 0.68f));

            if (Button(new Rect(x, 630, 300, 56), "<b>Start skinning</b>  <size=13>(Enter)</size>", true)) StartSkinning();
            if (Button(new Rect(40, 630, 360, 56), "Hear the old hunter again")) { _introPage = 0; _mode = Mode.Intro; }
        }

        // ------------------------------------------------------------------ play screen

        private void DrawPlay()
        {
            var g = _game;
            Text(new Rect(30, 14, 900, 40),
                $"<b>{g.Species.Name}</b>  <size=16><color=#a9a294>{SeasonName(g.Setup.Season)}</color></size>", 26, new Color(0.95f, 0.82f, 0.5f));

            switch (g.Stage)
            {
                case SkinStage.Open: DrawCarcass(g); DrawOpenChoice(g); break;
                case SkinStage.Skin: DrawCarcass(g); DrawKit(g); break;
                case SkinStage.Flesh: DrawBeam(g); DrawBeamKit(g); break;
                case SkinStage.Preserve: DrawBeam(g); DrawKeepChoice(g); break;
            }

            if (_caption.Length > 0 && Time.time - _captionAt < 6f)
            {
                float a = Mathf.Clamp01(1.5f - (Time.time - _captionAt) / 4f);
                Text(new Rect(GridX - 60, GridY + GridH + 14, GridW + 120, 50), _caption, 18, new Color(0.95f, 0.92f, 0.85f, a), TextAnchor.UpperCenter);
            }
            DrawControls(g);
        }

        private void DrawControls(SkinMinigame g)
        {
            string keys = g.Stage switch
            {
                SkinStage.Open => "<b>1</b> open (belly and legs)   <b>2</b> cased (hind legs only)",
                SkinStage.Skin => "<b>1</b> feel  <b>2</b> pull  <b>3</b> knife   <b>MOUSE</b>/<b>ARROWS</b> choose   <b>CLICK</b>/<b>SPACE</b> use   <b>H</b> hone   <b>G</b> gut   <b>E</b> stop",
                SkinStage.Flesh => "<b>1</b> scrape  <b>2</b> trim (knife)   <b>MOUSE</b>/<b>ARROWS</b> choose   <b>CLICK</b>/<b>SPACE</b> use   <b>ENTER</b> done",
                SkinStage.Preserve => "<b>1</b> salt   <b>2</b> stretch and dry   <b>3</b> roll it green",
                _ => "",
            };
            var bar = new Rect(0, H - 40, W, 40);
            Fill(bar, new Color(0.055f, 0.06f, 0.045f));
            Text(new Rect(30, bar.y, W - 60, bar.height), keys + "      <color=#7d746c>ESC leave</color>", 15, new Color(0.85f, 0.82f, 0.78f), TextAnchor.MiddleLeft);
        }

        private static Rect CellRect(int row, int col) => new Rect(GridX + col * CellW, GridY + row * CellH, CellW, CellH);

        /// <summary>The animal on its back, hide side up, laid out flat: neck at the top, legs at the corners.</summary>
        private void DrawCarcass(SkinMinigame g)
        {
            var (back, belly) = FurColors(g.Species);
            // Ground under the carcass.
            Fill(new Rect(GridX - 30, GridY - 30, GridW + 60, GridH + 40), new Color(0.15f, 0.17f, 0.11f));

            foreach (var p in g.Patches) DrawHidePatch(g, p, back, belly);
            DrawCuts(g);
            DrawSwelling(g);

            var sel = g.PatchAt(_selRow, _selCol);
            if (sel != null && g.Stage == SkinStage.Skin) Outline(CellRect(sel.Row, sel.Col), new Color(0.95f, 0.95f, 0.9f, 0.85f), 2);

            DrawSteam(g);
        }

        private void DrawHidePatch(SkinMinigame g, Patch p, Color back, Color belly)
        {
            var r = CellRect(p.Row, p.Col);
            var inner = new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2);
            var rng = new System.Random(_gameSeed * 31 + p.Id * 977);

            if (p.State != PatchState.OnBody)
            {
                // Hide off: bare muscle, darker once cold.
                float cold = (float)(1 - g.Warmth);
                Fill(inner, Shade(Muscle, 1f - 0.3f * cold));
                for (int i = 0; i < 6; i++)
                    Fill(new Rect(inner.x + 4, inner.y + 6 + i * 13 + (float)rng.NextDouble() * 4, inner.width - 8, 1), Shade(Muscle, 0.75f));
                if (p.Fat) for (int i = 0; i < 8; i++)
                        Fill(new Rect(inner.x + (float)rng.NextDouble() * (inner.width - 20), inner.y + (float)rng.NextDouble() * (inner.height - 10), 18, 8), new Color(0.93f, 0.88f, 0.7f));
                if (p.State == PatchState.Torn)
                    for (int i = 0; i < 5; i++) // shreds of hide left stuck to the body
                        Fill(new Rect(inner.x + (float)rng.NextDouble() * (inner.width - 16), inner.y + (float)rng.NextDouble() * (inner.height - 12), 14, 10), back);
                return;
            }

            // Fur: lighter along the belly line, darker toward the back.
            var fur = p.Col == 2 ? belly : p.Region == Region.Ribs || p.Region == Region.Chest ? Color.Lerp(back, belly, 0.35f) : back;
            Fill(inner, fur);
            for (int i = 0; i < 40; i++)
            {
                float sx = inner.x + (float)rng.NextDouble() * (inner.width - 2), sy = inner.y + (float)rng.NextDouble() * (inner.height - 6);
                Fill(new Rect(sx, sy, 1, 4 + rng.Next(4)), Shade(fur, 0.8f + 0.35f * (float)rng.NextDouble()));
            }
            // Wounds the skinner has seen: a dark hole with dried blood.
            if (p.Wound && p.WoundSeen)
            {
                float cx = inner.x + 20 + (float)rng.NextDouble() * (inner.width - 40), cy = inner.y + 20 + (float)rng.NextDouble() * (inner.height - 40);
                Fill(new Rect(cx - 9, cy - 7, 18, 14), new Color(0.35f, 0.06f, 0.05f, 0.8f));
                Fill(new Rect(cx - 4, cy - 3, 8, 6), new Color(0.08f, 0.02f, 0.02f));
            }

            // Chalked feel: what the fingers said, when they said it.
            if (p.Felt.HasValue)
            {
                string word = p.Felt.Value switch { Hold.Loose => "slides", Hold.Clinging => "clings", _ => "tight" };
                if (p.FeltFat) word += ", fat";
                Text(new Rect(inner.x + 4, inner.y + 2, inner.width - 8, 22), word, 13, new Color(0.08f, 0.08f, 0.08f, 0.9f));
            }
        }

        /// <summary>The opening cuts: a red line through each cut patch.</summary>
        private void DrawCuts(SkinMinigame g)
        {
            var cut = new Color(0.6f, 0.08f, 0.06f);
            foreach (var p in g.Patches)
            {
                if (!g.IsOpened(p) || p.State != PatchState.OnBody) continue;
                var r = CellRect(p.Row, p.Col);
                bool across = g.Style == SkinStyle.Cased && p.Region == Region.Hindleg;
                if (across) Fill(new Rect(r.x + 4, r.center.y - 2, r.width - 8, 4), cut);
                else Fill(new Rect(r.center.x - 2, r.y + (p.Row == 0 ? r.height / 2 : 0), 4, p.Row == 0 ? r.height / 2 : r.height), cut);
            }
        }

        /// <summary>A souring belly swells. Only drawn once the skinner would notice it.</summary>
        private void DrawSwelling(SkinMinigame g)
        {
            float s = (float)g.Swelling;
            if (s <= 0) return;
            var top = CellRect(2, 2);
            var c = new Color(0.95f, 0.92f, 0.85f, 0.18f + 0.3f * s);
            float grow = 6 + 16 * s;
            var rect = new Rect(top.x - grow, top.y + 10, top.width + 2 * grow, CellH * 3 - 20);
            Fill(rect, c);
            Outline(rect, new Color(0.3f, 0.25f, 0.2f, 0.4f + 0.4f * s), 2);
        }

        /// <summary>Steam off the warm body: the only reading of how warm it still is.</summary>
        private void DrawSteam(SkinMinigame g)
        {
            float w = (float)g.Warmth;
            if (w <= 0.02f) return;
            int n = Mathf.RoundToInt(40 * w);
            for (int i = 0; i < n; i++)
            {
                float seed = i * 12.9898f;
                float x = GridX + 40 + Mathf.Repeat(seed * 37.1f, GridW - 80);
                float life = Mathf.Repeat(Time.time * 0.35f + seed, 1f);
                float y = GridY + GridH * 0.7f - life * (GridH * 0.9f);
                x += Mathf.Sin(Time.time * 1.3f + seed) * 10;
                float a = (1 - life) * 0.22f * w;
                Fill(new Rect(x, y, 10, 8), new Color(0.95f, 0.95f, 0.95f, a));
            }
        }

        private void DrawOpenChoice(SkinMinigame g)
        {
            var panel = new Rect(830, 96, 420, 440);
            Fill(panel, new Color(0.13f, 0.14f, 0.11f));
            Outline(panel, new Color(0.3f, 0.3f, 0.25f), 1);
            Text(new Rect(panel.x + 18, panel.y + 14, 380, 30), "<b>How will you open it?</b>", 19, new Color(0.9f, 0.85f, 0.7f));
            if (Button(new Rect(panel.x + 18, panel.y + 60, 384, 52), "<b>1</b>  Open: belly and legs"))
            { g.ChooseStyle(SkinStyle.Open); DrainEvents(); }
            Text(new Rect(panel.x + 22, panel.y + 116, 376, 60), "Slit from throat to tail and down the inside of each leg. The hide comes off flat.", 14, new Color(0.75f, 0.72f, 0.68f));
            if (Button(new Rect(panel.x + 18, panel.y + 190, 384, 52), "<b>2</b>  Cased: hind legs only"))
            { g.ChooseStyle(SkinStyle.Cased); DrainEvents(); }
            Text(new Rect(panel.x + 22, panel.y + 246, 376, 60), "Cut across the hind legs under the tail and peel it forward like a sleeve. The pelt stays a tube.", 14, new Color(0.75f, 0.72f, 0.68f));
            Text(new Rect(panel.x + 22, panel.y + 330, 376, 90), "Look it over first. Wounds you can see show through the fur.", 14, new Color(0.65f, 0.62f, 0.6f));
        }

        /// <summary>The skinner's kit: tools, patience left for feeling, hone, gut.</summary>
        private void DrawKit(SkinMinigame g)
        {
            var panel = new Rect(830, 96, 420, 440);
            Fill(panel, new Color(0.13f, 0.14f, 0.11f));
            Outline(panel, new Color(0.3f, 0.3f, 0.25f), 1);
            float y = panel.y + 14;
            var tools = new[] { (SkinTool.Feel, "1  Feel"), (SkinTool.Pull, "2  Pull"), (SkinTool.Knife, "3  Knife") };
            foreach (var (tool, label) in tools)
            {
                if (Button(new Rect(panel.x + 18, y, 384, 40), label, _tool == tool)) _tool = tool;
                y += 46;
            }
            y += 8;
            Text(new Rect(panel.x + 20, y, 380, 24), $"Feels left in your patience: <b>{g.FeelsLeft}</b>", 15, Color.white); y += 30;
            Text(new Rect(panel.x + 20, y, 380, 24), g.Gutted ? "Gutted." : "Not gutted yet.", 15, new Color(0.85f, 0.82f, 0.78f)); y += 34;
            if (Button(new Rect(panel.x + 18, y, 124, 40), "H  Hone")) { g.Hone(); DrainEvents(); }
            if (Button(new Rect(panel.x + 148, y, 124, 40), "G  Gut", false, !g.Gutted)) { g.Gut(); DrainEvents(); }
            if (Button(new Rect(panel.x + 278, y, 124, 40), "E  Stop")) { g.StopSkinning(); DrainEvents(); }
        }

        // ------------------------------------------------------------------ the beam

        private static Color LayerColor(Layer l) => l switch
        {
            Layer.Clean => new Color(0.84f, 0.84f, 0.8f),
            Layer.Membrane => new Color(0.86f, 0.7f, 0.68f),
            Layer.Fat => new Color(0.95f, 0.9f, 0.66f),
            _ => new Color(0.6f, 0.16f, 0.13f),
        };

        /// <summary>The hide on the beam, flesh side up, held to the light.</summary>
        private void DrawBeam(SkinMinigame g)
        {
            Fill(new Rect(GridX - 30, GridY - 30, GridW + 60, GridH + 40), new Color(0.2f, 0.15f, 0.1f));
            foreach (var p in g.Patches)
            {
                var r = CellRect(p.Row, p.Col);
                var inner = new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2);
                var rng = new System.Random(_gameSeed * 17 + p.Id * 389);
                if (!p.IsOff)
                {
                    Outline(inner, new Color(0.35f, 0.28f, 0.2f), 1);
                    continue;
                }

                var baseCol = LayerColor(Layer.Clean);
                // Thin skin glows with the light behind it.
                if (p.Thin) baseCol = Color.Lerp(baseCol, new Color(1f, 0.97f, 0.9f), 0.45f);
                Fill(inner, baseCol);

                switch (p.Layer)
                {
                    case Layer.Membrane:
                        Fill(new Rect(inner.x + 4, inner.y + 4, inner.width - 8, inner.height - 8), new Color(0.86f, 0.66f, 0.64f, 0.75f));
                        for (int i = 0; i < 6; i++)
                            Fill(new Rect(inner.x + 8 + (float)rng.NextDouble() * (inner.width - 30), inner.y + 8 + (float)rng.NextDouble() * (inner.height - 16), 18, 1), new Color(1f, 1f, 1f, 0.5f));
                        break;
                    case Layer.Fat:
                        for (int i = 0; i < 14; i++)
                            Fill(new Rect(inner.x + 4 + (float)rng.NextDouble() * (inner.width - 30), inner.y + 4 + (float)rng.NextDouble() * (inner.height - 20), 16 + rng.Next(12), 10 + rng.Next(8)), LayerColor(Layer.Fat));
                        break;
                    case Layer.Meat:
                        for (int i = 0; i < 9; i++)
                            Fill(new Rect(inner.x + 4 + (float)rng.NextDouble() * (inner.width - 30), inner.y + 4 + (float)rng.NextDouble() * (inner.height - 20), 12 + rng.Next(14), 8 + rng.Next(8)), LayerColor(Layer.Meat));
                        break;
                }

                // Damage from the skinning and the beam, as it would look.
                if (p.State == PatchState.Torn || p.Mark == FleshMark.Torn)
                {
                    var a = new Vector2(inner.x + 12, inner.y + 14 + (float)rng.NextDouble() * 20);
                    var b = new Vector2(inner.xMax - 12, inner.yMax - 14 - (float)rng.NextDouble() * 20);
                    Dots(a, b, new Color(0.1f, 0.08f, 0.06f), 5f, 3f);
                }
                if (p.State == PatchState.Scored || p.Mark == FleshMark.Scored)
                    Dots(new Vector2(inner.x + 20, inner.center.y + 10), new Vector2(inner.xMax - 30, inner.center.y - 8), new Color(0.35f, 0.1f, 0.08f), 2f, 2f);
                if (p.State == PatchState.Holed)
                    Fill(new Rect(inner.center.x - 6, inner.center.y - 5, 12, 10), new Color(0.1f, 0.08f, 0.06f));
                if (p.State == PatchState.Stretched)
                    for (int i = 0; i < 3; i++) Fill(new Rect(inner.x + 10, inner.y + 20 + i * 18, inner.width - 20, 1), new Color(0.6f, 0.55f, 0.5f, 0.6f));
                if (p.Mark == FleshMark.Thinned)
                    Fill(new Rect(inner.x + 18, inner.y + 18, inner.width - 36, inner.height - 36), new Color(1f, 1f, 0.95f, 0.45f));
            }

            var sel = g.PatchAt(_selRow, _selCol);
            if (sel != null && g.Stage == SkinStage.Flesh) Outline(CellRect(sel.Row, sel.Col), new Color(0.2f, 0.2f, 0.2f, 0.9f), 3);
        }

        private void DrawBeamKit(SkinMinigame g)
        {
            var panel = new Rect(830, 96, 420, 440);
            Fill(panel, new Color(0.13f, 0.14f, 0.11f));
            Outline(panel, new Color(0.3f, 0.3f, 0.25f), 1);
            Text(new Rect(panel.x + 18, panel.y + 14, 380, 30), "<b>On the beam</b>, flesh side up", 18, new Color(0.9f, 0.85f, 0.7f));
            if (Button(new Rect(panel.x + 18, panel.y + 56, 384, 40), "1  Scrape", _fleshTool == FleshTool.Scrape)) _fleshTool = FleshTool.Scrape;
            if (Button(new Rect(panel.x + 18, panel.y + 102, 384, 40), "2  Trim with the knife", _fleshTool == FleshTool.Trim)) _fleshTool = FleshTool.Trim;
            if (Button(new Rect(panel.x + 18, panel.yMax - 66, 384, 48), "<b>Done</b> (Enter)", true)) g.FinishFleshing();
        }

        private void DrawKeepChoice(SkinMinigame g)
        {
            var panel = new Rect(830, 96, 420, 440);
            Fill(panel, new Color(0.13f, 0.14f, 0.11f));
            Outline(panel, new Color(0.3f, 0.3f, 0.25f), 1);
            Text(new Rect(panel.x + 18, panel.y + 14, 380, 50), $"<b>How will you keep the {g.Species.HideName.ToLowerInvariant()}?</b>\nIt's {SeasonName(g.Setup.Season)}.", 18, new Color(0.9f, 0.85f, 0.7f));
            if (Button(new Rect(panel.x + 18, panel.y + 90, 384, 48), "1  Salt it")) g.Keep(Preserve.Salt);
            if (Button(new Rect(panel.x + 18, panel.y + 150, 384, 48), "2  Stretch it and dry it")) g.Keep(Preserve.Stretch);
            if (Button(new Rect(panel.x + 18, panel.y + 210, 384, 48), "3  Roll it green for the tanner")) g.Keep(Preserve.Green);
        }

        // ------------------------------------------------------------------ results screen

        private void DrawResults()
        {
            var r = _game.Result;
            var g = _game;
            var gc = r.HideQuality > 0 ? GradeColor(r.HideQuality) : new Color(0.7f, 0.65f, 0.6f);
            string title = r.HideQuality > 0 ? $"{r.HideItem} · {r.HideGrade} ({r.HideQuality})" : r.HideItem;
            Text(new Rect(40, 18, 1200, 50), $"<b>{title}</b>", 34, gc);
            string meat = r.MeatItem.Length > 0 ? $"{r.MeatItem} × {r.MeatYield} · {r.MeatGrade} ({r.MeatQuality})" : "";
            string units = r.HideQuality > 0 ? $"counts as {r.HideUnits:0.#} hide at the tannery" : "too little came off whole to make a hide";
            Text(new Rect(40, 62, 1200, 30), $"{units}" + (meat.Length > 0 ? $"   ·   {meat}" : "") + (r.Extras.Length > 0 ? $"   ·   {r.Extras}" : ""), 17, Color.white);

            string dim(string s) => $"<size=14><color=#a9a294>{s}</color></size>";
            string capNote = r.HideRaw > r.Ceiling ? $" → capped at <b>{r.Ceiling}</b> by {r.CeilingReason}" : "";
            string style = r.Style == SkinStyle.Open ? "opened" : r.TubeKept ? "cased, the tube whole" : "cased, but slit to gut";
            string body =
                $"<b>Skinning</b>  {style} · {r.Clean} clean, {r.Stretched} stretched, {r.Scored} scored, {r.Holed} cut round a wound, {r.Torn} torn, {r.Left} left on\n" +
                dim($"work done {r.TimeUsed} · hones {r.Hones} · wounds the cuts ran through {r.WoundsAbsorbed}") + "\n" +
                $"<b>Gutting</b>  " + (g.Species.Meat == 0 ? "no meat on this one" : r.GuttedLate ? $"<color=#ff9a80>late</color> (at {r.GutAt}; it held until {r.SpoilPoint})" : $"in time (at {r.GutAt}; it held until {r.SpoilPoint})") + "\n" +
                $"<b>On the beam</b>  clean {r.LayerClean}, film left {r.LayerMembrane}, fat left {r.LayerFat}, meat left {r.LayerMeat} · thinned {r.Thinned}, cut {r.ScoredOnBeam}, holed {r.TornOnBeam}\n" +
                $"<b>Kept</b>  {KeepName(r.Preserve)} ×{r.PreserveFactor:0.00}\n\n" +
                $"<b>Hide</b>  grade {r.AnimalGrade} × (0.4 + 0.6 × skinning {r.SkinScore:0.00} × beam {r.FleshScore:0.00}) × style {r.StyleFactor:0.00} × kept {r.PreserveFactor:0.00} × season {r.SeasonHide:0.00} = {r.HideRaw}{capNote}\n" +
                (g.Species.Meat > 0 ? $"<b>Meat</b>  grade {r.AnimalGrade} × gutting {r.MeatFactor:0.00} × season {r.SeasonMeat:0.00} = {r.MeatRaw}" + (r.MeatRaw > r.MeatQuality ? $" → capped at {r.MeatQuality} by skill" : "") + "\n" : "") +
                $"\n<b>Skill</b>  {_skillBefore} → {_skill}" + (r.SkillGain == 0 ? "  " + dim("(no gain: no hide came of it, or this animal is too easy for you)") : "") + "\n\n" +
                "<b>What went wrong</b>\n" + (r.Notes.Count > 0 ? string.Join("\n", r.Notes) : "Nothing. That was clean work.");
            Text(new Rect(40, 100, 780, 530), body, 15, new Color(0.9f, 0.88f, 0.85f));

            DrawTruthMap(g, new Rect(860, 110, 380, 380 * GridH / GridW));

            if (Button(new Rect(40, 640, 260, 50), "<b>Skin another</b> (Enter)", true)) StartSkinning();
            if (Button(new Rect(320, 640, 260, 50), "Change setup (Esc)")) _mode = Mode.Setup;
        }

        private static string KeepName(Preserve p) => p switch
        {
            Preserve.Salt => "salted",
            Preserve.Stretch => "stretched and dried",
            Preserve.Green => "rolled green",
            _ => "not kept",
        };

        /// <summary>How the hide really held on this animal, and what happened to each patch.</summary>
        private void DrawTruthMap(SkinMinigame g, Rect map)
        {
            float cw = map.width / SkinTuning.Columns, ch = map.height / SkinTuning.Rows;
            foreach (var p in g.Patches)
            {
                var r = new Rect(map.x + p.Col * cw + 1, map.y + p.Row * ch + 1, cw - 2, ch - 2);
                Color c = p.BaseHold switch
                {
                    Hold.Loose => new Color(0.35f, 0.55f, 0.35f),
                    Hold.Clinging => new Color(0.65f, 0.55f, 0.25f),
                    _ => new Color(0.6f, 0.3f, 0.25f),
                };
                Fill(r, c);
                if (p.Fat) Outline(r, new Color(0.95f, 0.9f, 0.66f), 3);
                string hold = p.BaseHold switch { Hold.Loose => "loose", Hold.Clinging => "clings", _ => "tight" };
                string state = p.State switch
                {
                    PatchState.Clean => "clean",
                    PatchState.Stretched => "stretched",
                    PatchState.Scored => "scored",
                    PatchState.Holed => "holed",
                    PatchState.Torn => "TORN",
                    _ => "left on",
                };
                string extra = (p.Fat ? " fat" : "") + (p.Thin ? " thin" : "") + (p.Wound ? " wound" : "");
                Text(r, $"<size=11>{hold}{extra}\n<b>{state}</b></size>", 11, Color.white, TextAnchor.MiddleCenter);
            }
            Text(new Rect(map.x, map.yMax + 8, map.width, 90),
                "How the hide held while the body was warm (green loose, yellow clings, red tight; pale border = fat), and how each patch came off. " +
                "A cooling body holds everything one step harder, and harder still once it sets.",
                13, new Color(0.8f, 0.78f, 0.75f));
        }
    }

    /// <summary>
    /// Safety net: if the Skinning scene opens without its Skinner object (for example a broken script link),
    /// add one so Play still works.
    /// </summary>
    internal static class SkinBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureSkinner()
        {
            if (SceneManager.GetActiveScene().name != "Skinning") return;
            if (Object.FindAnyObjectByType<SkinView>() != null) return;
            new GameObject("Skinner").AddComponent<SkinView>();
        }
    }
}
