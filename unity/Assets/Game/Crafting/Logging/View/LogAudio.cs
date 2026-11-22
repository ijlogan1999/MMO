using UnityEngine;

namespace Mmo.Crafting.Logging.View
{
    /// <summary>Placeholder sounds generated in code, so the prototype needs no audio files.</summary>
    public sealed class LogAudio : MonoBehaviour
    {
        private const int Rate = 44100;
        private AudioSource _source;
        private AudioClip _ring, _dull, _boom, _chop, _saw, _wedge, _creak, _crash, _crack, _pinch, _grind, _split, _thump;

        private void Awake()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;

            var noise = new System.Random(13);
            float N() => (float)noise.NextDouble() * 2 - 1;

            // Knocks: sound wood rings, rot sounds dull, a hollow booms.
            _ring = Make("ring", 0.35f, t => (Mathf.Sin(2 * Mathf.PI * 520 * t) + 0.4f * Mathf.Sin(2 * Mathf.PI * 1310 * t)) * Mathf.Exp(-t * 14) * 0.3f);
            _dull = Make("dull", 0.15f, t => Mathf.Sin(2 * Mathf.PI * 170 * t) * Mathf.Exp(-t * 45) * 0.45f + N() * Mathf.Exp(-t * 90) * 0.1f);
            _boom = Make("boom", 0.6f, t => Mathf.Sin(2 * Mathf.PI * 95 * t) * Mathf.Exp(-t * 6) * 0.5f + Mathf.Sin(2 * Mathf.PI * 190 * t) * Mathf.Exp(-t * 12) * 0.2f);
            // An axe biting into wood.
            _chop = Make("chop", 0.2f, t => (Mathf.Sin(2 * Mathf.PI * 210 * t) * 0.6f + N() * 0.5f) * Mathf.Exp(-t * 32) * 0.55f);
            // One push-pull of the saw: a rasp that swells and fades.
            _saw = Make("saw", 0.45f, t => N() * Mathf.Sin(Mathf.PI * t / 0.45f) * (0.6f + 0.4f * Mathf.Sin(2 * Mathf.PI * 60 * t)) * 0.22f);
            _wedge = Make("wedge", 0.3f, t => (Mathf.Sin(2 * Mathf.PI * 1600 * t) + 0.5f * Mathf.Sin(2 * Mathf.PI * 2350 * t)) * Mathf.Exp(-t * 22) * 0.2f + N() * Mathf.Exp(-t * 60) * 0.2f);
            _creak = Make("creak", 0.9f, t => Mathf.Sin(2 * Mathf.PI * (70 + 40 * t) * t) * Mathf.Sin(Mathf.PI * t / 0.9f) * 0.3f + N() * (noise.NextDouble() < 0.03 ? 0.25f : 0.01f));
            // The fall: a rising rush of air, then the crash.
            _crash = Make("crash", 2.6f, t =>
                N() * Mathf.Clamp01(t / 1.2f) * (t < 1.3f ? 0.25f : 0f) +
                (t >= 1.3f ? (N() * 0.6f + Mathf.Sin(2 * Mathf.PI * 50 * t) * 0.5f) * Mathf.Exp(-(t - 1.3f) * 2.5f) : 0f) * 0.8f);
            _crack = Make("crack", 0.4f, t => N() * Mathf.Exp(-t * 18) * 0.7f + Mathf.Sin(2 * Mathf.PI * 140 * t) * Mathf.Exp(-t * 12) * 0.3f);
            _pinch = Make("pinch", 0.5f, t => Mathf.Sin(2 * Mathf.PI * (900 + 300 * Mathf.Sin(t * 30)) * t) * Mathf.Sin(Mathf.PI * t / 0.5f) * 0.12f);
            _grind = Make("grind", 0.4f, t => N() * Mathf.Sin(Mathf.PI * t / 0.4f) * 0.3f * (0.5f + 0.5f * Mathf.Sin(2 * Mathf.PI * 25 * t)));
            _split = Make("split", 0.25f, t => N() * Mathf.Exp(-t * 25) * 0.5f + Mathf.Sin(2 * Mathf.PI * 260 * t) * Mathf.Exp(-t * 30) * 0.3f);
            _thump = Make("thump", 0.25f, t => Mathf.Sin(2 * Mathf.PI * 80 * t) * Mathf.Exp(-t * 20) * 0.5f);
        }

        private static AudioClip Make(string name, float seconds, System.Func<float, float> wave)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = wave(i / (float)Rate);
            var clip = AudioClip.Create(name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        public void Play(LogEvent e)
        {
            switch (e.Kind)
            {
                case LogEventKind.Tapped:
                    _source.PlayOneShot(e.Message == "ring" ? _ring : e.Message == "boom" ? _boom : _dull);
                    break;
                case LogEventKind.Chopped: _source.PlayOneShot(_chop); break;
                case LogEventKind.Sawed: _source.PlayOneShot(_saw); break;
                case LogEventKind.Bucked: _source.PlayOneShot(_saw); break;
                case LogEventKind.Wedged: _source.PlayOneShot(_wedge); break;
                case LogEventKind.Creak: _source.PlayOneShot(_creak); break;
                case LogEventKind.Fell: _source.PlayOneShot(_crash); break;
                case LogEventKind.HungUp:
                case LogEventKind.Cracked:
                case LogEventKind.Kickback:
                case LogEventKind.BarberChair: _source.PlayOneShot(_crack); break;
                case LogEventKind.Pinched:
                case LogEventKind.BuckPinched: _source.PlayOneShot(_pinch); break;
                case LogEventKind.HitDirt: _source.PlayOneShot(_grind); break;
                case LogEventKind.Refused:
                case LogEventKind.TooTired: _source.PlayOneShot(_thump); break;
            }
        }

        public void PlaySplit() => _source.PlayOneShot(_split);
    }
}
