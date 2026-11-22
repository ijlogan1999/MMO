using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mmo.Crafting.Logging.View
{
    /// <summary>
    /// Placeholder front end for the logging minigame, drawn with Unity's immediate-mode GUI (no sprites or packages).
    /// The rules live in LogMinigame (Core); this class only draws and forwards input.
    ///
    /// A knowledge game with no timing or aiming: the player reads each tree (lean, crown, rot signs, a knock), picks a
    /// line for it to fall along, judges the notch and the back cut by eye, wedges it over, bucks the stem by how it
    /// lies, and decides what each length is worth. Nothing on screen says which tree is rotten, where it will land
    /// or how deep to cut; the results screen explains what happened.
    /// </summary>
    public sealed class LogView : MonoBehaviour
    {
        private const float W = 1280f, H = 720f;
        private const string SkillPref = "mmo.log.skill";
        private const string IntroPref = "mmo.log.introSeen.v1";

        // The clearing map (GUI units).
        private const float MapX = 30, MapY = 64, MapScale = 14f;
        private static float MapW => (float)LogTuning.ClearingW * MapScale;
        private static float MapH => (float)LogTuning.ClearingH * MapScale;
        private static Rect MapRect => new Rect(MapX, MapY, MapW, MapH);

        // The stump close-up (notch and back cut).
        private static readonly Rect StumpRect = new Rect(110, 130, 520, 400);
        private const float TrunkW = 420f;

        // Bucking.
        private const float LogX = 60, LogY = 260, LogSpan = 1160;

        private enum Mode { Intro, Setup, Play, Results }

        private Mode _mode = Mode.Setup;
        private int _introPage;
        private int _standIndex;
        private readonly LogSetup _setup = new LogSetup();
        private int _skill = 1;
        private LogMinigame _game;
        private LogAudio _audio;
        private int _seed = 1;
        private int _gameSeed;
        private int _skillBefore;

        private int _mark = 1;        // bucking: the boundary the saw is on
        private int _selPiece;        // yard: the length in hand
        private readonly Dictionary<int, double> _sighted = new Dictionary<int, double>();
        private string _caption = "";
        private float _captionAt = -10f;
        private float _fallAt = -10f;
        private Vector2 _lastMouse = new Vector2(-1, -1);

        private readonly HashSet<KeyCode> _held = new HashSet<KeyCode>();
        private GUIStyle _style;

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            EnsureCameraAndListener();
            _audio = gameObject.AddComponent<LogAudio>();
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
            cam.backgroundColor = new Color(0.06f, 0.07f, 0.05f);
            if (FindAnyObjectByType<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) _held.Clear();
        }

        private void StartLogging()
        {
            _setup.Stand = StandType.All[_standIndex];
            _setup.SkillLevel = _skill;
            _gameSeed = _seed++;
            _game = new LogMinigame(_setup, _gameSeed);
            _mark = 1;
            _selPiece = 0;
            _sighted.Clear();
            _caption = "";
            _fallAt = -10f;
            _skillBefore = _skill;
            _mode = Mode.Play;
        }

        private void Update()
        {
            if (_mode != Mode.Play || _game == null) return;
            DrainEvents();
            if (_game.Stage == LogStage.Done) Finish();
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
                switch (e.Kind)
                {
                    case LogEventKind.Selected:
                    case LogEventKind.Aimed:
                    case LogEventKind.Sighted:
                    case LogEventKind.Chopped:
                    case LogEventKind.Sawed:
                    case LogEventKind.StageChanged:
                    case LogEventKind.Done:
                        break;
                    case LogEventKind.Tapped:
                        Say(e.Message == "ring" ? "It rings clear." : e.Message == "boom" ? "It booms, deep and hollow." : "It sounds dull and dead.");
                        break;
                    case LogEventKind.Fell:
                        _fallAt = Time.time;
                        Say(e.Message);
                        break;
                    default:
                        Say(e.Message + ".");
                        break;
                }
            }
            _game.Events.Clear();
        }

        private void Say(string text)
        {
            _caption = text;
            _captionAt = Time.time;
        }

        private bool Falling => _game != null && Time.time - _fallAt < 1.8f;

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

            if (_mode != Mode.Play || _game == null || Falling) return;
            var m = e.mousePosition;
            bool moved = m != _lastMouse;
            _lastMouse = m;
            bool click = e.type == EventType.MouseDown && e.button == 0;

            switch (_game.Stage)
            {
                case LogStage.Survey:
                    if (click && MapRect.Contains(m))
                    {
                        int hit = TreeAt(m);
                        if (hit >= 0) { _game.Select(hit); e.Use(); }
                    }
                    break;
                case LogStage.Aim:
                    if (click && MapRect.Contains(m))
                    {
                        var t = _game.Tree;
                        var p = MapPoint(t.X, t.Y);
                        if (Vector2.Distance(m, p) > 8)
                        {
                            _game.SetAim(LogTuning.DirOf(m.x - p.x, m.y - p.y));
                            e.Use();
                        }
                    }
                    break;
                case LogStage.Buck:
                {
                    int n = _game.Stem.Count;
                    var area = new Rect(LogX - 20, LogY - 60, LogSpan + 40, 200);
                    if ((moved || click) && area.Contains(m))
                    {
                        _mark = Mathf.Clamp(Mathf.RoundToInt((m.x - LogX) / UnitPx), 1, n - 1);
                        if (click) e.Use();
                    }
                    break;
                }
                case LogStage.Yard:
                {
                    int hit = PieceAt(m);
                    if (moved && hit >= 0) _selPiece = hit;
                    if (click && hit >= 0)
                    {
                        _selPiece = hit;
                        CycleUse(hit);
                        e.Use();
                    }
                    break;
                }
            }
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
                    if (key == KeyCode.Return || key == KeyCode.KeypadEnter) { StartLogging(); return true; }
                    return false;
                case Mode.Results:
                    if (key == KeyCode.Return || key == KeyCode.KeypadEnter || key == KeyCode.Space) { StartLogging(); return true; }
                    if (key == KeyCode.Escape) { _mode = Mode.Setup; return true; }
                    return false;
            }

            if (key == KeyCode.Escape) { _mode = Mode.Setup; return true; }
            if (Falling) return false;
            var g = _game;
            bool enter = key == KeyCode.Return || key == KeyCode.KeypadEnter;

            switch (g.Stage)
            {
                case LogStage.Survey:
                    if (key == KeyCode.LeftArrow || key == KeyCode.A || key == KeyCode.UpArrow || key == KeyCode.W) { CycleTree(-1); return true; }
                    if (key == KeyCode.RightArrow || key == KeyCode.D || key == KeyCode.DownArrow || key == KeyCode.S) { CycleTree(1); return true; }
                    if (key == KeyCode.K) { g.Tap(g.Selected); return true; }
                    if (enter) { g.ChooseTree(); return true; }
                    break;
                case LogStage.Aim:
                    if (key == KeyCode.LeftArrow || key == KeyCode.A) { g.SetAim(g.Aim - 1); return true; }
                    if (key == KeyCode.RightArrow || key == KeyCode.D) { g.SetAim(g.Aim + 1); return true; }
                    if (key == KeyCode.H) { _sighted[g.Selected] = g.SightReach(g.Selected); return true; }
                    if (key == KeyCode.Backspace) { g.BackToSurvey(); return true; }
                    if (enter) { g.ConfirmAim(); return true; }
                    break;
                case LogStage.Notch:
                    if (key == KeyCode.Space) { g.Chop(); return true; }
                    if (key == KeyCode.Backspace) { g.BackToAim(); return true; }
                    if (enter) { g.FinishNotch(); return true; }
                    if (key == KeyCode.E) { g.WalkAway(); return true; }
                    break;
                case LogStage.BackCut:
                    if (key == KeyCode.UpArrow || key == KeyCode.W) { g.SetBackHeight(Higher(g.BackHeight, 1)); return true; }
                    if (key == KeyCode.DownArrow || key == KeyCode.S) { g.SetBackHeight(Higher(g.BackHeight, -1)); return true; }
                    if (key == KeyCode.Space) { g.Saw(); return true; }
                    if (key == KeyCode.Q) { g.DriveWedge(); return true; }
                    if (key == KeyCode.E) { g.WalkAway(); return true; }
                    break;
                case LogStage.Buck:
                {
                    int n = g.Stem.Count;
                    if (key == KeyCode.LeftArrow || key == KeyCode.A) { _mark = Mathf.Max(1, _mark - 1); return true; }
                    if (key == KeyCode.RightArrow || key == KeyCode.D) { _mark = Mathf.Min(n - 1, _mark + 1); return true; }
                    if (key == KeyCode.Alpha1 || key == KeyCode.Keypad1) { g.Buck(_mark, BuckSide.Top); return true; }
                    if (key == KeyCode.Alpha2 || key == KeyCode.Keypad2) { g.Buck(_mark, BuckSide.Under); return true; }
                    if (key == KeyCode.K) { g.TapStem(_mark); return true; }
                    if (enter) { g.FinishBucking(); _selPiece = 0; return true; }
                    break;
                }
                case LogStage.Yard:
                {
                    int n = g.Pieces.Count;
                    if (key == KeyCode.LeftArrow || key == KeyCode.A || key == KeyCode.UpArrow) { if (n > 0) _selPiece = (_selPiece + n - 1) % n; return true; }
                    if (key == KeyCode.RightArrow || key == KeyCode.D || key == KeyCode.DownArrow) { if (n > 0) _selPiece = (_selPiece + 1) % n; return true; }
                    if (key == KeyCode.Space) { CycleUse(_selPiece); return true; }
                    if (enter) { g.FinishYard(); return true; }
                    break;
                }
            }
            return false;
        }

        private static BackCutHeight Higher(BackCutHeight h, int by) =>
            (BackCutHeight)Mathf.Clamp((int)h + by, (int)BackCutHeight.Below, (int)BackCutHeight.High);

        private void CycleUse(int i)
        {
            var before = _game.Pieces[i].Use;
            _game.CycleUse(i);
            if (_game.Pieces[i].Use == PieceUse.Firewood && before != PieceUse.Firewood) _audio.PlaySplit();
        }

        /// <summary>Arrow keys walk the stand west to east.</summary>
        private void CycleTree(int by)
        {
            var order = new List<Tree>(_game.Trees);
            order.Sort((a, b) => a.X.CompareTo(b.X));
            int at = order.FindIndex(t => t.Id == _game.Selected);
            _game.Select(order[(at + by + order.Count) % order.Count].Id);
        }

        private int TreeAt(Vector2 m)
        {
            int best = -1;
            float bestD = float.MaxValue;
            foreach (var t in _game.Trees)
            {
                if (t.Felled) continue;
                float d = Vector2.Distance(m, MapPoint(t.X, t.Y));
                if (d < (float)t.CrownRadius * MapScale && d < bestD) { best = t.Id; bestD = d; }
            }
            return best;
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
            Fill(new Rect(0, 0, W, H), new Color(0.075f, 0.085f, 0.065f));

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

        private static void Line(Vector2 a, Vector2 b, Color c, float size = 2f, float spacing = 0f)
        {
            if (spacing <= 0) spacing = Mathf.Max(1f, size * 0.6f);
            int n = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / spacing));
            for (int i = 0; i <= n; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)n);
                Fill(new Rect(p.x - size / 2, p.y - size / 2, size, size), c);
            }
        }

        private static void Disc(Vector2 c, float r, Color col, float strip = 2f)
        {
            for (float dy = -r; dy < r; dy += strip)
            {
                float y = dy + strip / 2;
                float w = Mathf.Sqrt(Mathf.Max(0, r * r - y * y));
                Fill(new Rect(c.x - w, c.y + dy, 2 * w, strip), col);
            }
        }

        private static void Ellipse(Rect r, Color col)
        {
            float rx = r.width / 2, ry = r.height / 2;
            for (float dy = -ry; dy < ry; dy += 2)
            {
                float y = (dy + 1) / ry;
                float w = rx * Mathf.Sqrt(Mathf.Max(0, 1 - y * y));
                Fill(new Rect(r.center.x - w, r.center.y + dy, 2 * w, 2), col);
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
            Fill(r, !enabled ? new Color(0.16f, 0.17f, 0.15f) : selected ? new Color(0.3f, 0.34f, 0.2f) : new Color(0.19f, 0.21f, 0.18f));
            Outline(r, selected ? new Color(0.85f, 0.85f, 0.45f) : new Color(0.36f, 0.4f, 0.33f), selected ? 2f : 1f);
            Text(r, label, 16, enabled ? Color.white : new Color(0.5f, 0.5f, 0.5f), TextAnchor.MiddleCenter);
            return enabled && Pressed(r);
        }

        private static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

        private static Color GradeColor(int q)
        {
            if (q >= 81) return new Color(1f, 0.75f, 0.25f);
            if (q >= 61) return new Color(0.45f, 0.75f, 1f);
            if (q >= 41) return new Color(0.85f, 0.85f, 0.85f);
            if (q >= 21) return new Color(0.7f, 0.6f, 0.5f);
            return new Color(0.6f, 0.45f, 0.45f);
        }

        private static Vector2 MapPoint(double x, double y) => new Vector2(MapX + (float)x * MapScale, MapY + (float)y * MapScale);

        private static Vector2 DirVec(int dir)
        {
            var v = LogTuning.Vec(dir);
            return new Vector2((float)v.X, (float)v.Y);
        }

        private Color BarkColor => _game.Stand.Species switch
        {
            Species.Birch => new Color(0.86f, 0.85f, 0.8f),
            Species.Oak => new Color(0.36f, 0.31f, 0.25f),
            _ => new Color(0.48f, 0.33f, 0.24f),
        };

        private Color CrownColor => _game.Stand.Species switch
        {
            Species.Birch => new Color(0.42f, 0.6f, 0.3f),
            Species.Oak => new Color(0.24f, 0.4f, 0.2f),
            _ => new Color(0.16f, 0.33f, 0.22f),
        };

        private static readonly Color Wood = new Color(0.86f, 0.72f, 0.5f);
        private static readonly Color Chalk = new Color(0.96f, 0.96f, 0.9f, 0.85f);

        // ------------------------------------------------------------------ intro
        // The old forester teaches how trees behave, never the answer for a given tree.

        private static readonly (string Title, string Body)[] IntroPages =
        {
            ("Choosing a tree",
             "So you want to cut timber. Listen once, then learn with your hands.\n\n" +
             "Walk round a tree before you touch it. Look at it from two sides: which way it leans, which side of the " +
             "crown is heavy. Look for the clear, straight stems without branch stubs low down; knots and crooks spoil timber.\n\n" +
             "Rot hides inside. Bracket fungus on the bark, woodpecker holes and a dead top are its marks. Moss, ivy and " +
             "burls are just bark business. When you're not sure, knock the trunk with the back of the axe: sound wood " +
             "rings, rotten wood sounds dull, a hollow tree booms. Your arm has only so many knocks in it."),
            ("Laying it down",
             "Decide where it will lie before you cut. It needs a clear lane as long as the tree is tall: a tree that " +
             "falls into another's crown hangs up there, and one that reaches the forest edge does the same. Across a rock " +
             "it cracks. Straight down a steep slope it hits hard and breaks. Sight its height with your axe handle if you " +
             "can't judge it.\n\n" +
             "A tree wants to go where it leans, where its crown is heavy and where the wind pushes it. You can steer it " +
             "across that, but not straight against it without wedges, and not far."),
            ("The notch and the hinge",
             "Chop the notch on the side it should fall, a quarter to a third of the way in. Too shallow and it won't " +
             "steer, and a tree pulling hard forward can split right up the trunk. Too deep and the hinge is too short to hold.\n\n" +
             "Then saw in from the far side, a little above the floor of the notch, never below it. Stop while there's a " +
             "strip of wood left between the cuts, about a tenth of the trunk. That strip is the hinge: it steers the tree " +
             "down. Saw through it and the tree goes where it likes. Leave it too thick and it tears the butt.\n\n" +
             "If it won't go, drive a wedge. If it leans back at you, set a wedge early, before it sits down on your saw."),
            ("Bucking and the yard",
             "Once it's down, cut it into lengths. A buyer takes timber four metres or longer, sound and straight. Rot runs " +
             "up from the butt; look at the cut ends, or knock along it. Cut crooks and cracks out.\n\n" +
             "Look at how it lies. Where it bridges a dip it sags, and a cut from the top closes on your saw: cut from " +
             "beneath. Where an end hangs out past what holds it, cut from the top. On the ground, from the top.\n\n" +
             "Then stack your timber, peel it if you want the bark for the tanners, split the rest for firewood. All of " +
             "it costs strength, and you only have so much in a day. Now go and fell one."),
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
            Fill(card, new Color(0.12f, 0.135f, 0.11f));
            Outline(card, new Color(0.38f, 0.42f, 0.3f), 2);
            Text(new Rect(card.x + 50, card.y + 30, 800, 40), "The old forester", 16, new Color(0.6f, 0.64f, 0.5f));
            Text(new Rect(card.x + 50, card.y + 56, 800, 60), $"<b>{page.Title}</b>", 34, new Color(0.9f, 0.85f, 0.5f));
            Text(new Rect(card.x + 50, card.y + 118, 800, 420), page.Body, 19, new Color(0.9f, 0.9f, 0.84f));
            Text(new Rect(card.x + 50, card.yMax - 50, 300, 30), $"{_introPage + 1} / {IntroPages.Length}", 15, new Color(0.55f, 0.58f, 0.48f));

            bool last = _introPage == IntroPages.Length - 1;
            if (_introPage > 0 && Button(new Rect(card.xMax - 430, card.yMax - 70, 180, 48), "Back")) _introPage--;
            if (Button(new Rect(card.xMax - 230, card.yMax - 70, 180, 48), last ? "<b>To the woods</b>" : "<b>Next</b>", true)) NextIntroPage();
            Text(new Rect(0, H - 40, W, 30), "SPACE next · LEFT back · ESC skip", 14, new Color(0.48f, 0.5f, 0.44f), TextAnchor.MiddleCenter);
        }

        // ------------------------------------------------------------------ setup screen

        private void DrawSetup()
        {
            Text(new Rect(40, 20, 1200, 50), "Fell  <size=20><color=#a3a894>logging prototype</color></size>", 36, new Color(0.9f, 0.85f, 0.5f), bold: true);

            Text(new Rect(40, 80, 360, 30), "Stand", 18, new Color(0.78f, 0.8f, 0.7f));
            for (int i = 0; i < StandType.All.Length; i++)
            {
                var s = StandType.All[i];
                if (Button(new Rect(40, 110 + i * 56, 360, 48), $"{s.Name}  <size=13>(skill {s.RequiredSkill}+)</size>", i == _standIndex))
                    _standIndex = i;
            }
            var stand = StandType.All[_standIndex];
            Text(new Rect(40, 290, 360, 120), $"{stand.Description}\n<b>Gives:</b> {stand.TimberName}, {stand.BarkName.ToLowerInvariant()} (peeled), firewood", 15, new Color(0.84f, 0.85f, 0.78f));

            float x = 460;
            Text(new Rect(x, 80, 700, 30), $"Logging skill: <b>{_skill}</b> ({LogTuning.RankFor(_skill)})", 18, Color.white);
            int newSkill = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x, 115, 500, 20), _skill, 1, 100));
            if (newSkill != _skill) { _skill = newSkill; PlayerPrefs.SetInt(SkillPref, _skill); }
            Text(new Rect(x, 135, 780, 40), "Test slider. Skill sharpens your senses (more rot signs noticed, more knocks, a truer eye for height) and raises the quality cap.", 13, new Color(0.62f, 0.64f, 0.58f));
            if (_skill < stand.RequiredSkill)
                Text(new Rect(x, 172, 780, 24), $"Below this stand's rank ({stand.RequiredSkill}). You can still practise in it.", 14, new Color(1f, 0.6f, 0.4f));

            Text(new Rect(x, 200, 400, 26), "Axe", 18, new Color(0.78f, 0.8f, 0.7f));
            for (int i = 0; i < Axe.All.Length; i++)
            {
                var a = Axe.All[i];
                if (Button(new Rect(x + i * 205, 228, 195, 36), a.Name, _setup.Axe == a)) _setup.Axe = a;
            }
            Text(new Rect(x, 266, 780, 22), _setup.Axe.Description + " A better axe never gives more wood.", 13, new Color(0.62f, 0.64f, 0.58f));

            Text(new Rect(x, 300, 200, 30), "Stand grade", 15, Color.white);
            _setup.StandGrade = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x + 200, 308, 300, 20), _setup.StandGrade, 1, 100));
            Text(new Rect(x + 520, 300, 300, 30), $"{_setup.StandGrade}  <size=13><color=#a0a494>test slider; hidden in game</color></size>", 15, GradeColor(_setup.StandGrade));

            Text(new Rect(x, 350, 780, 200),
                "<b>One tree per trip.</b> Pick it, lay it down, buck it and sort the lengths. " +
                "Timber goes to carpenters and builders, bark to the tanners, firewood to cooks and alchemists.", 15, new Color(0.78f, 0.8f, 0.72f));

            if (Button(new Rect(x, 630, 300, 56), "<b>Into the woods</b>  <size=13>(Enter)</size>", true)) StartLogging();
            if (Button(new Rect(40, 630, 360, 56), "Hear the old forester again")) { _introPage = 0; _mode = Mode.Intro; }
        }

        // ------------------------------------------------------------------ play screen

        private void DrawPlay()
        {
            var g = _game;
            string title = g.Stage switch
            {
                LogStage.Survey => $"<b>{g.Stand.Name}</b>   <size=16><color=#b8bca8>walk the stand</color></size>",
                LogStage.Aim => $"<b>{g.Stand.Name}</b>   <size=16><color=#b8bca8>where will it lie?</color></size>",
                LogStage.Notch => "<b>The notch</b>",
                LogStage.BackCut => "<b>The back cut</b>",
                LogStage.Buck => "<b>Bucking</b>",
                _ => "<b>The yard</b>",
            };
            Text(new Rect(30, 14, 900, 40), title, 26, new Color(0.9f, 0.85f, 0.5f));
            Text(new Rect(W - 330, 18, 300, 30), $"Strength left: <b>{g.Strength:0}</b>", 18, Color.white, TextAnchor.UpperRight);

            if (Falling && g.Stage == LogStage.Buck) DrawFall(g);
            else switch (g.Stage)
            {
                case LogStage.Survey:
                case LogStage.Aim:
                    DrawStand(g);
                    break;
                case LogStage.Notch:
                case LogStage.BackCut:
                    DrawStump(g);
                    break;
                case LogStage.Buck:
                    DrawBuck(g);
                    break;
                case LogStage.Yard:
                    DrawYard(g);
                    break;
            }

            if (_caption.Length > 0 && Time.time - _captionAt < 6f)
            {
                float a = Mathf.Clamp01(1.5f - (Time.time - _captionAt) / 4f);
                Text(new Rect(30, H - 84, W - 60, 34), _caption, 19, new Color(0.96f, 0.94f, 0.85f, a), TextAnchor.MiddleCenter);
            }
            DrawControls(g);
        }

        private void DrawControls(LogMinigame g)
        {
            string keys = Falling ? "" : g.Stage switch
            {
                LogStage.Survey => "<b>MOUSE</b>/<b>ARROWS</b> look at a tree   <b>K</b> knock the trunk   <b>ENTER</b> fell this one",
                LogStage.Aim => "<b>LEFT</b>/<b>RIGHT</b> or <b>CLICK</b> turn your line   <b>H</b> sight its height   <b>ENTER</b> start the notch   <b>BACKSPACE</b> back",
                LogStage.Notch => "<b>SPACE</b> chop   <b>ENTER</b> notch done   <b>BACKSPACE</b> back (before the first chop)   <b>E</b> walk away",
                LogStage.BackCut => "<b>UP</b>/<b>DOWN</b> back cut height (before sawing)   <b>SPACE</b> saw   <b>Q</b> drive a wedge   <b>E</b> walk away",
                LogStage.Buck => "<b>MOUSE</b>/<b>ARROWS</b> move the saw   <b>1</b> saw from the top   <b>2</b> saw from beneath   <b>K</b> knock   <b>ENTER</b> done",
                LogStage.Yard => "<b>MOUSE</b>/<b>ARROWS</b> pick a length   <b>CLICK</b>/<b>SPACE</b> leave · timber · peeled timber · firewood   <b>ENTER</b> done",
                _ => "",
            };
            var bar = new Rect(0, H - 40, W, 40);
            Fill(bar, new Color(0.045f, 0.05f, 0.04f));
            Text(new Rect(30, bar.y, W - 60, bar.height), keys + "      <color=#767c6a>ESC leave</color>", 15, new Color(0.84f, 0.85f, 0.78f), TextAnchor.MiddleLeft);
        }

        // ------------------------------------------------------------------ the stand (survey and aim)

        private void DrawStand(LogMinigame g)
        {
            DrawMap(g, g.Stage == LogStage.Aim);
            DrawTreePanel(g);
        }

        private void DrawMap(LogMinigame g, bool aiming, int fellDir = -1, float fallT = 0f)
        {
            var map = MapRect;
            Fill(map, new Color(0.3f, 0.34f, 0.2f));
            var rng = new System.Random(_gameSeed * 7 + 3);

            // Grass texture.
            for (int i = 0; i < 500; i++)
                Fill(new Rect(map.x + (float)rng.NextDouble() * (map.width - 3), map.y + (float)rng.NextDouble() * (map.height - 2), 3, 2),
                    new Color(0.26f + 0.1f * (float)rng.NextDouble(), 0.32f + 0.08f * (float)rng.NextDouble(), 0.17f));

            // A steep slope shows as contour lines across the fall of the ground, and a stream along its foot.
            var down = DirVec(g.DownhillDir);
            var across = new Vector2(-down.y, down.x);
            var centre = map.center;
            if (g.Steep)
            {
                for (int c = -4; c <= 4; c++)
                {
                    var mid = centre + down * (c * 60f);
                    var a = mid - across * 900; var b = mid + across * 900;
                    DrawClipped(a, b, map, new Color(0.22f, 0.25f, 0.14f, 0.7f));
                }
            }
            var foot = centre + down * Mathf.Max(map.width, map.height) * 0.48f;
            for (int i = -60; i <= 60; i++)
            {
                var p = foot + across * (i * 8f) + down * (Mathf.Sin(i * 0.4f) * 6f);
                if (map.Contains(p)) Fill(new Rect(p.x - 5, p.y - 5, 10, 10), new Color(0.25f, 0.42f, 0.55f, g.Steep ? 0.85f : 0.4f));
            }

            foreach (var r in g.Rocks)
            {
                var p = MapPoint(r.X, r.Y);
                Disc(p, (float)r.R * MapScale, new Color(0.47f, 0.47f, 0.45f));
                Disc(p + new Vector2(-2, -3), (float)r.R * MapScale * 0.6f, new Color(0.56f, 0.56f, 0.53f));
            }

            // The forest edge: dark crowns all round.
            var edge = new System.Random(_gameSeed + 99);
            var dark = new Color(0.08f, 0.16f, 0.1f);
            for (float x = map.x; x < map.xMax; x += 26)
            {
                Disc(new Vector2(x + edge.Next(10), map.y - 4 + edge.Next(8)), 18, dark);
                Disc(new Vector2(x + edge.Next(10), map.yMax + 4 - edge.Next(8)), 18, dark);
            }
            for (float y = map.y; y < map.yMax; y += 26)
            {
                Disc(new Vector2(map.x - 4 + edge.Next(8), y + edge.Next(10)), 18, dark);
                Disc(new Vector2(map.xMax + 4 - edge.Next(8), y + edge.Next(10)), 18, dark);
            }

            // Trees.
            foreach (var t in g.Trees)
            {
                var p = MapPoint(t.X, t.Y);
                if (t.Felled) { Disc(p, (float)t.Diameter / 100f * MapScale * 0.8f + 2, Wood); continue; }
                // The crown sits over the lean and the heavy side.
                var lean = DirVec(t.LeanDir) * (t.LeanStrength * 0.6f) + DirVec(t.CrownDir) * (t.CrownStrength * 0.6f);
                var crown = p + lean * MapScale;
                float cr = (float)t.CrownRadius * MapScale;
                Disc(crown, cr, Shade(CrownColor, 0.85f) * new Color(1, 1, 1, 0.92f), 3f);
                var crng = new System.Random(_gameSeed * 13 + t.Id);
                for (int i = 0; i < 6; i++)
                {
                    var off = new Vector2((float)crng.NextDouble() - 0.5f, (float)crng.NextDouble() - 0.5f) * cr * 1.1f;
                    Disc(crown + off, cr * 0.35f, Shade(CrownColor, 0.95f + 0.2f * (float)crng.NextDouble()), 3f);
                }
                if (t.Signs.Contains(TreeSign.DeadTop))
                    for (int i = 0; i < 4; i++) Line(crown, crown + new Vector2(Mathf.Cos(i * 1.6f), Mathf.Sin(i * 1.6f)) * cr * 0.5f, new Color(0.6f, 0.58f, 0.52f), 2f);
                Disc(p, Mathf.Max(3, (float)t.Diameter / 100f * MapScale * 0.8f), BarkColor);
                if (t.Tapped != TrunkSound.None)
                    Text(new Rect(p.x - 40, p.y + 6, 80, 18), t.Tapped.ToString().ToLowerInvariant(), 12, Chalk, TextAnchor.UpperCenter);
            }

            // Wind: leaves drifting across the clearing.
            if (g.WindStrength > 0)
            {
                var wv = DirVec(g.WindDir);
                float speed = g.WindStrength == 1 ? 40 : 90;
                int count = g.WindStrength == 1 ? 14 : 28;
                for (int i = 0; i < count; i++)
                {
                    float s = i * 17.31f;
                    float along = Mathf.Repeat(Time.time * speed + s * 23f, 900f) - 450f;
                    float side = Mathf.Repeat(s * 41f, 700f) - 350f;
                    var pos = centre + wv * along + new Vector2(-wv.y, wv.x) * side;
                    if (map.Contains(pos)) Fill(new Rect(pos.x, pos.y, 4, 3), new Color(0.75f, 0.62f, 0.25f, 0.85f));
                }
            }

            var sel = g.Tree;
            var sp = MapPoint(sel.X, sel.Y);
            if (!sel.Felled && fellDir < 0) DrawRing(sp, (float)sel.CrownRadius * MapScale + 6, Chalk);

            if (aiming)
            {
                // The line you've chosen, as you'd mark it: a short blaze toward it, nothing more.
                var v = DirVec(g.Aim);
                Line(sp + v * 8, sp + v * (3.5f * MapScale), new Color(1f, 0.95f, 0.75f), 4f);
                var tip = sp + v * (3.5f * MapScale);
                var side = new Vector2(-v.y, v.x);
                Line(tip, tip - v * 10 + side * 7, new Color(1f, 0.95f, 0.75f), 3f);
                Line(tip, tip - v * 10 - side * 7, new Color(1f, 0.95f, 0.75f), 3f);
                if (_sighted.TryGetValue(sel.Id, out double reach))
                {
                    var x = sp + v * ((float)reach * MapScale);
                    Line(x + new Vector2(-6, -6), x + new Vector2(6, 6), Chalk, 3f);
                    Line(x + new Vector2(-6, 6), x + new Vector2(6, -6), Chalk, 3f);
                }
            }

            if (fellDir >= 0)
            {
                var v = DirVec(fellDir);
                float len = (float)sel.Height * MapScale * Mathf.SmoothStep(0, 1, fallT);
                Line(sp, sp + v * len, BarkColor, 7f, 3f);
                if (fallT >= 1f) Disc(sp + v * len, (float)sel.CrownRadius * MapScale * 0.6f, CrownColor, 3f);
            }

            Outline(map, new Color(0.1f, 0.12f, 0.08f), 2);
        }

        private static void DrawClipped(Vector2 a, Vector2 b, Rect clip, Color c)
        {
            int n = Mathf.CeilToInt(Vector2.Distance(a, b) / 6f);
            for (int i = 0; i <= n; i++)
            {
                var p = Vector2.Lerp(a, b, i / (float)n);
                if (i % 2 == 0 && clip.Contains(p)) Fill(new Rect(p.x - 1, p.y - 1, 3, 2), c);
            }
        }

        private static void DrawRing(Vector2 c, float r, Color col)
        {
            int n = Mathf.Max(24, (int)(r * 0.8f));
            for (int i = 0; i < n; i += 2)
            {
                float a = i * Mathf.PI * 2 / n;
                Fill(new Rect(c.x + Mathf.Cos(a) * r - 1, c.y + Mathf.Sin(a) * r - 1, 3, 3), col);
            }
        }

        /// <summary>The tree as you see it walking round it, what you notice on it, and your kit.</summary>
        private void DrawTreePanel(LogMinigame g)
        {
            var t = g.Tree;
            var panel = new Rect(810, 64, 440, 532);
            Fill(panel, new Color(0.11f, 0.125f, 0.1f));
            Outline(panel, new Color(0.28f, 0.31f, 0.24f), 1);

            // Two silhouettes: from the south (west on the left) and from the east (south on the left).
            var leanV = DirVec(t.LeanDir) * t.LeanStrength;
            var crownV = DirVec(t.CrownDir) * t.CrownStrength;
            DrawSilhouette(g, t, new Rect(panel.x + 10, panel.y + 10, 205, 300), leanV.x, crownV.x, "seen from the south", "W", "E");
            DrawSilhouette(g, t, new Rect(panel.x + 225, panel.y + 10, 205, 300), -leanV.y, -crownV.y, "seen from the east", "S", "N");

            float y = panel.y + 322;
            var notes = new List<string>();
            foreach (var s in t.Signs) notes.Add(LogTuning.Describe(s));
            if (t.LowBranches) notes.Add("branch stubs low on the stem");
            string seen = notes.Count > 0 ? "You notice " + string.Join(", ", notes) + "." : "Nothing catches your eye on the bark.";
            Text(new Rect(panel.x + 14, y, panel.width - 28, 70), seen, 15, new Color(0.88f, 0.88f, 0.82f));
            y += 66;
            if (t.Tapped != TrunkSound.None)
                Text(new Rect(panel.x + 14, y, panel.width - 28, 24), "Knocked: " + (t.Tapped == TrunkSound.Ring ? "it rang clear" : t.Tapped == TrunkSound.Boom ? "it boomed" : "it sounded dull"), 15, Chalk);
            y += 28;
            string wind = g.WindStrength == 0 ? "The air is still." : g.WindStrength == 1 ? "A light breeze stirs the leaves." : "A strong wind works the crowns.";
            Text(new Rect(panel.x + 14, y, panel.width - 28, 24), wind, 15, new Color(0.78f, 0.8f, 0.72f));
            y += 30;
            Text(new Rect(panel.x + 14, y, 200, 24), $"Knocks left: <b>{g.TapsLeft}</b>", 15, Color.white);
            y += 32;

            if (g.Stage == LogStage.Survey)
            {
                if (Button(new Rect(panel.x + 14, y, 160, 36), "K  Knock", false, g.TapsLeft > 0 && t.Tapped == TrunkSound.None)) g.Tap(t.Id);
                if (Button(new Rect(panel.x + 186, y, 240, 36), "<b>ENTER  Fell this one</b>", true)) g.ChooseTree();
            }
            else
            {
                if (Button(new Rect(panel.x + 14, y, 160, 36), "H  Sight height")) _sighted[t.Id] = g.SightReach(t.Id);
                if (Button(new Rect(panel.x + 186, y, 240, 36), "<b>ENTER  Start the notch</b>", true)) g.ConfirmAim();
            }
        }

        /// <summary>
        /// A tree as seen from one side: trunk tilted by its lean across the view, crown shifted by its heavy side,
        /// with a figure for scale. Signs, crooks and low stubs show on the stem.
        /// </summary>
        private void DrawSilhouette(LogMinigame g, Tree t, Rect r, float leanSide, float crownSide, string label, string leftWord, string rightWord)
        {
            Fill(r, new Color(0.5f, 0.6f, 0.65f));
            Fill(new Rect(r.x, r.y + r.height * 0.6f, r.width, r.height * 0.4f), new Color(0.56f, 0.64f, 0.66f));
            float groundY = r.yMax - 26;
            Fill(new Rect(r.x, groundY, r.width, 26), new Color(0.26f, 0.3f, 0.18f));
            Text(new Rect(r.x + 6, r.y + 4, r.width - 12, 20), label, 13, new Color(0.15f, 0.17f, 0.18f));
            Text(new Rect(r.x + 6, groundY + 4, 30, 20), leftWord, 13, new Color(0.85f, 0.85f, 0.75f));
            Text(new Rect(r.xMax - 36, groundY + 4, 30, 20), rightWord, 13, new Color(0.85f, 0.85f, 0.75f), TextAnchor.UpperRight);

            float scale = (groundY - r.y - 30) / 27f; // px per metre, the same for every tree
            float baseX = r.center.x - 10;
            var basePt = new Vector2(baseX, groundY);
            float tilt = leanSide * 0.085f; // radians per lean step
            var up = new Vector2(Mathf.Sin(tilt), -Mathf.Cos(tilt));
            float hPx = (float)t.Height * scale;
            float wBase = Mathf.Max(4, (float)t.Diameter / 100f * scale * 1.8f);

            // Crown first, behind the stem.
            float crownR = (float)t.CrownRadius * scale;
            var crownC = basePt + up * (hPx - crownR * 0.9f) + new Vector2(crownSide * crownR * 0.35f, 0);
            bool deadTop = t.Signs.Contains(TreeSign.DeadTop);
            var rng = new System.Random(_gameSeed * 5 + t.Id);
            Disc(crownC, crownR, Shade(CrownColor, 0.85f), 3f);
            for (int i = 0; i < 5; i++)
                Disc(crownC + new Vector2(((float)rng.NextDouble() - 0.5f) * crownR, ((float)rng.NextDouble() - 0.5f) * crownR),
                    crownR * 0.5f, Shade(CrownColor, 0.95f + 0.15f * (float)rng.NextDouble()), 3f);
            // Wind sways the crown a little.
            if (g.WindStrength > 0)
            {
                float sway = Mathf.Sin(Time.time * (g.WindStrength == 1 ? 1.4f : 2.3f)) * g.WindStrength * 1.5f;
                Disc(crownC + new Vector2(sway, 0), crownR * 0.3f, Shade(CrownColor, 1.1f), 3f);
            }

            // The stem, with a kink where it's crooked.
            float crookF = t.CrookAt >= 0 ? (t.CrookAt + 0.5f) / (float)t.Height : -1f;
            int steps = 60;
            Vector2 prev = basePt;
            for (int i = 1; i <= steps; i++)
            {
                float f = i / (float)steps;
                var p = basePt + up * (hPx * f);
                if (crookF > 0 && f > crookF) p += new Vector2(6, 0);
                else if (crookF > 0 && f > crookF - 0.05f) p += new Vector2(6 * (f - (crookF - 0.05f)) / 0.05f, 0);
                float w = Mathf.Lerp(wBase, 1.5f, f);
                Line(prev, p, BarkColor, w, 2f);
                prev = p;
            }
            if (deadTop)
            {
                var top = basePt + up * hPx;
                for (int i = -2; i <= 2; i++) Line(top, top + new Vector2(i * 5, -12 - Mathf.Abs(i) * -2), new Color(0.55f, 0.52f, 0.46f), 2f);
            }

            // Low stubs, and the signs on the bark.
            if (t.LowBranches)
                for (int i = 0; i < 4; i++)
                {
                    var p = basePt + up * (hPx * (0.15f + 0.08f * i));
                    float dir = i % 2 == 0 ? 1 : -1;
                    Line(p, p + new Vector2(dir * (wBase / 2 + 7), -4), BarkColor, 3f);
                }
            foreach (var s in t.Signs)
            {
                switch (s)
                {
                    case TreeSign.Conks:
                        for (int i = 0; i < 3; i++)
                        {
                            var p = basePt + up * (8 + i * 9) + new Vector2(wBase / 2, 0);
                            Fill(new Rect(p.x, p.y, 8, 4), new Color(0.82f, 0.7f, 0.45f));
                            Fill(new Rect(p.x, p.y + 4, 6, 2), new Color(0.5f, 0.38f, 0.22f));
                        }
                        break;
                    case TreeSign.WoodpeckerHoles:
                        for (int i = 0; i < 4; i++)
                        {
                            var p = basePt + up * (hPx * (0.35f + 0.05f * i)) + new Vector2((i % 2 - 0.5f) * 3, 0);
                            Fill(new Rect(p.x - 2, p.y - 2, 4, 4), new Color(0.05f, 0.04f, 0.03f));
                        }
                        break;
                    case TreeSign.Moss:
                        for (int i = 0; i < 6; i++)
                        {
                            var p = basePt + up * (2 + i * 4) + new Vector2(-wBase / 2 - 1, 0);
                            Fill(new Rect(p.x, p.y - 3, 5, 4), new Color(0.35f, 0.55f, 0.2f));
                        }
                        break;
                    case TreeSign.Ivy:
                        for (int i = 0; i < 14; i++)
                        {
                            var p = basePt + up * (i * hPx * 0.035f) + new Vector2((i % 2 == 0 ? -1 : 1) * wBase * 0.4f, 0);
                            Fill(new Rect(p.x - 3, p.y - 3, 6, 5), new Color(0.12f, 0.32f, 0.12f));
                        }
                        break;
                    case TreeSign.Burl:
                        Disc(basePt + up * (hPx * 0.2f) + new Vector2(-wBase / 2, 0), wBase * 0.45f + 3, Shade(BarkColor, 0.85f));
                        break;
                }
            }

            // A figure for scale.
            var man = new Vector2(baseX + 34, groundY);
            float mh = 1.8f * scale;
            Fill(new Rect(man.x - 2, man.y - mh, 4, mh * 0.6f), new Color(0.2f, 0.2f, 0.22f));
            Fill(new Rect(man.x - 3, man.y - mh * 0.45f, 2, mh * 0.45f), new Color(0.2f, 0.2f, 0.22f));
            Fill(new Rect(man.x + 1, man.y - mh * 0.45f, 2, mh * 0.45f), new Color(0.2f, 0.2f, 0.22f));
            Disc(new Vector2(man.x, man.y - mh - 2), 3, new Color(0.2f, 0.2f, 0.22f), 1f);
        }

        // ------------------------------------------------------------------ the stump (notch and back cut)

        private void DrawStump(LogMinigame g)
        {
            var t = g.Tree;
            var r = StumpRect;
            Fill(r, new Color(0.45f, 0.55f, 0.6f));
            float groundY = r.yMax - 40;
            Fill(new Rect(r.x, groundY, r.width, 40), new Color(0.26f, 0.3f, 0.18f));
            Text(new Rect(r.x + 10, r.y + 6, r.width - 20, 22), $"The base of the trunk, side on. Your line is to the right ({LogTuning.DirNames[g.Aim]}).", 14, new Color(0.15f, 0.17f, 0.18f));

            float left = r.center.x - TrunkW / 2, right = left + TrunkW;
            float top = r.y + 40;
            float floorY = groundY - 70; // the notch floor
            var bark = BarkColor;
            Fill(new Rect(left, top, TrunkW, groundY - top), bark);
            var rng = new System.Random(_gameSeed * 3 + t.Id);
            for (int i = 0; i < 70; i++)
                Fill(new Rect(left + 4 + (float)rng.NextDouble() * (TrunkW - 12), top + (float)rng.NextDouble() * (groundY - top - 8), 2, 6 + rng.Next(10)), Shade(bark, 0.75f + 0.2f * (float)rng.NextDouble()));
            Fill(new Rect(left - 10, groundY - 12, TrunkW + 20, 12), Shade(bark, 0.9f)); // root flare

            float step = TrunkW / LogTuning.CutSteps;
            // The notch: an open wedge into the side your line is on, floor flat, top face sloping up.
            float depth = g.NotchDepth * step;
            if (depth > 0)
            {
                float rise = depth * 0.75f;
                for (float x = 0; x < depth; x += 2)
                {
                    float hgt = rise * (x / depth);
                    float cx = right - depth + x;
                    Fill(new Rect(cx, floorY - hgt, 2, hgt), new Color(0.45f, 0.55f, 0.6f));
                    Fill(new Rect(cx, floorY - hgt, 2, 2), Shade(Wood, 0.9f));
                }
                Fill(new Rect(right - depth, floorY, depth, 3), Shade(Wood, 0.9f));
                // Chips at the foot.
                for (int i = 0; i < g.NotchDepth * 3; i++)
                    Fill(new Rect(right + 10 + (float)rng.NextDouble() * 70, groundY - 3 - (float)rng.NextDouble() * 4, 5, 3), Wood);
            }

            // The back cut: a saw kerf from the far side, at the chosen height.
            float offset = -(int)g.BackHeight * 14f;
            float cutY = floorY + offset;
            float back = g.BackDepth * step;
            if (g.Stage == LogStage.BackCut)
            {
                if (back > 0) Fill(new Rect(left, cutY - (g.Pinched ? 1 : 2), back, g.Pinched ? 2 : 4), new Color(0.08f, 0.06f, 0.04f));
                // The saw lies where the kerf starts (or will start).
                var blade = new Rect(left - 160 + back, cutY - 6, 160, 10);
                Fill(blade, g.Pinched ? new Color(0.55f, 0.5f, 0.48f) : new Color(0.7f, 0.72f, 0.75f));
                for (float x = blade.x + 4; x < blade.xMax - 4; x += 8) Fill(new Rect(x, blade.yMax - 2, 4, 3), new Color(0.5f, 0.5f, 0.52f));
                Fill(new Rect(blade.x - 18, blade.y - 6, 18, 22), new Color(0.45f, 0.3f, 0.18f));
                for (int i = 0; i < g.Wedges; i++)
                {
                    float wx = left + 4 + i * 10;
                    Fill(new Rect(wx, cutY - 8, 12, 6), new Color(0.82f, 0.68f, 0.25f));
                    Fill(new Rect(wx, cutY - 2, 8, 4), new Color(0.82f, 0.68f, 0.25f));
                }
                // Sawdust under the kerf.
                for (int i = 0; i < g.BackDepth * 4; i++)
                    Fill(new Rect(left - 40 + (float)rng.NextDouble() * 60, groundY - 2 - (float)rng.NextDouble() * 3, 2, 2), new Color(0.9f, 0.82f, 0.6f));
            }

            // Your kit.
            var panel = new Rect(700, 130, 520, 400);
            Fill(panel, new Color(0.11f, 0.125f, 0.1f));
            Outline(panel, new Color(0.28f, 0.31f, 0.24f), 1);
            float y = panel.y + 16;
            string wind = g.WindStrength == 0 ? "The air is still." : g.WindStrength == 1 ? "A light breeze in the crown." : "The wind works the crown.";
            Text(new Rect(panel.x + 16, y, panel.width - 32, 24), wind, 15, new Color(0.8f, 0.82f, 0.74f));
            y += 34;
            if (g.Stage == LogStage.Notch)
            {
                Text(new Rect(panel.x + 16, y, panel.width - 32, 80), "Chop the notch on the side it should fall. Judge the depth against the trunk's width.", 15, new Color(0.88f, 0.88f, 0.82f));
                y += 70;
                if (Button(new Rect(panel.x + 16, y, 200, 40), "SPACE  Chop")) g.Chop();
                if (Button(new Rect(panel.x + 230, y, 270, 40), "<b>ENTER  Notch done</b>", true, g.NotchDepth > 0)) g.FinishNotch();
            }
            else
            {
                string height = g.BackHeight switch
                {
                    BackCutHeight.Below => "below the notch floor",
                    BackCutHeight.Level => "level with the notch floor",
                    BackCutHeight.Step => "a hand above the notch floor",
                    _ => "well above the notch floor",
                };
                Text(new Rect(panel.x + 16, y, panel.width - 32, 50),
                    (g.BackDepth == 0 ? "Set the saw: " : "The back cut is ") + height + ".", 15, new Color(0.88f, 0.88f, 0.82f));
                y += 50;
                if (g.BackDepth == 0)
                {
                    if (Button(new Rect(panel.x + 16, y, 150, 36), "UP  Higher")) g.SetBackHeight(Higher(g.BackHeight, 1));
                    if (Button(new Rect(panel.x + 176, y, 150, 36), "DOWN  Lower")) g.SetBackHeight(Higher(g.BackHeight, -1));
                    y += 46;
                }
                Text(new Rect(panel.x + 16, y, panel.width - 32, 24), $"Wedges in the kerf: <b>{g.Wedges}</b> of {LogTuning.MaxWedges}", 15, Color.white);
                y += 34;
                if (Button(new Rect(panel.x + 16, y, 200, 40), "SPACE  Saw")) g.Saw();
                if (Button(new Rect(panel.x + 230, y, 200, 40), "Q  Drive a wedge", false, g.Wedges < LogTuning.MaxWedges)) g.DriveWedge();
            }
            if (Button(new Rect(panel.x + 16, panel.yMax - 54, 200, 38), "E  Walk away")) g.WalkAway();
        }

        // ------------------------------------------------------------------ the fall

        private void DrawFall(LogMinigame g)
        {
            float t = Mathf.Clamp01((Time.time - _fallAt) / 1.3f);
            DrawMap(g, false, g.FellDir, t);
        }

        // ------------------------------------------------------------------ bucking

        private float UnitPx => LogSpan / Mathf.Max(1, _game.Stem.Count);

        private static float LogHeight(int i, int n) => Mathf.Lerp(78, 44, i / (float)Mathf.Max(1, n - 1));

        private void DrawBuck(LogMinigame g)
        {
            int n = g.Stem.Count;
            float u = UnitPx;
            float logBottom = LogY + 80;

            // Ground: touching the log where it rests, falling away where it bridges a dip or hangs out.
            var groundCol = new Color(0.3f, 0.26f, 0.18f);
            float gx0 = LogX - 40, gx1 = LogX + LogSpan + 40;
            for (float x = gx0; x < gx1; x += 4)
            {
                float sum = 0; int cnt = 0;
                for (float dx = -16; dx <= 16; dx += 4)
                {
                    int i = Mathf.Clamp(Mathf.FloorToInt((x + dx - LogX) / u), 0, n - 1);
                    sum += g.Stem[i].Resting ? 0 : 44;
                    cnt++;
                }
                float gy = logBottom + sum / cnt;
                Fill(new Rect(x, gy, 4, 560 - gy), groundCol);
                Fill(new Rect(x, gy, 4, 3), new Color(0.32f, 0.38f, 0.2f));
            }
            for (int i = 0; i < n; i++)
            {
                if (!g.Stem[i].OnRock) continue;
                var c = new Vector2(LogX + (i + 0.5f) * u, logBottom + 14);
                Disc(c, 22, new Color(0.48f, 0.48f, 0.46f));
            }
            // The stump the butt came off.
            Fill(new Rect(LogX - 46, logBottom - 20, 30, 20 + 4), BarkColor);
            Fill(new Rect(LogX - 46, logBottom - 22, 30, 4), Wood);

            DrawStemRun(g, 0, n, LogX, logBottom, u, true);

            // Metre scribes along the top.
            for (int k = 1; k < n; k++)
            {
                float x = LogX + k * u;
                Fill(new Rect(x - 1, logBottom - LogHeight(k, n) - 6, 2, 6), new Color(0.95f, 0.95f, 0.85f, 0.5f));
            }

            // The saw mark.
            if (_mark >= 1 && _mark < n)
            {
                float x = LogX + _mark * u;
                float h = LogHeight(_mark, n);
                Line(new Vector2(x, logBottom - h - 40), new Vector2(x, logBottom - h - 10), Chalk, 3f);
                Line(new Vector2(x, logBottom + 10), new Vector2(x, logBottom + 30), Chalk, 3f);
            }

            // Kit.
            var panel = new Rect(60, 460, 1160, 130);
            Fill(panel, new Color(0.11f, 0.125f, 0.1f));
            Outline(panel, new Color(0.28f, 0.31f, 0.24f), 1);
            Text(new Rect(panel.x + 16, panel.y + 12, 700, 24), $"Knocks left: <b>{g.TapsLeft}</b>     Cuts made: <b>{g.Cuts.Count}</b>", 15, Color.white);
            Text(new Rect(panel.x + 16, panel.y + 38, 1100, 24), "A buyer takes sound, straight timber four metres or longer.", 14, new Color(0.75f, 0.77f, 0.7f));
            float bx = panel.x + 16, by = panel.y + 70;
            if (Button(new Rect(bx, by, 220, 40), "1  Saw from the top")) g.Buck(_mark, BuckSide.Top);
            if (Button(new Rect(bx + 232, by, 220, 40), "2  Saw from beneath")) g.Buck(_mark, BuckSide.Under);
            if (Button(new Rect(bx + 464, by, 160, 40), "K  Knock", false, g.TapsLeft > 0)) g.TapStem(_mark);
            if (Button(new Rect(panel.xMax - 276, by, 260, 40), "<b>ENTER  Done bucking</b>", true)) { g.FinishBucking(); _selPiece = 0; }
        }

        /// <summary>
        /// Draws metres [start, end) of the stem from x0, bottom on baseY. Cuts show as gaps with their end grain;
        /// rot shows only on an end that has been cut (or the butt).
        /// </summary>
        private void DrawStemRun(LogMinigame g, int start, int end, float x0, float baseY, float u, bool showCuts)
        {
            int n = g.Stem.Count;
            var bark = BarkColor;
            for (int i = start; i < end; i++)
            {
                var s = g.Stem[i];
                float h = LogHeight(i, n), h2 = LogHeight(i + 1, n);
                float x = x0 + (i - start) * u;
                bool cutLeft = showCuts && g.Cuts.Contains(i);
                bool cutRight = showCuts && g.Cuts.Contains(i + 1);
                float gl = cutLeft ? 4 : 0, gr = cutRight ? 4 : 0;
                var rng = new System.Random(_gameSeed * 37 + i * 101);

                // Body (a crook humps the metre it's in).
                for (float px = gl; px < u - gr; px += 2)
                {
                    float f = px / u;
                    float hh = Mathf.Lerp(h, h2, f);
                    float hump = s.Crook ? Mathf.Sin(f * Mathf.PI) * 10 : 0;
                    Fill(new Rect(x + px, baseY - hh - hump, 2, hh), bark);
                    Fill(new Rect(x + px, baseY - hh - hump, 2, 3), Shade(bark, 1.15f));
                    Fill(new Rect(x + px, baseY - 4 - hump, 2, 4), Shade(bark, 0.7f));
                }
                // Bark texture.
                for (int k = 0; k < 10; k++)
                    Fill(new Rect(x + gl + (float)rng.NextDouble() * (u - gl - gr - 8), baseY - h + 6 + (float)rng.NextDouble() * (h - 14), 6 + rng.Next(8), 2), Shade(bark, 0.78f));
                // Knots.
                for (int k = 0; k < s.Knots; k++)
                {
                    var c = new Vector2(x + gl + 8 + (float)rng.NextDouble() * (u - gl - gr - 16), baseY - h * (0.3f + 0.4f * (float)rng.NextDouble()));
                    Ellipse(new Rect(c.x - 6, c.y - 4, 12, 9), new Color(0.22f, 0.15f, 0.08f));
                    Ellipse(new Rect(c.x - 3, c.y - 2, 6, 4), new Color(0.4f, 0.28f, 0.14f));
                }
                // Cracks.
                if (s.Cracked)
                {
                    float cx = x + u * (0.3f + 0.4f * (float)rng.NextDouble());
                    var p = new Vector2(cx, baseY - h + 2);
                    for (int k = 0; k < 6; k++)
                    {
                        var q = p + new Vector2(k % 2 == 0 ? 5 : -5, h / 6f);
                        Line(p, q, new Color(0.08f, 0.05f, 0.03f), 2.5f);
                        p = q;
                    }
                }
                if (s.FibrePull && i == start)
                    for (int k = 0; k < 5; k++) Line(new Vector2(x + 2, baseY - h * (0.2f + 0.15f * k)), new Vector2(x - 12, baseY - h * (0.25f + 0.15f * k)), Wood, 3f);
                // Ends: the butt and each cut show their end grain, rot and all.
                if (i == 0 || cutLeft || (i == start && start > 0)) DrawEndGrain(new Vector2(x + gl, baseY - h / 2), h, s);
                if (cutRight || (i == end - 1 && end < n)) DrawEndGrain(new Vector2(x + u - gr, baseY - h2 / 2), h2, s);
                if (s.SplitEnd)
                {
                    float ex = (cutLeft || i == start) ? x + gl : x + u - gr;
                    for (int k = -1; k <= 1; k++) Line(new Vector2(ex, baseY - h / 2), new Vector2(ex + (ex == x + gl ? 18 : -18), baseY - h / 2 + k * h * 0.3f), new Color(0.08f, 0.05f, 0.03f), 2f);
                }
                if (s.Tapped != TrunkSound.None)
                    Text(new Rect(x, baseY - h - 30, u, 20), s.Tapped.ToString().ToLowerInvariant(), 13, Chalk, TextAnchor.MiddleCenter);
            }
        }

        private static void DrawEndGrain(Vector2 c, float h, StemUnit s)
        {
            var face = new Rect(c.x - 7, c.y - h / 2, 14, h);
            Ellipse(face, Wood);
            for (int ring = 1; ring <= 3; ring++)
                Ellipse(new Rect(c.x - 7 + ring * 1.6f, c.y - h / 2 + ring * h * 0.11f, 14 - ring * 3.2f, h - ring * h * 0.22f), Shade(Wood, ring % 2 == 0 ? 1f : 0.92f));
            if (s.Rot == RotKind.HeartRot) Ellipse(new Rect(c.x - 4, c.y - h * 0.25f, 8, h * 0.5f), new Color(0.35f, 0.24f, 0.12f));
            else if (s.Rot == RotKind.Hollow) Ellipse(new Rect(c.x - 4, c.y - h * 0.24f, 8, h * 0.48f), new Color(0.04f, 0.03f, 0.02f));
        }

        // ------------------------------------------------------------------ the yard

        private readonly List<Rect> _pieceRects = new List<Rect>();

        private int PieceAt(Vector2 m)
        {
            for (int i = 0; i < _pieceRects.Count; i++) if (_pieceRects[i].Contains(m)) return i;
            return -1;
        }

        private void DrawYard(LogMinigame g)
        {
            const float unit = 52;
            float x = 60, y = 150;
            _pieceRects.Clear();
            for (int i = 0; i < g.Pieces.Count; i++)
            {
                var p = g.Pieces[i];
                float w = p.IsCrown ? 150 : p.Length * unit;
                if (x + w > W - 60) { x = 60; y += 170; }
                var r = new Rect(x, y - 90, w, 150);
                _pieceRects.Add(r);

                if (p.IsCrown)
                {
                    var rng = new System.Random(_gameSeed + 777);
                    for (int k = 0; k < 18; k++)
                    {
                        var a = new Vector2(x + 10 + (float)rng.NextDouble() * 120, y - 4 - (float)rng.NextDouble() * 50);
                        Line(a, a + new Vector2(20 + (float)rng.NextDouble() * 20, -10 + (float)rng.NextDouble() * 20), BarkColor, 3f);
                    }
                    Disc(new Vector2(x + 75, y - 40), 26, Shade(CrownColor, p.Use == PieceUse.Firewood ? 0.5f : 0.9f), 3f);
                }
                else DrawStemRun(g, p.Start, p.End, x, y, unit, false);

                if (p.Use == PieceUse.PeeledTimber && !p.IsCrown)
                    Fill(new Rect(x + 6, y - 30, w - 12, 18), new Color(Wood.r, Wood.g, Wood.b, 0.8f)); // peeled: bare wood shows
                if (p.Use == PieceUse.Firewood)
                    for (int k = 0; k < g.FirewoodBundles(p); k++)
                        Fill(new Rect(x + 6 + k * 18, y + 6, 14, 10), Wood);

                string label = p.IsCrown ? "crown and limbs" : $"{p.Length} m";
                string use = p.Use switch
                {
                    PieceUse.Timber => "<color=#d8e0a0>timber</color>",
                    PieceUse.PeeledTimber => "<color=#f0d890>timber, peeled</color>",
                    PieceUse.Firewood => "<color=#e0b080>firewood</color>",
                    _ => "<color=#8a8f80>leave it</color>",
                };
                Text(new Rect(x, y + 20, Mathf.Max(w, 120), 20), label, 14, new Color(0.85f, 0.86f, 0.8f));
                Text(new Rect(x, y + 38, Mathf.Max(w, 160), 22), use, 15, Color.white);
                if (i == _selPiece) Outline(new Rect(r.x - 6, r.y - 4, r.width + 12, r.height + 10), Chalk, 2);
                x += w + 40;
            }

            var panel = new Rect(60, 520, 1160, 76);
            Fill(panel, new Color(0.11f, 0.125f, 0.1f));
            Outline(panel, new Color(0.28f, 0.31f, 0.24f), 1);
            Text(new Rect(panel.x + 16, panel.y + 10, 860, 56),
                $"Stacking a length costs a little strength, peeling it (for {g.Stand.BarkName.ToLowerInvariant()}) more, splitting firewood more per bundle. " +
                "Timber a buyer won't take still costs you to stack.", 14, new Color(0.78f, 0.8f, 0.72f));
            if (Button(new Rect(panel.xMax - 276, panel.y + 18, 260, 40), "<b>ENTER  Done</b>", true)) g.FinishYard();
        }

        // ------------------------------------------------------------------ results screen

        private void DrawResults()
        {
            var r = _game.Result;
            var g = _game;
            var gc = r.Timber > 0 ? GradeColor(r.Quality) : new Color(0.7f, 0.7f, 0.62f);
            string title = r.Timber > 0 ? $"{r.ItemName} × {r.Timber}" : r.ItemName;
            Text(new Rect(40, 18, 1200, 60), $"<b>{title}</b>", 36, gc);
            var extra = new List<string>();
            if (r.Bark > 0) extra.Add($"{r.BarkName} × {r.Bark} (quality {r.BarkQuality})");
            if (r.Firewood > 0) extra.Add($"Firewood × {r.Firewood}");
            string sub = (r.Timber > 0 ? $"Quality <b>{r.Quality}</b>   " : "") + string.Join("   ·   ", extra);
            Text(new Rect(40, 64, 1200, 30), sub.Length > 0 ? sub : (r.Felled ? "Nothing came out of it worth carrying." : "You left the tree standing."), 18, Color.white);

            string dim(string s) => $"<size=13><color=#a3a894>{s}</color></size>";
            var body = new System.Text.StringBuilder();
            string rot = r.Rot == RotKind.None ? "sound all through" : r.Rot == RotKind.Hollow ? $"hollow for {r.RotLength} m up from the butt" : $"rotten at the heart for {r.RotLength} m up from the butt";
            body.Append($"<b>The tree</b>  {r.Height:0} m tall, {r.Diameter} cm across, {rot}" + (r.Crooked ? ", with a crook" : "") + (r.LowBranches ? ", knotty low down" : "") + $". Wood grade {r.TreeGrade}.\n");
            if (r.Felled)
            {
                string pull = r.PullStrength < 0.5 ? "barely pulled any way" : $"pulled {(r.PullStrength >= 2 ? "hard " : "")}to the {LogTuning.DirNames[r.PullDir]} (lean, crown and wind together)";
                body.Append($"<b>The fall</b>  You aimed {LogTuning.DirNames[r.Aim]}; it {pull}; it fell {LogTuning.DirNames[r.FellDir]}" + (r.OnLine ? " <color=#b8e0a0>on your line</color>.\n" : " <color=#ffb090>off your line</color>.\n"));
                string notch = r.NotchDepth < LogTuning.NotchMin ? "shallow" : r.NotchDepth > LogTuning.NotchMax ? "too deep" : "about right";
                string hinge = r.CutThrough ? "sawn through" : r.Hinge <= 1 ? "too thin" : r.Hinge >= LogTuning.ThickHinge ? "thick" : "about right";
                string height = r.BackHeight switch { BackCutHeight.Below => "below the notch", BackCutHeight.Level => "level with the notch", BackCutHeight.Step => "a step above the notch", _ => "well above the notch" };
                body.Append(dim($"notch {r.NotchDepth}/20 of the trunk ({notch}) · hinge {r.Hinge}/20 ({hinge}) · back cut {height} · wedges {r.Wedges}") + "\n");
                foreach (var n in r.FallNotes) body.Append("  " + n + "\n");
                if (r.CloseCalls > 0) body.Append($"<color=#ffb090>Close calls: {r.CloseCalls}</color>\n");
                body.Append($"<b>Bucking</b>  {r.Cuts} cuts" + (r.BuckPinches > 0 ? $", {r.BuckPinches} pinched" : ", none pinched") + (r.DirtHits > 0 ? $", the saw hit dirt {r.DirtHits}×" : "") + "\n");
                foreach (var n in r.BuckNotes) body.Append("  " + dim(n) + "\n");
                body.Append($"<b>The yard</b>  timber {r.Timber} sound" + (r.TimberCulled > 0 ? $" + <color=#ffb090>{r.TimberCulled} a buyer will refuse</color>" : "") +
                            $" · of the {r.BestTimber} the tree could give · {r.MetresLeft} m left lying\n");
                foreach (var n in r.CullNotes) body.Append("  " + dim(n) + "\n");
                if (r.Timber > 0)
                {
                    string capNote = r.RawQuality > r.Ceiling ? $" → capped at <b>{r.Ceiling}</b> by {r.CeilingReason}" : "";
                    body.Append($"<b>Quality</b>  lengths average {r.AvgLengthQuality:0} × (0.6 + 0.4 × purity {r.Purity:0.00}) = {r.RawQuality}{capNote}\n");
                    body.Append(dim("a length's quality = wood grade, less for knots, a torn end or fibre pulled from the butt; purity = share of what you stacked as timber that a buyer takes") + "\n");
                }
            }
            body.Append($"<b>Skill</b>  {_skillBefore} → {_skill}" + (r.SkillGain == 0 ? "  " + dim("(no gain: nothing came of it, or this stand is too easy for you)") : ""));
            Text(new Rect(40, 102, 740, 530), body.ToString(), 15, new Color(0.9f, 0.9f, 0.85f));

            DrawTruthMap(g, new Rect(800, 110, 450, 450 * (float)(LogTuning.ClearingH / LogTuning.ClearingW)));

            if (Button(new Rect(40, 640, 260, 50), "<b>Fell another</b> (Enter)", true)) StartLogging();
            if (Button(new Rect(320, 640, 260, 50), "Change setup (Esc)")) _mode = Mode.Setup;
        }

        /// <summary>The stand as it really was: which trees were rotten, which way each pulled, and where yours went.</summary>
        private void DrawTruthMap(LogMinigame g, Rect map)
        {
            float s = map.width / (float)LogTuning.ClearingW;
            Fill(map, new Color(0.24f, 0.28f, 0.17f));
            Outline(map, new Color(0.1f, 0.12f, 0.08f), 2);
            Vector2 P(double x, double y) => new Vector2(map.x + (float)x * s, map.y + (float)y * s);
            foreach (var rock in g.Rocks) Disc(P(rock.X, rock.Y), (float)rock.R * s, new Color(0.5f, 0.5f, 0.48f));
            foreach (var t in g.Trees)
            {
                var p = P(t.X, t.Y);
                var col = t.Rot == RotKind.None ? new Color(0.3f, 0.5f, 0.25f) : t.Rot == RotKind.HeartRot ? new Color(0.55f, 0.4f, 0.2f) : new Color(0.35f, 0.2f, 0.15f);
                if (!t.Felled) Disc(p, (float)t.CrownRadius * s, col, 2f);
                var pv = g.Pull(t);
                var pull = new Vector2((float)pv.X, (float)pv.Y);
                if (pull.magnitude > 0.3f) Line(p, p + pull * (3f * s), new Color(1f, 0.9f, 0.5f), 2f);
                string label = t.Rot == RotKind.None ? "sound" : t.Rot == RotKind.HeartRot ? "heart rot" : "hollow";
                Text(new Rect(p.x - 40, p.y - 8, 80, 16), $"<size=11>{label}</size>", 11, Color.white, TextAnchor.MiddleCenter);
            }
            var tree = g.Tree;
            var tp = P(tree.X, tree.Y);
            if (g.FellDir >= 0)
            {
                Line(tp, tp + DirVec(g.Aim) * (float)tree.Height * s, new Color(1f, 1f, 1f, 0.5f), 2f, 5f);
                Line(tp, tp + DirVec(g.FellDir) * (float)tree.Height * s, BarkColor, 5f);
            }
            if (g.Steep)
            {
                var d = DirVec(g.DownhillDir);
                var c = map.center;
                Line(c - d * 30, c + d * 30, new Color(0.6f, 0.75f, 0.9f), 2f);
            }
            Text(new Rect(map.x, map.yMax + 8, map.width, 90),
                "What the stand really was. Yellow strokes show which way each tree pulled (lean, crown and wind). " +
                "Your line is the faint one; the solid one is where it fell." + (g.Steep ? $" Blue: the slope runs down to the {LogTuning.DirNames[g.DownhillDir]}." : ""),
                13, new Color(0.8f, 0.82f, 0.75f));
        }
    }

    /// <summary>
    /// Safety net: if the Logging scene opens without its Logger object (for example a broken script link),
    /// add one so Play still works.
    /// </summary>
    internal static class LogBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureLogger()
        {
            if (SceneManager.GetActiveScene().name != "Logging") return;
            if (Object.FindAnyObjectByType<LogView>() != null) return;
            new GameObject("Logger").AddComponent<LogView>();
        }
    }
}
