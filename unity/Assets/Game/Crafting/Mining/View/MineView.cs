using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mmo.Crafting.Mining.View
{
    /// <summary>
    /// Placeholder front end for the mining minigame, drawn with Unity's immediate-mode GUI (no sprites or packages).
    /// The rules live in MineMinigame (Core); this class only draws and forwards input.
    ///
    /// A knowledge game with no timing or aiming: the player reads surface signs, taps blocks and the roof, plans which
    /// blocks to take out in which order with which tool, props the roof when it starts to work, and sorts the cart.
    /// Nothing on screen says which block is ore or what to do; the results screen explains what happened.
    /// </summary>
    public sealed class MineView : MonoBehaviour
    {
        private const float W = 1280f, H = 720f;
        private const string SkillPref = "mmo.mine.skill";
        private const string IntroPref = "mmo.mine.introSeen.v2";
        private const string WorldPref = "mmo.mine.world";
        private const string JournalPrefPrefix = "mmo.mine.journal.";

        // Face geometry (GUI units).
        private const float FaceX = 110, FaceY = 100, CellW = 70, CellH = 88;
        private static float FaceW => CellW * MineTuning.Columns;
        private static float FaceH => CellH * MineTuning.Rows;
        private static Rect FaceRect => new Rect(FaceX, FaceY, FaceW, FaceH);
        private static Rect RoofRect => new Rect(FaceX - 20, FaceY - 34, FaceW + 40, 30);

        private enum Mode { Intro, Setup, Play, Results }

        private Mode _mode = Mode.Setup;
        private int _introPage;
        private int _nodeIndex;
        private readonly MineSetup _setup = new MineSetup();
        private int _skill = 1;
        private int _world;
        private MinersJournal _journal;
        private MineMinigame _game;
        private MineAudio _audio;
        private int _seed = 1;
        private int _gameSeed;
        private int _skillBefore;

        private Tool _tool = Tool.Tap;
        private int _selRow = 0, _selCol = 0;   // row -1 = the roof
        private int _selPiece;
        private string _caption = "";
        private float _captionAt = -10f;
        private float _collapseAt = -10f;
        private Vector2 _lastMouse = new Vector2(-1, -1);
        private readonly List<string> _learnedNew = new List<string>();

        private readonly HashSet<KeyCode> _held = new HashSet<KeyCode>();
        private GUIStyle _style;

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            EnsureCameraAndListener();
            _audio = gameObject.AddComponent<MineAudio>();
            _skill = Mathf.Clamp(PlayerPrefs.GetInt(SkillPref, 1), 1, 100);
            _world = PlayerPrefs.GetInt(WorldPref, 0);
            if (_world == 0) NewWorld();
            else LoadJournal();
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
            cam.backgroundColor = new Color(0.07f, 0.065f, 0.06f);
            if (FindAnyObjectByType<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) _held.Clear();
        }

        /// <summary>A fresh world: the signs mean different things and the journal starts empty.</summary>
        private void NewWorld()
        {
            _world = Random.Range(1, int.MaxValue);
            PlayerPrefs.SetInt(WorldPref, _world);
            _journal = new MinersJournal();
        }

        private void LoadJournal() => _journal = MinersJournal.Parse(PlayerPrefs.GetString(JournalPrefPrefix + _world, ""));

        private void SaveJournal() => PlayerPrefs.SetString(JournalPrefPrefix + _world, _journal.Serialize());

        private void StartMining()
        {
            _setup.Node = OreNodeType.All[_nodeIndex];
            _setup.SkillLevel = _skill;
            _setup.WorldSeed = _world;
            _gameSeed = _seed++;
            _game = new MineMinigame(_setup, _gameSeed);
            _tool = Tool.Tap;
            _selRow = 0;
            _selCol = 0;
            _selPiece = 0;
            _caption = "";
            _collapseAt = -10f;
            _skillBefore = _skill;
            _mode = Mode.Play;
        }

        private void Update()
        {
            if (_mode != Mode.Play || _game == null) return;
            DrainEvents();
            if (_game.Stage == MineStage.Done) Finish();
        }

        private void Finish()
        {
            var r = _game.Result;
            _skill = Mathf.Min(100, _skill + r.SkillGain);
            PlayerPrefs.SetInt(SkillPref, _skill);
            _journal.RecordFace(_game.Node.Ore, _game.Blocks);
            SaveJournal();
            _mode = Mode.Results;
        }

        private void DrainEvents()
        {
            foreach (var e in _game.Events)
            {
                _audio.Play(e);
                switch (e.Kind)
                {
                    case MineEventKind.Tapped:
                    case MineEventKind.StageChanged:
                    case MineEventKind.Done:
                        break;
                    case MineEventKind.RoofTapped:
                        Say(_game.LastRoofTap switch
                        {
                            RoofSound.Solid => "The roof rings solid.",
                            RoofSound.Drummy => "The roof sounds a little drummy.",
                            _ => "The roof sounds hollow and drummy.",
                        });
                        break;
                    case MineEventKind.Collapse:
                        _collapseAt = Time.time;
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

            var m = e.mousePosition;
            bool moved = m != _lastMouse;
            _lastMouse = m;

            if (_game.Stage == MineStage.Dig)
            {
                if (moved) HoverFace(m);
                if (e.type == EventType.MouseDown && e.button == 0 && (FaceRect.Contains(m) || RoofRect.Contains(m)))
                {
                    HoverFace(m);
                    UseTool();
                    e.Use();
                }
            }
            else if (_game.Stage == MineStage.Sort)
            {
                int hit = PieceAt(m);
                if (moved && hit >= 0) _selPiece = hit;
                if (e.type == EventType.MouseDown && e.button == 0 && hit >= 0)
                {
                    _selPiece = hit;
                    _game.ToggleKeep(hit);
                    e.Use();
                }
            }
        }

        private void HoverFace(Vector2 m)
        {
            if (RoofRect.Contains(m)) { _selRow = -1; return; }
            if (!FaceRect.Contains(m)) return;
            _selCol = Mathf.Clamp((int)((m.x - FaceX) / CellW), 0, MineTuning.Columns - 1);
            _selRow = Mathf.Clamp((int)((m.y - FaceY) / CellH), 0, MineTuning.Rows - 1);
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
                    if (key == KeyCode.Return || key == KeyCode.KeypadEnter) { StartMining(); return true; }
                    return false;
                case Mode.Results:
                    if (key == KeyCode.Return || key == KeyCode.KeypadEnter || key == KeyCode.Space) { StartMining(); return true; }
                    if (key == KeyCode.Escape) { _mode = Mode.Setup; return true; }
                    return false;
            }

            if (key == KeyCode.Escape) { _mode = Mode.Setup; return true; }

            if (_game.Stage == MineStage.Dig)
            {
                if (key == KeyCode.Alpha1 || key == KeyCode.Keypad1) { _tool = Tool.Tap; return true; }
                if (key == KeyCode.Alpha2 || key == KeyCode.Keypad2) { _tool = Tool.Pick; return true; }
                if (key == KeyCode.Alpha3 || key == KeyCode.Keypad3) { _tool = Tool.Wedge; return true; }
                if (key == KeyCode.Alpha4 || key == KeyCode.Keypad4) { _tool = Tool.Sledge; return true; }
                if (key == KeyCode.P) { _game.Prop(); DrainEvents(); return true; }
                if (key == KeyCode.E) { _game.FinishDigging(); return true; }
                if (key == KeyCode.Space || key == KeyCode.Return) { UseTool(); return true; }
                if (key == KeyCode.LeftArrow || key == KeyCode.A) { MoveSelection(-1, 0); return true; }
                if (key == KeyCode.RightArrow || key == KeyCode.D) { MoveSelection(1, 0); return true; }
                if (key == KeyCode.UpArrow || key == KeyCode.W) { MoveSelection(0, -1); return true; }
                if (key == KeyCode.DownArrow || key == KeyCode.S) { MoveSelection(0, 1); return true; }
            }
            else if (_game.Stage == MineStage.Sort)
            {
                int n = _game.Cart.Count;
                if (key == KeyCode.LeftArrow || key == KeyCode.A) { if (n > 0) _selPiece = (_selPiece + n - 1) % n; return true; }
                if (key == KeyCode.RightArrow || key == KeyCode.D) { if (n > 0) _selPiece = (_selPiece + 1) % n; return true; }
                if (key == KeyCode.Space) { _game.ToggleKeep(_selPiece); return true; }
                if (key == KeyCode.Return || key == KeyCode.KeypadEnter) { _game.FinishSorting(); return true; }
            }
            return false;
        }

        /// <summary>Arrow keys step from block to block (left/right jump a whole block) and up onto the roof.</summary>
        private void MoveSelection(int dx, int dy)
        {
            if (dy != 0)
            {
                _selRow = Mathf.Clamp(_selRow + dy, -1, MineTuning.Rows - 1);
                return;
            }
            if (_selRow < 0) return;
            var b = _game.BlockAt(_selRow, _selCol);
            int col = dx < 0 ? (b != null ? b.Col - 1 : _selCol - 1) : (b != null ? b.End : _selCol + 1);
            _selCol = Mathf.Clamp(col, 0, MineTuning.Columns - 1);
        }

        private void UseTool()
        {
            if (_game.Stage != MineStage.Dig) return;
            if (_selRow < 0)
            {
                if (_tool == Tool.Tap) _game.TapRoof();
                DrainEvents();
                return;
            }
            var b = _game.BlockAt(_selRow, _selCol);
            if (b == null || !b.InPlace) return;
            if (_tool == Tool.Tap) _game.Tap(b.Id);
            else _game.Work(_tool, b.Id);
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
            Fill(new Rect(0, 0, W, H), new Color(0.09f, 0.085f, 0.08f));

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
            Fill(r, !enabled ? new Color(0.18f, 0.17f, 0.17f) : selected ? new Color(0.35f, 0.33f, 0.22f) : new Color(0.22f, 0.21f, 0.2f));
            Outline(r, selected ? new Color(0.9f, 0.8f, 0.45f) : new Color(0.4f, 0.38f, 0.35f), selected ? 2f : 1f);
            Text(r, label, 16, enabled ? Color.white : new Color(0.5f, 0.5f, 0.5f), TextAnchor.MiddleCenter);
            return enabled && Pressed(r);
        }

        private static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);

        private static Color GradeColor(int q)
        {
            if (q >= 81) return new Color(1f, 0.75f, 0.25f);
            if (q >= 61) return new Color(0.45f, 0.75f, 1f);
            if (q >= 41) return new Color(0.85f, 0.85f, 0.85f);
            if (q >= 21) return new Color(0.7f, 0.6f, 0.5f);
            return new Color(0.6f, 0.45f, 0.45f);
        }

        private static Color RockColor(OreKind ore) => ore switch
        {
            OreKind.Iron => new Color(0.40f, 0.39f, 0.40f),
            OreKind.Coal => new Color(0.36f, 0.33f, 0.30f),
            _ => new Color(0.46f, 0.42f, 0.37f),
        };

        private static Color PieceColor(PieceColour c) => c switch
        {
            PieceColour.Rust => new Color(0.55f, 0.27f, 0.15f),
            PieceColour.DarkGrey => new Color(0.26f, 0.26f, 0.28f),
            PieceColour.Black => new Color(0.08f, 0.08f, 0.09f),
            PieceColour.GreenBlue => new Color(0.22f, 0.55f, 0.50f),
            PieceColour.Brass => new Color(0.72f, 0.62f, 0.28f),
            PieceColour.Grey => new Color(0.50f, 0.50f, 0.50f),
            PieceColour.Brown => new Color(0.45f, 0.36f, 0.27f),
            _ => new Color(0.90f, 0.90f, 0.88f),
        };

        // ------------------------------------------------------------------ intro
        // The old miner teaches the craft (how rock behaves, what tools are for), never the answer:
        // nothing about which sign means which ore here. That has to be learned.

        private static readonly (string Title, string Body)[] IntroPages =
        {
            ("Reading the face",
             "So you want to dig. Good. Listen once, then learn with your hands.\n\n" +
             "Ore lies in seams, a bed of it between beds of plain rock. Look at the face before you swing. Ore leaves " +
             "signs on the surface: stains, streaks, lichen, little crystals. Which sign goes with which ore changes from " +
             "land to land, so I can't tell you. Keep a journal and you'll learn it.\n\n" +
             "When you're not sure, tap the rock with the back of your pick. Ore rings. Sound rock thuds. Broken rock " +
             "sounds hollow. Your arm only has so many taps in it before you have to get on with it."),
            ("Cutting",
             "Rock is held by the rock around it. A block with rock on both sides and rock above it is locked tight. " +
             "Try to pry ore out of that and it shatters, and crushed ore is poor ore.\n\n" +
             "So open the face first, on barren rock, beside or above the ore. Broken rock comes away easy. Then the " +
             "ore has a free side, and it comes out whole.\n\n" +
             "Pick for the small blocks. Wedge for the big ones; the pick can't shift them and the wedge shatters small " +
             "ones. The sledge breaks rock out of anywhere, but never take it to ore. Every swing tires you, and a " +
             "wasted one is wasted."),
            ("The roof",
             "Every block you take out leaves the roof less to sit on. The sledge shakes it worse.\n\n" +
             "Listen to it. When it starts to creak and dust trickles down, it's working. Tap it: a roof that rings solid " +
             "is fine, one that sounds drummy is not. Set a prop and it settles. You only carry a couple.\n\n" +
             "Push your luck and it comes down, and you'll leave part of your cart under it."),
            ("Sorting",
             "Not everything in the cart is ore. Some rock is stained the same colour and will fool your eye. Colour " +
             "lies. Lift each piece and feel it. Know what your ore should weigh for its size.\n\n" +
             "Keep the ore, toss the rest on the spoil heap. Rock left in your ore makes it worth less, and ore " +
             "thrown away is ore you dug for nothing.\n\n" +
             "When a node's done, look at what was really in that face. That's how a miner learns. Now go dig."),
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
            var card = new Rect(190, 60, 900, 590);
            Fill(card, new Color(0.14f, 0.13f, 0.12f));
            Outline(card, new Color(0.42f, 0.38f, 0.3f), 2);
            Text(new Rect(card.x + 50, card.y + 30, 800, 40), "The old miner", 16, new Color(0.62f, 0.58f, 0.5f));
            Text(new Rect(card.x + 50, card.y + 56, 800, 60), $"<b>{page.Title}</b>", 34, new Color(0.95f, 0.82f, 0.5f));
            Text(new Rect(card.x + 50, card.y + 120, 800, 400), page.Body, 20, new Color(0.92f, 0.89f, 0.84f));
            Text(new Rect(card.x + 50, card.yMax - 50, 300, 30), $"{_introPage + 1} / {IntroPages.Length}", 15, new Color(0.58f, 0.54f, 0.48f));

            bool last = _introPage == IntroPages.Length - 1;
            if (_introPage > 0 && Button(new Rect(card.xMax - 430, card.yMax - 70, 180, 48), "Back")) _introPage--;
            if (Button(new Rect(card.xMax - 230, card.yMax - 70, 180, 48), last ? "<b>To the rock</b>" : "<b>Next</b>", true)) NextIntroPage();
            Text(new Rect(0, H - 40, W, 30), "SPACE next · LEFT back · ESC skip", 14, new Color(0.5f, 0.47f, 0.45f), TextAnchor.MiddleCenter);
        }

        // ------------------------------------------------------------------ setup screen

        private void DrawSetup()
        {
            Text(new Rect(40, 20, 1200, 50), "Mine  <size=20><color=#a9a294>mining prototype</color></size>", 36, new Color(0.95f, 0.82f, 0.5f), bold: true);

            Text(new Rect(40, 80, 360, 30), "Node", 18, new Color(0.8f, 0.76f, 0.7f));
            for (int i = 0; i < OreNodeType.All.Length; i++)
            {
                var n = OreNodeType.All[i];
                if (Button(new Rect(40, 110 + i * 56, 360, 48), $"{n.Name}  <size=13>(skill {n.RequiredSkill}+)</size>", i == _nodeIndex))
                    _nodeIndex = i;
            }
            var node = OreNodeType.All[_nodeIndex];
            Text(new Rect(40, 290, 360, 80), $"{node.Description}\n<b>Gives:</b> {node.OreName}", 15, new Color(0.85f, 0.82f, 0.78f));

            float x = 460;
            Text(new Rect(x, 80, 700, 30), $"Mining skill: <b>{_skill}</b> ({MineTuning.RankFor(_skill)})", 18, Color.white);
            int newSkill = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x, 115, 500, 20), _skill, 1, 100));
            if (newSkill != _skill) { _skill = newSkill; PlayerPrefs.SetInt(SkillPref, _skill); }
            Text(new Rect(x, 135, 780, 40), "Test slider. Skill sharpens your senses (more signs noticed, more taps, earlier roof warnings) and raises the quality cap.", 13, new Color(0.65f, 0.62f, 0.6f));
            if (_skill < node.RequiredSkill)
                Text(new Rect(x, 172, 780, 24), $"Below this node's rank ({node.RequiredSkill}). You can still practise on it.", 14, new Color(1f, 0.6f, 0.4f));

            Text(new Rect(x, 200, 400, 26), "Pick", 18, new Color(0.8f, 0.76f, 0.7f));
            for (int i = 0; i < Pick.All.Length; i++)
            {
                var p = Pick.All[i];
                if (Button(new Rect(x + i * 205, 228, 195, 36), p.Name, _setup.Pick == p)) _setup.Pick = p;
            }
            Text(new Rect(x, 266, 780, 22), _setup.Pick.Description, 13, new Color(0.65f, 0.62f, 0.6f));

            Text(new Rect(x, 300, 200, 30), "Vein grade", 15, Color.white);
            _setup.VeinGrade = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x + 200, 308, 300, 20), _setup.VeinGrade, 1, 100));
            Text(new Rect(x + 520, 300, 300, 30), $"{_setup.VeinGrade}  <size=13><color=#a6a09a>test slider; hidden in game</color></size>", 15, GradeColor(_setup.VeinGrade));

            // World and journal
            Text(new Rect(x, 350, 780, 26), "World", 18, new Color(0.8f, 0.76f, 0.7f));
            Text(new Rect(x, 378, 520, 60), "Signs mean different things in each world. Your journal keeps what you've learned in this one.", 14, new Color(0.75f, 0.72f, 0.68f));
            if (Button(new Rect(x + 540, 380, 240, 40), "New world (fresh journal)")) NewWorld();
            DrawJournal(new Rect(x, 440, 780, 170), node.Ore);

            if (Button(new Rect(x, 630, 300, 56), "<b>Start mining</b>  <size=13>(Enter)</size>", true)) StartMining();
            if (Button(new Rect(40, 630, 360, 56), "Hear the old miner again")) { _introPage = 0; _mode = Mode.Intro; }
        }

        private void DrawJournal(Rect r, OreKind ore)
        {
            Text(new Rect(r.x, r.y, r.width, 24), $"<b>Journal</b>: signs seen at {ore.ToString().ToLowerInvariant()} faces", 15, new Color(0.9f, 0.85f, 0.7f));
            if (_journal.IsEmpty(ore))
            {
                Text(new Rect(r.x, r.y + 26, r.width, 24), "Nothing written yet.", 14, new Color(0.65f, 0.62f, 0.6f));
                return;
            }
            float y = r.y + 26;
            foreach (var s in SignTable.AllSigns)
            {
                var (o, k) = _journal.Seen(ore, s);
                if (o + k == 0) continue;
                Text(new Rect(r.x, y, r.width, 20), $"{SignTable.Describe(s)}: on ore {o}, on barren rock {k}", 13, new Color(0.82f, 0.8f, 0.76f));
                y += 18;
                if (y > r.yMax - 16) break;
            }
        }

        // ------------------------------------------------------------------ play screen

        private void DrawPlay()
        {
            var g = _game;
            float shake = Time.time - _collapseAt < 0.6f ? (1 - (Time.time - _collapseAt) / 0.6f) * 6f : 0;
            var old = GUI.matrix;
            if (shake > 0) GUI.matrix = old * Matrix4x4.TRS(new Vector3(Mathf.Sin(Time.time * 80) * shake, Mathf.Cos(Time.time * 60) * shake, 0), Quaternion.identity, Vector3.one);

            Text(new Rect(30, 14, 900, 40), $"<b>{g.Node.Name}</b>", 26, new Color(0.95f, 0.82f, 0.5f));
            if (g.Stage == MineStage.Dig) DrawDig(g);
            else if (g.Stage == MineStage.Sort) DrawSort(g);

            GUI.matrix = old;
            DrawControls(g);
        }

        private void DrawControls(MineMinigame g)
        {
            string keys = g.Stage switch
            {
                MineStage.Dig => "<b>1</b> tap  <b>2</b> pick  <b>3</b> wedge  <b>4</b> sledge   <b>MOUSE</b>/<b>ARROWS</b> choose   <b>CLICK</b>/<b>SPACE</b> use   <b>P</b> prop   <b>E</b> stop digging",
                MineStage.Sort => "<b>MOUSE</b>/<b>ARROWS</b> pick up a piece   <b>CLICK</b>/<b>SPACE</b> keep or toss   <b>ENTER</b> done",
                _ => "",
            };
            var bar = new Rect(0, H - 40, W, 40);
            Fill(bar, new Color(0.055f, 0.05f, 0.045f));
            Text(new Rect(30, bar.y, W - 60, bar.height), keys + "      <color=#7d746c>ESC leave</color>", 15, new Color(0.85f, 0.82f, 0.78f), TextAnchor.MiddleLeft);
        }

        private void DrawDig(MineMinigame g)
        {
            var rock = RockColor(g.Node.Ore);

            // Roof and frame.
            Fill(new Rect(FaceX - 20, FaceY - 34, FaceW + 40, FaceH + 54), Shade(rock, 0.45f));
            Fill(RoofRect, Shade(rock, 0.6f));

            foreach (var b in g.Blocks) DrawBlock(g, b, rock);

            // Props drawn over the face (they stand in front of it).
            for (int i = 0; i < g.PropsSet; i++)
            {
                float px = FaceX + FaceW * (i + 1) / (g.PropsSet + 1f);
                Fill(new Rect(px - 6, FaceY - 4, 12, FaceH + 8), new Color(0.48f, 0.34f, 0.2f, 0.9f));
                Fill(new Rect(px - 22, FaceY - 8, 44, 8), new Color(0.42f, 0.3f, 0.18f));
            }

            DrawSelection(g);
            DrawDust(g);
            DrawFloor(g, rock);
            DrawKit(g);
            DrawJournal(new Rect(990, 420, 270, 220), g.Node.Ore);

            if (_caption.Length > 0 && Time.time - _captionAt < 6f)
            {
                float a = Mathf.Clamp01(1.5f - (Time.time - _captionAt) / 4f);
                Text(new Rect(FaceX, FaceY + FaceH + 66, FaceW, 30), _caption, 18, new Color(0.95f, 0.92f, 0.85f, a), TextAnchor.MiddleCenter);
            }
        }

        private Rect BlockRect(Block b) => new Rect(FaceX + b.Col * CellW, FaceY + b.Row * CellH, b.Width * CellW, CellH);

        private void DrawBlock(MineMinigame g, Block b, Color rock)
        {
            var r = BlockRect(b);
            var inner = new Rect(r.x + 2, r.y + 2, r.width - 4, r.height - 4);
            var rng = new System.Random(_gameSeed * 31 + b.Id * 977);

            if (!b.InPlace)
            {
                Fill(inner, new Color(0.06f, 0.05f, 0.05f));
                if (b.State == BlockState.Crushed)
                    for (int i = 0; i < 30; i++)
                        Fill(new Rect(inner.x + (float)rng.NextDouble() * inner.width, inner.yMax - 4 - (float)rng.NextDouble() * 18, 3, 2), new Color(0.5f, 0.48f, 0.45f));
                return;
            }

            // Body with grain.
            float tone = 0.85f + 0.3f * (float)rng.NextDouble();
            Fill(inner, Shade(rock, tone));
            for (int i = 0; i < 26; i++)
            {
                float sx = inner.x + (float)rng.NextDouble() * (inner.width - 4), sy = inner.y + (float)rng.NextDouble() * (inner.height - 3);
                Fill(new Rect(sx, sy, 2 + rng.Next(5), 2), Shade(rock, tone * (0.75f + 0.5f * (float)rng.NextDouble())));
            }
            // Bedding texture: faint horizontal lines.
            for (int i = 1; i < 3; i++)
                Fill(new Rect(inner.x + 4, inner.y + inner.height * i / 3f + (float)rng.NextDouble() * 6, inner.width - 8, 1), Shade(rock, tone * 0.8f));

            // Broken rock shows it: a web of cracks.
            if (b.Kind == BlockKind.Loose)
            {
                for (int i = 0; i < 5; i++)
                {
                    var a = new Vector2(inner.x + (float)rng.NextDouble() * inner.width, inner.y + (float)rng.NextDouble() * inner.height);
                    var c = a + new Vector2((float)rng.NextDouble() * 40 - 20, (float)rng.NextDouble() * 40 - 20);
                    Dots(a, c, new Color(0.08f, 0.07f, 0.06f), 2f, 2f);
                }
            }

            foreach (var s in b.Signs) DrawSign(inner, s, rng);

            // Chalked tap result.
            if (b.Tapped != TapSound.None)
                Text(new Rect(inner.x + 4, inner.y + 2, inner.width - 8, 22), b.Tapped.ToString().ToLowerInvariant(), 14, new Color(0.95f, 0.95f, 0.9f, 0.85f));
        }

        private static void DrawSign(Rect r, Sign s, System.Random rng)
        {
            float X() => r.x + 6 + (float)rng.NextDouble() * (r.width - 12);
            float Y() => r.y + 18 + (float)rng.NextDouble() * (r.height - 26);
            switch (s)
            {
                case Sign.RustStreaks:
                    for (int i = 0; i < 4; i++) { var a = new Vector2(X(), Y()); Dots(a, a + new Vector2(18, 10), new Color(0.6f, 0.28f, 0.12f), 2f, 2f); }
                    break;
                case Sign.GreenStain:
                    for (int i = 0; i < 3; i++) Fill(new Rect(X() - 8, Y() - 5, 18 + rng.Next(10), 10 + rng.Next(6)), new Color(0.2f, 0.55f, 0.5f, 0.55f));
                    break;
                case Sign.BlackBands:
                    for (int i = 0; i < 2; i++) Fill(new Rect(r.x + 6, Y(), r.width - 12, 2), new Color(0.05f, 0.05f, 0.06f));
                    break;
                case Sign.QuartzVeins:
                    for (int i = 0; i < 2; i++)
                    {
                        var a = new Vector2(X(), Y());
                        var b = a + new Vector2(10, -8); var c = b + new Vector2(12, 6);
                        Dots(a, b, new Color(0.92f, 0.92f, 0.9f), 2f, 1.5f); Dots(b, c, new Color(0.92f, 0.92f, 0.9f), 2f, 1.5f);
                    }
                    break;
                case Sign.YellowLichen:
                case Sign.GreyLichen:
                {
                    var col = s == Sign.YellowLichen ? new Color(0.85f, 0.75f, 0.2f) : new Color(0.55f, 0.65f, 0.5f);
                    float cx = X(), cy = Y();
                    for (int i = 0; i < 12; i++) Fill(new Rect(cx + (float)rng.NextDouble() * 16 - 8, cy + (float)rng.NextDouble() * 10 - 5, 3, 3), col);
                    break;
                }
                case Sign.CubeCrystals:
                    for (int i = 0; i < 6; i++) { float cx = X(), cy = Y(); Fill(new Rect(cx, cy, 4, 4), new Color(0.7f, 0.65f, 0.4f)); Fill(new Rect(cx, cy, 2, 2), Color.white); }
                    break;
                default:
                    for (int i = 0; i < 7; i++) { float cx = X(), cy = Y(); Fill(new Rect(cx, cy, 1, 7), new Color(0.85f, 0.88f, 0.9f)); }
                    break;
            }
        }

        private void DrawSelection(MineMinigame g)
        {
            var chalk = new Color(0.95f, 0.95f, 0.9f, 0.8f);
            if (_selRow < 0) { Outline(RoofRect, chalk, 2); return; }
            var b = g.BlockAt(_selRow, _selCol);
            if (b != null) Outline(BlockRect(b), chalk, 2);
        }

        /// <summary>Dust trickling from the roof once the miner would notice it. The only roof reading besides a tap.</summary>
        private void DrawDust(MineMinigame g)
        {
            float d = (float)g.DustLevel;
            if (d <= 0) return;
            int n = Mathf.RoundToInt(6 + d * 40);
            for (int i = 0; i < n; i++)
            {
                float seed = i * 12.9898f;
                float x = FaceX + Mathf.Repeat(seed * 37.1f, FaceW);
                float speed = 60 + Mathf.Repeat(seed * 7.3f, 60);
                float y = FaceY + Mathf.Repeat(Time.time * speed + seed * 50, FaceH);
                Fill(new Rect(x, y, 2, 2), new Color(0.8f, 0.77f, 0.7f, 0.35f + 0.4f * d));
            }
        }

        private void DrawFloor(MineMinigame g, Color rock)
        {
            float y = FaceY + FaceH + 20;
            // Spoil heap: one heap of rubble per barren block out.
            int removed = 0;
            foreach (var b in g.Blocks) if (b.State == BlockState.Removed) removed++;
            var rng = new System.Random(_gameSeed + 5);
            for (int i = 0; i < removed * 10; i++)
                Fill(new Rect(FaceX + 10 + (float)rng.NextDouble() * 220, y + 30 - (float)rng.NextDouble() * Mathf.Min(30, removed * 4), 5, 4), Shade(rock, 0.7f + 0.3f * (float)rng.NextDouble()));
            Text(new Rect(FaceX + 10, y + 32, 220, 20), "spoil heap", 12, new Color(0.55f, 0.52f, 0.48f));

            // The cart.
            var cart = new Rect(FaceX + FaceW - 200, y, 190, 36);
            Fill(cart, new Color(0.25f, 0.18f, 0.12f));
            Outline(cart, new Color(0.38f, 0.28f, 0.2f), 2);
            for (int i = 0; i < g.Cart.Count && i < 30; i++)
                Fill(new Rect(cart.x + 6 + (i % 15) * 12, cart.y - 8 - (i / 15) * 8, 10, 8), Shade(PieceColor(g.Cart[i].Colour), g.Cart[i].Crushed ? 0.8f : 1f));
        }

        /// <summary>The miner's kit: tools, what's left in the arm, props in hand.</summary>
        private void DrawKit(MineMinigame g)
        {
            var panel = new Rect(990, 70, 270, 340);
            Fill(panel, new Color(0.13f, 0.12f, 0.11f));
            Outline(panel, new Color(0.3f, 0.28f, 0.25f), 1);
            float y = panel.y + 12;
            var tools = new[] { (Tool.Tap, "1  Tap"), (Tool.Pick, "2  Pick"), (Tool.Wedge, "3  Wedge"), (Tool.Sledge, "4  Sledge") };
            foreach (var (tool, label) in tools)
            {
                if (Button(new Rect(panel.x + 12, y, panel.width - 24, 34), label, _tool == tool)) _tool = tool;
                y += 40;
            }
            y += 8;
            Text(new Rect(panel.x + 14, y, 240, 22), $"Swings left in your arm: <b>{g.SwingsLeft}</b>", 15, Color.white); y += 24;
            Text(new Rect(panel.x + 14, y, 240, 22), $"Taps left: <b>{g.TapsLeft}</b>", 15, Color.white); y += 28;
            Text(new Rect(panel.x + 14, y, 120, 22), "Props:", 15, Color.white);
            for (int i = 0; i < g.PropsLeft; i++) Fill(new Rect(panel.x + 80 + i * 22, y + 2, 10, 20), new Color(0.48f, 0.34f, 0.2f));
            y += 30;
            if (Button(new Rect(panel.x + 12, y, 120, 32), "P  Prop", false, g.PropsLeft > 0 && g.SwingsLeft > 0)) { g.Prop(); DrainEvents(); }
            if (Button(new Rect(panel.x + 138, y, 120, 32), "E  Stop")) g.FinishDigging();
        }

        // ------------------------------------------------------------------ sorting

        private Rect PieceRect(int i, bool keep, int slot)
        {
            const int perRow = 12;
            float baseY = keep ? 130 : 420;
            return new Rect(90 + (slot % perRow) * 74, baseY + (slot / perRow) * 70, 60, 54);
        }

        private readonly List<Rect> _pieceRects = new List<Rect>();

        private int PieceAt(Vector2 m)
        {
            for (int i = 0; i < _pieceRects.Count; i++) if (_pieceRects[i].Contains(m)) return i;
            return -1;
        }

        private void DrawSort(MineMinigame g)
        {
            if (g.Collapsed) Text(new Rect(30, 48, 1000, 26), "The roof came in. You got clear with what was in the cart.", 16, new Color(1f, 0.6f, 0.45f));
            Text(new Rect(90, 92, 600, 30), "<b>Keep</b> (the cart)", 18, new Color(0.9f, 0.85f, 0.7f));
            Fill(new Rect(80, 122, 900, 270), new Color(0.2f, 0.15f, 0.1f));
            Text(new Rect(90, 392, 600, 26), "<b>Toss</b> (the spoil heap)", 18, new Color(0.8f, 0.76f, 0.7f));
            Fill(new Rect(80, 418, 900, 200), new Color(0.16f, 0.15f, 0.14f));

            _pieceRects.Clear();
            int keepSlot = 0, tossSlot = 0;
            for (int i = 0; i < g.Cart.Count; i++)
            {
                var p = g.Cart[i];
                var r = PieceRect(i, p.Keep, p.Keep ? keepSlot++ : tossSlot++);
                _pieceRects.Add(r);
                DrawPiece(r, p, i == _selPiece);
            }
            if (g.Cart.Count == 0) Text(new Rect(90, 160, 800, 30), "The cart is empty.", 16, new Color(0.7f, 0.66f, 0.6f));

            // What you see and feel of the piece in your hand.
            var info = new Rect(1000, 130, 260, 300);
            Fill(info, new Color(0.13f, 0.12f, 0.11f));
            Outline(info, new Color(0.3f, 0.28f, 0.25f), 1);
            if (_selPiece >= 0 && _selPiece < g.Cart.Count)
            {
                var p = g.Cart[_selPiece];
                Text(new Rect(info.x + 14, info.y + 12, 232, 26), "<b>In your hand</b>", 16, new Color(0.9f, 0.85f, 0.7f));
                Text(new Rect(info.x + 14, info.y + 44, 232, 240),
                    $"{Cap(MineTuning.Describe(p.Colour))}, {MineTuning.Describe(p.Lustre)}" + (p.Crushed ? ", crushed to grit" : "") +
                    $".\n\nYou heft it: {MineTuning.Describe(p.Heft)}.", 16, new Color(0.9f, 0.88f, 0.84f));
            }
            if (Button(new Rect(1000, 560, 260, 52), "<b>Done sorting</b> (Enter)", true)) g.FinishSorting();
        }

        private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        private void DrawPiece(Rect r, Piece p, bool selected)
        {
            var c = PieceColor(p.Colour);
            float w = r.width * (0.7f + 0.3f * (float)p.Shape), h = r.height * (0.6f + 0.25f * (float)(1 - p.Shape));
            var body = new Rect(r.x + (r.width - w) / 2, r.y + (r.height - h) / 2, w, h);
            if (p.Crushed)
            {
                var rng = new System.Random((int)(p.Shape * 100000));
                for (int i = 0; i < 18; i++)
                    Fill(new Rect(body.x + (float)rng.NextDouble() * body.width, body.y + (float)rng.NextDouble() * body.height, 5, 4), Shade(c, 0.8f + 0.3f * (float)rng.NextDouble()));
            }
            else
            {
                Fill(body, c);
                Fill(new Rect(body.x + 4, body.y - 4, body.width - 10, 4), Shade(c, 0.9f));
                if (p.Lustre == Lustre.Metallic) Fill(new Rect(body.x + 6, body.y + 4, 8, 3), new Color(0.95f, 0.95f, 0.95f));
                else if (p.Lustre == Lustre.Glassy) Dots(new Vector2(body.x + 6, body.y + 4), new Vector2(body.x + 20, body.y + body.height - 6), new Color(1f, 1f, 1f, 0.7f), 2f, 2f);
            }
            if (selected) Outline(new Rect(r.x - 3, r.y - 6, r.width + 6, r.height + 10), new Color(0.95f, 0.95f, 0.9f, 0.8f), 2);
        }

        // ------------------------------------------------------------------ results screen

        private void DrawResults()
        {
            var r = _game.Result;
            var g = _game;
            var gc = r.Yield > 0 ? GradeColor(r.Quality) : new Color(0.7f, 0.65f, 0.6f);
            string title = r.Yield > 0 ? $"{r.ItemName} × {r.Yield}" : r.ItemName;
            Text(new Rect(40, 22, 1200, 60), $"<b>{title}</b>", 38, gc);
            Text(new Rect(40, 72, 1200, 30), r.Yield > 0 ? $"Quality <b>{r.Quality}</b>  ·  vein grade {r.VeinGrade}" : "Nothing worth carrying came out of this node.", 19, Color.white);

            string capNote = r.RawQuality > r.Ceiling ? $" → capped at <b>{r.Ceiling}</b> by {r.CeilingReason}" : "";
            string dim(string s) => $"<size=14><color=#a9a294>{s}</color></size>";
            string body =
                $"<b>The face</b>  {r.OreBlocks} ore blocks: {r.OreFreed} out whole, {r.OreShattered} shattered, {r.OreBlocks - r.OreFreed - r.OreShattered} left in the rock\n" +
                dim($"barren rock taken out {r.RockRemoved} · wasted swings {r.WastedSwings} · swings used {r.SwingsUsed} · taps used {r.TapsUsed}") + "\n" +
                $"<b>The roof</b>  props set {r.PropsUsed}" + (r.Collapsed ? $" · <color=#ff9a80>it came down; {r.PiecesBuried} pieces buried</color>" : " · it held") + "\n" +
                $"<b>Sorting</b>  right calls {r.SortScore:P0} · ore kept {r.OreKept + r.CrushedKept} · rock kept {r.WasteKept} · ore tossed {r.OreTossed}\n\n" +
                $"<b>Yield</b>  ore kept {r.OreKept} + crushed {r.CrushedKept} × ½ = <b>{r.Yield}</b> of the {r.NodeOre} the node held\n" +
                $"<b>Quality</b>  vein {r.VeinGrade} × (0.5 + 0.5 × whole {r.BreakScore:0.00}) × (0.6 + 0.4 × purity {r.Purity:0.00}) = {r.RawQuality}{capNote}\n" +
                dim("whole = share of the ore you took out that came out unbroken; purity = share of what you kept that was ore") + "\n\n" +
                $"<b>Skill</b>  {_skillBefore} → {_skill}" + (r.SkillGain == 0 ? "  " + dim("(no gain: nothing came out, or this node is too easy for you)") : "") + "\n\n" +
                "<b>Written in your journal</b>\n" + (r.Learned.Count > 0 ? string.Join("\n", r.Learned) : "No signs on this face.");
            Text(new Rect(40, 112, 760, 520), body, 16, new Color(0.9f, 0.88f, 0.85f));

            DrawFaceMap(g, new Rect(820, 120, 430, 430 * FaceH / FaceW));

            if (Button(new Rect(40, 640, 260, 50), "<b>Mine again</b> (Enter)", true)) StartMining();
            if (Button(new Rect(320, 640, 260, 50), "Change setup (Esc)")) _mode = Mode.Setup;
        }

        /// <summary>The face as it really was: ore, solid and loose rock, and what happened to each block.</summary>
        private void DrawFaceMap(MineMinigame g, Rect map)
        {
            float cw = map.width / MineTuning.Columns, ch = map.height / MineTuning.Rows;
            foreach (var b in g.Blocks)
            {
                var r = new Rect(map.x + b.Col * cw + 1, map.y + b.Row * ch + 1, b.Width * cw - 2, ch - 2);
                Color c = b.Kind switch
                {
                    BlockKind.Ore => new Color(0.75f, 0.6f, 0.25f),
                    BlockKind.Loose => new Color(0.38f, 0.35f, 0.32f),
                    _ => new Color(0.5f, 0.5f, 0.5f),
                };
                Fill(r, b.InPlace ? c : Shade(c, 0.4f));
                string mark = b.State switch
                {
                    BlockState.Clean => "whole",
                    BlockState.Crushed => "crushed",
                    BlockState.Removed => "out",
                    _ => "",
                };
                Text(r, $"<size=11>{(b.Kind == BlockKind.Loose ? "loose" : b.Kind == BlockKind.Ore ? "ORE" : "")}\n{mark}</size>", 11, Color.white, TextAnchor.MiddleCenter);
                if (b.Signs.Count > 0) Fill(new Rect(r.xMax - 7, r.y + 2, 5, 5), b.Signs.Contains(g.TrueSign) ? new Color(1f, 0.9f, 0.4f) : new Color(0.7f, 0.7f, 0.9f));
            }
            Text(new Rect(map.x, map.yMax + 8, map.width, 60),
                $"What the face really was. Ore here showed <b>{SignTable.Describe(g.TrueSign)}</b> (gold dot); other signs (blue dot) meant nothing.",
                13, new Color(0.8f, 0.78f, 0.75f));
        }
    }

    /// <summary>
    /// Safety net: if the Mining scene opens without its Mine object (for example a broken script link),
    /// add one so Play still works.
    /// </summary>
    internal static class MineBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureMine()
        {
            if (SceneManager.GetActiveScene().name != "Mining") return;
            if (Object.FindAnyObjectByType<MineView>() != null) return;
            new GameObject("Mine").AddComponent<MineView>();
        }
    }
}
