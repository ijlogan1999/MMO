using UnityEngine;

namespace Mmo.Crafting.Skinning.View
{
    /// <summary>Placeholder sounds generated in code, so the prototype needs no audio files.</summary>
    public sealed class SkinAudio : MonoBehaviour
    {
        private const int Rate = 44100;
        private AudioSource _source;
        private AudioClip _feel, _peel, _stretch, _tear, _slice, _score, _hone, _gut, _drag, _scrape, _skid, _swell;

        private void Awake()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;

            var noise = new System.Random(23);
            float N() => (float)noise.NextDouble() * 2 - 1;

            // Fingers through fur: a soft rustle.
            _feel = Make("feel", 0.35f, t => N() * Mathf.Sin(Mathf.PI * t / 0.35f) * 0.08f);
            // Hide peeling off: a crackling, sticky pull.
            _peel = Make("peel", 0.6f, t => N() * (noise.NextDouble() < 0.25 ? 0.35f : 0.05f) * Mathf.Sin(Mathf.PI * t / 0.6f));
            // Stretching: the same pull, slower, with a low groan under it.
            _stretch = Make("stretch", 0.8f, t =>
                N() * (noise.NextDouble() < 0.12 ? 0.25f : 0.04f) * Mathf.Sin(Mathf.PI * t / 0.8f) +
                Mathf.Sin(2 * Mathf.PI * (90 - 30 * t) * t) * 0.15f * Mathf.Sin(Mathf.PI * t / 0.8f));
            // Tearing: a loud, ragged rip.
            _tear = Make("tear", 0.45f, t => N() * Mathf.Exp(-t * 4) * 0.55f * (0.5f + 0.5f * Mathf.Sin(2 * Mathf.PI * 45 * t)));
            // A clean knife stroke.
            _slice = Make("slice", 0.25f, t => N() * Mathf.Exp(-t * 14) * 0.25f + Mathf.Sin(2 * Mathf.PI * 3200 * t) * Mathf.Exp(-t * 30) * 0.05f);
            // Scoring: the blade catching.
            _score = Make("score", 0.3f, t => N() * Mathf.Exp(-t * 10) * 0.3f + Mathf.Sin(2 * Mathf.PI * 600 * t) * Mathf.Exp(-t * 20) * 0.2f);
            // Steel on stone, two strokes.
            _hone = Make("hone", 0.9f, t =>
            {
                float s = t < 0.4f ? Mathf.Sin(Mathf.PI * t / 0.4f) : t > 0.5f ? Mathf.Sin(Mathf.PI * (t - 0.5f) / 0.4f) : 0;
                return N() * s * 0.18f + Mathf.Sin(2 * Mathf.PI * 4100 * t) * s * 0.03f;
            });
            _gut = Make("dress", 0.4f, t => Mathf.Sin(2 * Mathf.PI * 110 * t) * Mathf.Exp(-t * 10) * 0.3f);
            _drag = Make("drag", 0.4f, t => N() * Mathf.Sin(Mathf.PI * t / 0.4f) * 0.2f * (0.6f + 0.4f * Mathf.Sin(2 * Mathf.PI * 25 * t)));
            _scrape = Make("scrape", 0.4f, t => N() * Mathf.Sin(Mathf.PI * t / 0.4f) * 0.15f);
            _skid = Make("skid", 0.2f, t => N() * Mathf.Exp(-t * 20) * 0.15f + Mathf.Sin(2 * Mathf.PI * 300 * t) * Mathf.Exp(-t * 25) * 0.15f);
            _swell = Make("swell", 0.8f, t => Mathf.Sin(2 * Mathf.PI * (60 + 20 * t) * t) * Mathf.Sin(Mathf.PI * t / 0.8f) * 0.15f);
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

        public void Play(SkinEvent e)
        {
            switch (e.Kind)
            {
                case SkinEventKind.Felt: _source.PlayOneShot(_feel); break;
                case SkinEventKind.CameAway: _source.PlayOneShot(e.Message.Contains("cut") ? _slice : _peel); break;
                case SkinEventKind.Stretched: _source.PlayOneShot(_stretch); break;
                case SkinEventKind.Tore: _source.PlayOneShot(_tear); break;
                case SkinEventKind.Holed: _source.PlayOneShot(_slice); break;
                case SkinEventKind.Scored: _source.PlayOneShot(_score); break;
                case SkinEventKind.Honed: _source.PlayOneShot(_hone); break;
                case SkinEventKind.Gutted: _source.PlayOneShot(_gut); break;
                case SkinEventKind.Drag: _source.PlayOneShot(_drag); break;
                case SkinEventKind.Swelling: _source.PlayOneShot(_swell); break;
                case SkinEventKind.Scraped: _source.PlayOneShot(_scrape); break;
                case SkinEventKind.Trimmed: _source.PlayOneShot(_slice); break;
                case SkinEventKind.Skid: _source.PlayOneShot(_skid); break;
            }
        }
    }
}
