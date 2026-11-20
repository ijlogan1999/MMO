using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Mmo.Crafting.Mining.View
{
    /// <summary>
    /// Placeholder front end for the mining minigame, drawn with Unity's immediate-mode GUI plus one generated texture
    /// for the rock face, so there are no sprites, prefabs or packages to set up yet.
    /// The rules live in MineMinigame (Core); this class only draws and forwards input.
    ///
    /// Deliberately unguided: no gauges, markers, zones or timers. The player reads the streaks, flecks and cracks to
    /// find the seam, then watches and listens for each crack to open. The results screen explains the outcome.
    /// Controls: mouse or WASD / arrows move the pick, SPACE or click marks the start / swings, E steps back,
    /// ENTER mine again, ESC back to setup.
    /// </summary>
    public sealed class MineView : MonoBehaviour
    {
        private const float W = 1280f, H = 720f;
        private const string SkillPref = "mmo.mine.skill";
        private const string IntroPref = "mmo.mine.introSeen";
        private const float CursorSpeed = 0.55f; // face widths per second with the keys

        private static readonly Rect FaceRect = new Rect(170, 84, 860, 860 * (float)MineTuning.FaceHeight);

        private enum Mode { Intro, Setup, Play, Results }

        private Mode _mode = Mode.Setup;
        private int _introPage;
        private int _nodeIndex;
        private readonly MineSetup _setup = new MineSetup();
        private int _skill = 1;
        private MineMinigame _game;
        private MineAudio _audio;
        private int _seed = 1;
        private int _gameSeed;
        private int _skillBefore;

        private Texture2D _face;
        private Vector2 _cursor = new Vector2(0.5f, 0.3f);   // face units
        private Vector2 _lastMouse = new Vector2(-1, -1);
        private float _swingAt = -10f;
        private Vector2 _swingSpot;
        private bool _swingClean;

        private readonly HashSet<KeyCode> _held = new HashSet<KeyCode>();
        private GUIStyle _style;

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            EnsureCameraAndListener();
            _audio = gameObject.AddComponent<MineAudio>();
            _skill = Mathf.Clamp(PlayerPrefs.GetInt(SkillPref, 1), 1, 100);
            _seed = Random.Range(1, int.MaxValue);
            if (PlayerPrefs.GetInt(IntroPref, 0) == 0) _mode = Mode.Intro;
        }

        private void OnDestroy()
        {
            if (_face != null) Destroy(_face);
            Cursor.visible = true;
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

        // A key released while the window was unfocused never sends KeyUp; forget held keys.
        private void OnApplicationFocus(bool focused)
        {
            if (!focused) _held.Clear();
        }

        private void StartMining()
        {
            _setup.Node = OreNodeType.All[_nodeIndex];
            _setup.SkillLevel = _skill;
            _gameSeed = _seed++;
            _game = new MineMinigame(_setup, _gameSeed);
            BuildFaceTexture(_game, _gameSeed);
            _cursor = new Vector2(0.5f, (float)MineTuning.FaceHeight / 2);
            _swingAt = -10f;
            _skillBefore = _skill;
            _mode = Mode.Play;
        }

        private void Update()
        {
            if (_mode != Mode.Play || _game == null) return;

            // Keyboard aiming.
            Vector2 move = Vector2.zero;
            if (_held.Contains(KeyCode.A) || _held.Contains(KeyCode.LeftArrow)) move.x -= 1;
            if (_held.Contains(KeyCode.D) || _held.Contains(KeyCode.RightArrow)) move.x += 1;
            if (_held.Contains(KeyCode.W) || _held.Contains(KeyCode.UpArrow)) move.y -= 1;
            if (_held.Contains(KeyCode.S) || _held.Contains(KeyCode.DownArrow)) move.y += 1;
            if (move != Vector2.zero)
            {
                _cursor += move.normalized * CursorSpeed * Time.deltaTime;
                _cursor.x = Mathf.Clamp01(_cursor.x);
                _cursor.y = Mathf.Clamp(_cursor.y, 0, (float)MineTuning.FaceHeight);
            }

            _game.Update(Time.deltaTime);
            DrainEvents();
            if (_game.Stage == MineStage.Done)
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
                if (e.Kind == MineEventKind.Clean || e.Kind == MineEventKind.Fracture)
                {
                    _swingAt = Time.time;
                    _swingSpot = new Vector2((float)e.X, (float)e.Y);
                    _swingClean = e.Kind == MineEventKind.Clean;
                }
            }
            _game.Events.Clear();
        }

        private void Act()
        {
            if (_game.Stage == MineStage.Read) _game.Mark(_cursor.x, _cursor.y);
            else if (_game.Stage == MineStage.Strike) _game.Swing(_cursor.x, _cursor.y);
            DrainEvents();
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

            if (_mode != Mode.Play) return;

            // The mouse moves the pick whenever it moves over the face.
            var m = e.mousePosition;
            if (m != _lastMouse)
            {
                _lastMouse = m;
                if (FaceRect.Contains(m)) _cursor = ToFace(m);
            }
            if (e.type == EventType.MouseDown && e.button == 0 && FaceRect.Contains(m))
            {
                _cursor = ToFace(m);
                Act();
                e.Use();
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
                    if (key == KeyCode.Return || key == KeyCode.KeypadEnter) { StartMining(); return true; }
                    return false;
                case Mode.Results:
                    if (key == KeyCode.Return || key == KeyCode.KeypadEnter || key == KeyCode.Space) { StartMining(); return true; }
                    if (key == KeyCode.Escape) { _mode = Mode.Setup; return true; }
                    return false;
            }

            if (key == KeyCode.Escape) { _mode = Mode.Setup; return true; }
            if (key == KeyCode.Space) { Act(); return true; }
            if (key == KeyCode.E && _game.Stage == MineStage.Strike) { _game.StepBack(); return true; }
            return key == KeyCode.W || key == KeyCode.A || key == KeyCode.S || key == KeyCode.D ||
                   key == KeyCode.UpArrow || key == KeyCode.DownArrow || key == KeyCode.LeftArrow || key == KeyCode.RightArrow;
        }

        private static Vector2 ToFace(Vector2 gui) =>
            new Vector2((gui.x - FaceRect.x) / FaceRect.width, (gui.y - FaceRect.y) / FaceRect.width);

        private static Vector2 ToGui(Rect face, double x, double y) =>
            new Vector2(face.x + (float)x * face.width, face.y + (float)y * face.width);

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
            Fill(new Rect(0, 0, W, H), new Color(0.09f, 0.085f, 0.08f));

            switch (_mode)
            {
                case Mode.Intro: DrawIntro(); break;
                case Mode.Setup: DrawSetup(); break;
                case Mode.Play: DrawPlay(); break;
                case Mode.Results: DrawResults(); break;
            }

            if (Event.current.type == EventType.Repaint)
                Cursor.visible = !(_mode == Mode.Play && FaceRect.Contains(Event.current.mousePosition));
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

        /// <summary>A line of small squares (IMGUI has no line primitive).</summary>
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
            OreKind.Iron => new Color(0.37f, 0.36f, 0.37f),
            OreKind.Coal => new Color(0.33f, 0.30f, 0.27f),
            _ => new Color(0.44f, 0.40f, 0.35f),
        };

        private static Color OreColor(OreKind ore) => ore switch
        {
            OreKind.Iron => new Color(0.58f, 0.26f, 0.14f),
            OreKind.Coal => new Color(0.07f, 0.07f, 0.08f),
            _ => new Color(0.22f, 0.56f, 0.50f),
        };

        // ------------------------------------------------------------------ the rock face texture
        // Everything the player reads is painted here once per node: grain, a patchy stain along the seam, flecks of
        // ore (most near the seam, some stray ones elsewhere) and cracks (bedding cracks follow the seam, others don't).

        private const int TexW = 430;

        private void BuildFaceTexture(MineMinigame g, int seed)
        {
            int texH = Mathf.RoundToInt(TexW * (float)MineTuning.FaceHeight);
            if (_face == null || _face.width != TexW || _face.height != texH)
            {
                if (_face != null) Destroy(_face);
                _face = new Texture2D(TexW, texH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            }

            var rng = new System.Random(seed ^ 0x5eed);
            var ore = g.Node.Ore;
            Color rock = RockColor(ore), oreC = OreColor(ore);
            var seam = g.Seam;
            float nx = (float)rng.NextDouble() * 100f, ny = (float)rng.NextDouble() * 100f;
            float sx = (float)rng.NextDouble() * 100f;
            var px = new Color[TexW * texH];

            // Grain and a patchy seam stain.
            for (int j = 0; j < texH; j++)
            for (int i = 0; i < TexW; i++)
            {
                double u = (i + 0.5) / TexW, v = (j + 0.5) / TexW;
                float n = 0.55f * Mathf.PerlinNoise(nx + i * 0.035f, ny + j * 0.035f)
                        + 0.30f * Mathf.PerlinNoise(nx + i * 0.11f, ny + j * 0.11f)
                        + 0.15f * Mathf.PerlinNoise(nx + i * 0.4f, ny + j * 0.4f);
                Color c = rock * (0.72f + 0.55f * n);
                double d = seam.DistanceTo(u, v);
                float inSeam = Mathf.Clamp01(1f - (float)((d - seam.HalfWidth) / (seam.HalfWidth * 1.2)));
                float patch = Mathf.Clamp01(Mathf.PerlinNoise(sx + i * 0.06f, sx + j * 0.06f) * 1.6f - 0.35f);
                c = Color.Lerp(c, oreC * (0.8f + 0.4f * n), inSeam * patch * (ore == OreKind.Coal ? 0.35f : 0.22f));
                c.a = 1;
                px[(texH - 1 - j) * TexW + i] = c;
            }

            void Set(int i, int j, Color c)
            {
                if (i < 0 || j < 0 || i >= TexW || j >= texH) return;
                px[(texH - 1 - j) * TexW + i] = c;
            }

            Color Get(int i, int j) => (i < 0 || j < 0 || i >= TexW || j >= texH) ? Color.black : px[(texH - 1 - j) * TexW + i];

            double Gauss()
            {
                double a = 1.0 - rng.NextDouble(), b = rng.NextDouble();
                return System.Math.Sqrt(-2 * System.Math.Log(a)) * System.Math.Cos(2 * System.Math.PI * b);
            }

            // Flecks: about 70% sit in or near the seam, the rest are strays.
            double gx = seam.DirX, gy = seam.DirY, ax = -gy, ay = gx;
            for (int k = 0; k < 150; k++)
            {
                double u, v;
                if (rng.NextDouble() < 0.7)
                {
                    double along = (rng.NextDouble() * 2 - 1) * 1.2;
                    double across = Gauss() * seam.HalfWidth * 1.1;
                    u = seam.X + gx * along + ax * across;
                    v = seam.Y + gy * along + ay * across;
                }
                else
                {
                    u = rng.NextDouble();
                    v = rng.NextDouble() * MineTuning.FaceHeight;
                }
                int ci = (int)(u * TexW), cj = (int)(v * TexW);
                int size = 1 + rng.Next(3);
                Color fc = oreC * (0.85f + 0.3f * (float)rng.NextDouble());
                fc.a = 1;
                for (int a = 0; a < size; a++)
                for (int b = 0; b < size; b++)
                    if (rng.NextDouble() < 0.8) Set(ci + a, cj + b, fc);
                if (ore == OreKind.Coal && rng.NextDouble() < 0.5) Set(ci, cj, new Color(0.55f, 0.55f, 0.6f)); // glossy glint
            }

            // Cracks: a wobbling dark line, lit on one side.
            void Crack(double u, double v, double angle, double length)
            {
                double x = u * TexW, y = v * TexW;
                int steps = (int)(length * TexW);
                for (int s = 0; s < steps; s++)
                {
                    angle += (rng.NextDouble() * 2 - 1) * 0.25;
                    x += System.Math.Cos(angle);
                    y += System.Math.Sin(angle);
                    int i = (int)x, j = (int)y;
                    Set(i, j, Get(i, j) * 0.35f + new Color(0, 0, 0, 0.65f));
                    Set(i, j - 1, Get(i, j - 1) * 1.15f);
                }
            }

            for (int k = 0; k < 7; k++) // bedding cracks along the grain, a few hugging the seam's edges
            {
                double off = k < 3 ? (k - 1) * seam.HalfWidth * 1.15 : (rng.NextDouble() * 2 - 1) * 0.35;
                double along = (rng.NextDouble() * 2 - 1) * 0.6;
                Crack(seam.X + gx * along + ax * off, seam.Y + gy * along + ay * off,
                    seam.Angle + (rng.NextDouble() < 0.5 ? 0 : System.Math.PI), 0.12 + rng.NextDouble() * 0.3);
            }
            for (int k = 0; k < 5; k++) // random fractures that mean nothing
                Crack(rng.NextDouble(), rng.NextDouble() * MineTuning.FaceHeight, rng.NextDouble() * System.Math.PI * 2, 0.05 + rng.NextDouble() * 0.15);

            _face.SetPixels(px);
            _face.Apply();
        }

        // ------------------------------------------------------------------ intro
        // The old miner explains what each step is and why it matters, once, before the first node.
        // It teaches the craft, never the answer: no counts, no numbers, no "look for this exact sign".

        private static readonly (string Title, string Body)[] IntroPages =
        {
            ("The face",
             "So you want to dig. Good. Listen once, then learn with your hands.\n\n" +
             "Ore runs through rock in seams, bands of it, and a seam always shows itself if you look. Streaks of " +
             "colour, flecks, cracks that run alongside. Not every fleck tells the truth: bits wash out and lie where " +
             "there's nothing behind them. Look at the whole face before you choose.\n\n" +
             "Where you start matters most. Rock splits along its grain, so start on the seam and the seam comes out " +
             "with you. Start beside it and you'll break fine clean stone, and stone is all you'll carry home."),
            ("The pick",
             "Once you've started, the rock talks. It settles, it groans, and a crack opens where it wants to split. " +
             "Grit runs out of it. Strike it there, while it's open. It won't wait for you.\n\n" +
             "Swing anywhere else, or swing before the rock is ready, and it fractures. The ore crushes to grit and the " +
             "node wears out faster. Let a crack close and that piece stays in the rock for someone else.\n\n" +
             "You can always step back and leave the rest."),
            ("The vein",
             "Some veins are rich and some are poor, and no swing turns poor ore rich. The best of them lie deep, far " +
             "out, or where it's dangerous.\n\n" +
             "A worn pick holds you back, and so do untrained hands. Better tools and years at the face give you " +
             "more time to see each crack, and more of the vein's worth.\n\n" +
             "When a node's done, look at what you brought out and what you left. That's how a miner learns. Now go dig."),
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
            Fill(card, new Color(0.14f, 0.13f, 0.12f));
            Outline(card, new Color(0.42f, 0.38f, 0.3f), 2);
            Text(new Rect(card.x + 50, card.y + 34, 800, 40), "The old miner", 16, new Color(0.62f, 0.58f, 0.5f));
            Text(new Rect(card.x + 50, card.y + 60, 800, 60), $"<b>{page.Title}</b>", 34, new Color(0.95f, 0.82f, 0.5f));
            Text(new Rect(card.x + 50, card.y + 130, 800, 360), page.Body, 20, new Color(0.92f, 0.89f, 0.84f));
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
            Text(new Rect(40, 290, 360, 120), $"{node.Description}\n\n<b>Gives:</b> {node.OreName}", 15, new Color(0.85f, 0.82f, 0.78f));

            float x = 460;
            Text(new Rect(x, 80, 700, 30), $"Mining skill: <b>{_skill}</b> ({MineTuning.RankFor(_skill)})", 18, Color.white);
            int newSkill = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x, 115, 500, 20), _skill, 1, 100));
            if (newSkill != _skill) { _skill = newSkill; PlayerPrefs.SetInt(SkillPref, _skill); }
            Text(new Rect(x, 135, 780, 40), "Test slider (in game this is your character's skill; mining raises it).", 13, new Color(0.65f, 0.62f, 0.6f));
            if (_skill < node.RequiredSkill)
                Text(new Rect(x, 160, 780, 24), $"Below this node's rank ({node.RequiredSkill}). You can still practise on it.", 14, new Color(1f, 0.6f, 0.4f));

            Text(new Rect(x, 190, 400, 26), "Pick", 18, new Color(0.8f, 0.76f, 0.7f));
            for (int i = 0; i < Pick.All.Length; i++)
            {
                var p = Pick.All[i];
                if (Button(new Rect(x + i * 205, 218, 195, 36), p.Name, _setup.Pick == p)) _setup.Pick = p;
            }
            Text(new Rect(x, 256, 780, 22), _setup.Pick.Description, 13, new Color(0.65f, 0.62f, 0.6f));

            Text(new Rect(x, 290, 780, 26), "The node  <size=13><color=#a6a09a>(test sliders: in game these are hidden and rolled per node)</color></size>", 18, new Color(0.8f, 0.76f, 0.7f));
            Text(new Rect(x, 326, 200, 30), "Vein grade", 15, Color.white);
            _setup.VeinGrade = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x + 200, 334, 300, 20), _setup.VeinGrade, 1, 100));
            Text(new Rect(x + 520, 326, 260, 30), $"{_setup.VeinGrade}  <size=13><color=#a6a09a>caps ore quality</color></size>", 15, GradeColor(_setup.VeinGrade));
            Text(new Rect(x, 366, 200, 30), "Node size", 15, Color.white);
            _setup.NodeSize = Mathf.RoundToInt(GUI.HorizontalSlider(new Rect(x + 200, 374, 300, 20), _setup.NodeSize, 5, 15));
            Text(new Rect(x + 520, 366, 260, 30), $"{_setup.NodeSize} swings", 15, Color.white);

            if (Button(new Rect(x, 630, 300, 56), "<b>Start mining</b>  <size=13>(Enter)</size>", true)) StartMining();
            if (Button(new Rect(40, 630, 360, 56), "Hear the old miner again")) { _introPage = 0; _mode = Mode.Intro; }
            Text(new Rect(x + 320, 630, 470, 60),
                "Controls: mouse or WASD / arrows move the pick · click or SPACE mark where to start, then swing · E step back · ESC leave.",
                13, new Color(0.65f, 0.62f, 0.6f));
        }

        // ------------------------------------------------------------------ play screen
        // No prompts, gauges, markers or zones: the player reads the face, then watches and listens for each crack.

        private void DrawPlay()
        {
            var g = _game;
            Text(new Rect(30, 14, 900, 40), $"<b>{g.Node.Name}</b>", 26, new Color(0.95f, 0.82f, 0.5f));

            // A small shake on each blow.
            float since = Time.time - _swingAt;
            var face = FaceRect;
            if (since < 0.12f)
            {
                float s = (_swingClean ? 2f : 3.5f) * (1 - since / 0.12f);
                face.x += Mathf.Sin(Time.time * 90f) * s;
                face.y += Mathf.Cos(Time.time * 70f) * s * 0.6f;
            }

            Fill(new Rect(face.x - 14, face.y - 14, face.width + 28, face.height + 28), new Color(0.15f, 0.13f, 0.12f));
            if (_face != null) GUI.DrawTexture(face, _face);

            DrawWorkedRock(g, face);
            if (g.Stage == MineStage.Strike) DrawOpenCrack(g, face);
            if (g.Stage != MineStage.Read) DrawChalkMark(ToGui(face, g.MarkX, g.MarkY), 0.35f);
            DrawImpact(face, since);
            DrawRubble(g);
            DrawPile(g);
            DrawCursor(g, face);
            DrawControls(g);
        }

        private void DrawControls(MineMinigame g)
        {
            string keys = g.Stage switch
            {
                MineStage.Read => "<b>MOUSE</b> or <b>WASD</b> move   <b>CLICK</b> or <b>SPACE</b> mark where to start",
                MineStage.Strike => "<b>MOUSE</b> or <b>WASD</b> aim   <b>CLICK</b> or <b>SPACE</b> swing   <b>E</b> step back",
                _ => "",
            };
            var bar = new Rect(0, H - 40, W, 40);
            Fill(bar, new Color(0.055f, 0.05f, 0.045f));
            Text(new Rect(30, bar.y, W - 60, bar.height), keys + "      <color=#7d746c>ESC leave</color>", 16, new Color(0.85f, 0.82f, 0.78f), TextAnchor.MiddleLeft);
        }

        /// <summary>Hollows where chunks came away cleanly; pale crushed grit where blows fractured the rock.</summary>
        private void DrawWorkedRock(MineMinigame g, Rect face)
        {
            var rock = RockColor(g.Node.Ore);
            for (int i = 0; i < g.Points.Count; i++)
            {
                var p = g.Points[i];
                if (p.State != PointState.Clean) continue;
                var c = ToGui(face, p.X, p.Y);
                var rng = new System.Random(_gameSeed + i * 31);
                for (int k = 0; k < 5; k++)
                {
                    float w = 18 + rng.Next(14), h = 12 + rng.Next(10);
                    float dx = rng.Next(-10, 11), dy = rng.Next(-7, 8);
                    Fill(new Rect(c.x + dx - w / 2, c.y + dy - h / 2, w, h), Shade(rock, 0.38f + 0.06f * k));
                }
                Fill(new Rect(c.x - 12, c.y - 10, 24, 2), Shade(rock, 1.25f));
            }
            for (int i = 0; i < g.FractureMarks.Count; i++)
            {
                var (fx, fy) = g.FractureMarks[i];
                var c = ToGui(face, fx, fy);
                var rng = new System.Random(_gameSeed + 7 + i * 17);
                for (int k = 0; k < 26; k++)
                {
                    float r = (float)rng.NextDouble() * 18f, a = (float)rng.NextDouble() * Mathf.PI * 2;
                    float s = 2 + rng.Next(3);
                    Fill(new Rect(c.x + Mathf.Cos(a) * r, c.y + Mathf.Sin(a) * r, s, s), new Color(0.72f, 0.70f, 0.66f, 0.85f));
                }
                for (int k = 0; k < 4; k++)
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2;
                    Dots(c, c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (14 + rng.Next(14)), new Color(0.1f, 0.09f, 0.08f), 2f, 2f);
                }
            }
        }

        /// <summary>
        /// The one cue in play: a hairline crack that opens where the rock wants to split, with grit trickling out,
        /// then closes again. It is drawn in the rock's own colours, not as a marker.
        /// </summary>
        private void DrawOpenCrack(MineMinigame g, Rect face)
        {
            var p = g.CurrentPoint;
            if (p == null || p.State != PointState.Showing) return;
            float t = (float)g.OpenFor, life = (float)g.OpenSeconds;
            float alpha = Mathf.Clamp01(t / 0.12f) * Mathf.Clamp01((life - t) / (life * 0.3f));
            var c = ToGui(face, p.X, p.Y);
            var rng = new System.Random(_gameSeed * 13 + g.Current);

            // A jagged hairline through the point.
            float angle = (float)(rng.NextDouble() * Mathf.PI);
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 a = c - dir * 14, prev = a;
            for (int k = 1; k <= 6; k++)
            {
                Vector2 next = a + dir * (28f * k / 6f) + new Vector2(-dir.y, dir.x) * (float)(rng.NextDouble() * 6 - 3);
                Dots(prev, next, new Color(0.05f, 0.04f, 0.04f, 0.9f * alpha), 2f, 1.5f);
                prev = next;
            }
            // Grit running out of it.
            for (int k = 0; k < 6; k++)
            {
                float phase = (t * 1.6f + k / 6f) % 1f;
                float gx = c.x + (float)(rng.NextDouble() * 20 - 10);
                Fill(new Rect(gx, c.y + phase * 40f, 2, 2), new Color(0.8f, 0.77f, 0.7f, alpha * (1 - phase)));
            }
        }

        private void DrawImpact(Rect face, float since)
        {
            if (since > 0.15f) return;
            var c = ToGui(face, _swingSpot.x, _swingSpot.y);
            var rng = new System.Random(Mathf.FloorToInt(_swingAt * 1000));
            float k = since / 0.15f;
            for (int i = 0; i < 10; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2, r = 4 + k * (20 + rng.Next(20));
                Fill(new Rect(c.x + Mathf.Cos(a) * r, c.y + Mathf.Sin(a) * r, 3, 3), new Color(1f, 0.95f, 0.8f, 1 - k));
            }
        }

        /// <summary>Crushed rock collecting at the foot of the face, a little more with every fracture.</summary>
        private void DrawRubble(MineMinigame g)
        {
            var rng = new System.Random(_gameSeed + 99);
            int bits = g.Fractures * 14;
            for (int i = 0; i < bits; i++)
            {
                float x = FaceRect.x + 40 + (float)rng.NextDouble() * (FaceRect.width - 80);
                float y = FaceRect.yMax + 16 + (float)rng.NextDouble() * 10;
                Fill(new Rect(x, y, 3, 2), new Color(0.6f, 0.58f, 0.55f));
            }
        }

        /// <summary>The ore cart: one lump per clean split, coloured by how much ore it held.</summary>
        private void DrawPile(MineMinigame g)
        {
            var cart = new Rect(1060, 470, 190, 110);
            Fill(cart, new Color(0.25f, 0.18f, 0.12f));
            Outline(cart, new Color(0.38f, 0.28f, 0.2f), 3);
            Fill(new Rect(cart.x + 20, cart.yMax, 30, 30), new Color(0.2f, 0.2f, 0.22f));
            Fill(new Rect(cart.xMax - 50, cart.yMax, 30, 30), new Color(0.2f, 0.2f, 0.22f));
            Color rock = RockColor(g.Node.Ore), oreC = OreColor(g.Node.Ore);
            int n = 0;
            for (int i = 0; i < g.Points.Count; i++)
            {
                var p = g.Points[i];
                if (p.State != PointState.Clean) continue;
                int col = n % 5, row = n / 5;
                var lump = new Rect(cart.x + 12 + col * 34 + (row % 2) * 14, cart.y - 18 - row * 18, 30, 22);
                Fill(lump, Color.Lerp(rock, oreC, (float)p.OreContent * 0.85f));
                Fill(new Rect(lump.x + 3, lump.y + 2, lump.width - 10, 3), Color.Lerp(rock, oreC, (float)p.OreContent) * 1.3f);
                n++;
            }
        }

        private void DrawChalkMark(Vector2 c, float alpha)
        {
            var chalk = new Color(0.93f, 0.92f, 0.88f, alpha);
            Dots(c + new Vector2(-9, -9), c + new Vector2(9, 9), chalk, 3f, 2f);
            Dots(c + new Vector2(-9, 9), c + new Vector2(9, -9), chalk, 3f, 2f);
        }

        /// <summary>A stick of chalk while reading, the pick's point while striking.</summary>
        private void DrawCursor(MineMinigame g, Rect face)
        {
            if (g.Stage == MineStage.Done) return;
            var c = ToGui(face, _cursor.x, _cursor.y);
            if (g.Stage == MineStage.Read)
            {
                DrawChalkMark(c, 0.55f);
                return;
            }
            // Pick head: a dark iron spike pointing at the spot, with the haft going off to the lower right.
            Dots(c + new Vector2(30, 34), c + new Vector2(60, 70), new Color(0.42f, 0.29f, 0.17f), 6f, 3f);
            Dots(c, c + new Vector2(30, 34), new Color(0.2f, 0.2f, 0.22f), 5f, 2f);
            Dots(c + new Vector2(16, 44), c + new Vector2(44, 22), new Color(0.24f, 0.24f, 0.26f), 6f, 2f);
            Fill(new Rect(c.x - 1, c.y - 1, 3, 3), new Color(0.75f, 0.75f, 0.78f));
        }

        // ------------------------------------------------------------------ results screen

        private void DrawResults()
        {
            var r = _game.Result;
            var g = _game;
            var gc = r.Yield > 0 ? GradeColor(r.Quality) : new Color(0.7f, 0.65f, 0.6f);
            string title = r.Yield > 0 ? $"{r.ItemName} × {r.Yield}" : r.ItemName;
            Text(new Rect(40, 26, 1200, 60), $"<b>{title}</b>", 40, gc);
            Text(new Rect(40, 80, 1200, 30), r.Yield > 0 ? $"Quality <b>{r.Quality}</b>  ·  from a vein of grade {r.VeinGrade}" : "Nothing worth carrying came out of this node.", 20, Color.white);

            string markWord = r.MarkOffset <= 1 ? "on the seam" : r.MarkOffset <= 2.5 ? "just off the seam" : "well off the seam";
            string capNote = r.RawQuality > r.Ceiling ? $" → capped at <b>{r.Ceiling}</b> by {r.CeilingReason}" : "";
            string body =
                $"<b>Read</b>  You started {markWord}. Your clean chunks were {r.Read:P0} ore on average.\n" +
                $"<size=14><color=#a9a294>The map on the right shows where the seam really ran.</color></size>\n\n" +
                $"<b>Yield</b>  node {r.NodeSize} × yield {r.YieldScore:0.00} = <b>{r.Yield}</b>\n" +
                $"<size=14><color=#a9a294>yield = ore in the clean chunks ÷ node size</color></size>\n\n" +
                $"<b>Quality</b>  vein {r.VeinGrade} × (0.5 + 0.5 × clean breaks {r.BreakScore:0.00}) = {r.RawQuality}{capNote}\n" +
                $"<size=14><color=#a9a294>clean breaks = clean splits ÷ (clean splits + fractures); cap = {r.CeilingReason}, {r.Ceiling}</color></size>\n\n" +
                $"<b>Chunks</b>  split clean {r.Clean} · cracks left to close {r.Missed} · crushed by fractures {r.Crushed}" +
                (r.Left > 0 ? $" · left in the node {r.Left}" : "") + "\n" +
                $"<b>Fractures</b>  {r.Fractures}" + (r.Fractures > 0 ? "  <size=14><color=#a9a294>(blows off the open crack, or before the rock was ready)</color></size>" : "") + "\n\n" +
                $"<b>Skill</b>  {_skillBefore} → {_skill}" + (r.SkillGain == 0 ? "  <size=14><color=#a9a294>(no gain: nothing came out, or this node is too easy for you)</color></size>" : "");
            Text(new Rect(40, 124, 700, 480), body, 18, new Color(0.9f, 0.88f, 0.85f));

            DrawFaceMap(g, new Rect(770, 130, 470, 470 * (float)MineTuning.FaceHeight));

            if (Button(new Rect(40, 620, 260, 56), "<b>Mine again</b> (Enter)", true)) StartMining();
            if (Button(new Rect(320, 620, 260, 56), "Change setup (Esc)")) _mode = Mode.Setup;
        }

        /// <summary>The face again, with the seam revealed, the chalk mark and what happened at each weak point.</summary>
        private void DrawFaceMap(MineMinigame g, Rect map)
        {
            if (_face != null) GUI.DrawTexture(map, _face);
            Outline(map, new Color(0.4f, 0.38f, 0.35f), 1);

            var s = g.Seam;
            var band = new Color(1f, 0.85f, 0.4f, 0.55f);
            for (int side = -1; side <= 1; side += 2)
            {
                double ox = -s.DirY * s.HalfWidth * side, oy = s.DirX * s.HalfWidth * side;
                Vector2? prev = null;
                for (int k = -40; k <= 40; k++)
                {
                    double t = k / 40.0 * 1.2;
                    double x = s.X + s.DirX * t + ox, y = s.Y + s.DirY * t + oy;
                    Vector2? here = MineMinigame.InsideFace(x, y) ? ToGui(map, x, y) : (Vector2?)null;
                    if (prev.HasValue && here.HasValue) Dots(prev.Value, here.Value, band, 2f, 3f);
                    prev = here;
                }
            }

            foreach (var p in g.Points)
            {
                var c = ToGui(map, p.X, p.Y);
                Color col = p.State switch
                {
                    PointState.Clean => new Color(0.45f, 0.9f, 0.45f),
                    PointState.Crushed => new Color(1f, 0.4f, 0.3f),
                    PointState.Missed => new Color(0.75f, 0.75f, 0.75f),
                    _ => new Color(0.45f, 0.45f, 0.5f),
                };
                Fill(new Rect(c.x - 4, c.y - 4, 8, 8), col);
            }
            DrawChalkMark(ToGui(map, g.MarkX, g.MarkY), 0.9f);

            float y0 = map.yMax + 10;
            Text(new Rect(map.x, y0, map.width, 60),
                "<color=#ffd966>— seam</color>   <color=#73e673>■ clean</color>   <color=#d9d9d9>■ closed</color>   <color=#ff6650>■ crushed</color>   <color=#737380>■ left</color>   X your mark",
                14, new Color(0.8f, 0.78f, 0.75f));
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
