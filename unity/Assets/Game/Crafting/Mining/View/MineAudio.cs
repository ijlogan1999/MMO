using UnityEngine;

namespace Mmo.Crafting.Mining.View
{
    /// <summary>Placeholder sounds generated in code, so the prototype needs no audio files.</summary>
    public sealed class MineAudio : MonoBehaviour
    {
        private const int Rate = 44100;
        private AudioSource _source;
        private AudioClip _ring, _thud, _hollow, _split, _crunch, _bounce, _smash, _creak, _collapse, _prop;

        private void Awake()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;

            var noise = new System.Random(11);
            float N() => (float)noise.NextDouble() * 2 - 1;

            // Taps: ore rings bright, sound rock thuds, broken rock sounds hollow.
            _ring = Make("ring", 0.5f, t => (Mathf.Sin(2 * Mathf.PI * 1250 * t) + 0.6f * Mathf.Sin(2 * Mathf.PI * 2710 * t)) * Mathf.Exp(-t * 9) * 0.25f);
            _thud = Make("thud", 0.18f, t => Mathf.Sin(2 * Mathf.PI * 140 * t) * Mathf.Exp(-t * 30) * 0.5f + N() * Mathf.Exp(-t * 80) * 0.15f);
            _hollow = Make("hollow", 0.3f, t => Mathf.Sin(2 * Mathf.PI * 320 * t) * Mathf.Sin(2 * Mathf.PI * 7 * t + 1) * Mathf.Exp(-t * 12) * 0.4f + N() * Mathf.Exp(-t * 40) * 0.1f);
            // A clean split: bite, then the block lets go and tumbles.
            _split = Make("split", 0.7f, t =>
                N() * Mathf.Exp(-t * 40) * 0.6f +
                Mathf.Sin(2 * Mathf.PI * 85 * t) * Mathf.Exp(-t * 6) * 0.35f * Mathf.Clamp01((t - 0.05f) * 30));
            _crunch = Make("crunch", 0.55f, t =>
                Mathf.Sin(2 * Mathf.PI * 120 * t) * Mathf.Exp(-t * 20) * 0.45f +
                N() * Mathf.Exp(-t * 5) * 0.3f * (0.6f + 0.4f * Mathf.Sin(2 * Mathf.PI * 30 * t)));
            _bounce = Make("bounce", 0.3f, t => Mathf.Sin(2 * Mathf.PI * 1900 * t) * Mathf.Exp(-t * 35) * 0.3f + Mathf.Sin(2 * Mathf.PI * 160 * t) * Mathf.Exp(-t * 25) * 0.3f);
            _smash = Make("smash", 0.8f, t => Mathf.Sin(2 * Mathf.PI * 70 * t) * Mathf.Exp(-t * 8) * 0.5f + N() * Mathf.Exp(-t * 4) * 0.35f);
            _creak = Make("creak", 0.9f, t =>
                Mathf.Sin(2 * Mathf.PI * (55 + 30 * t) * t) * Mathf.Sin(Mathf.PI * t / 0.9f) * 0.3f +
                N() * (noise.NextDouble() < 0.02 ? 0.25f : 0.01f));
            _collapse = Make("collapse", 2.2f, t => (N() * 0.6f + Mathf.Sin(2 * Mathf.PI * 45 * t) * 0.4f) * Mathf.Exp(-t * 1.4f) * Mathf.Clamp01(t * 20) * 0.7f);
            _prop = Make("prop", 0.25f, t => Mathf.Sin(2 * Mathf.PI * 220 * t) * Mathf.Exp(-t * 20) * 0.4f);
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

        public void Play(MineEvent e)
        {
            switch (e.Kind)
            {
                case MineEventKind.Tapped:
                    _source.PlayOneShot(e.Message == "ring" ? _ring : e.Message == "hollow" ? _hollow : _thud);
                    break;
                case MineEventKind.RoofTapped:
                    _source.PlayOneShot(e.Message == "Solid" ? _thud : _hollow);
                    break;
                case MineEventKind.Freed:
                case MineEventKind.Pried: _source.PlayOneShot(_split); break;
                case MineEventKind.Shattered: _source.PlayOneShot(_crunch); break;
                case MineEventKind.Bounced: _source.PlayOneShot(_bounce); break;
                case MineEventKind.Smashed: _source.PlayOneShot(_smash); break;
                case MineEventKind.Creak: _source.PlayOneShot(_creak); break;
                case MineEventKind.Collapse: _source.PlayOneShot(_collapse); break;
                case MineEventKind.Propped: _source.PlayOneShot(_prop); break;
            }
        }
    }
}
