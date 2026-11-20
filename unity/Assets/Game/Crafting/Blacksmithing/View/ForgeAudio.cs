using UnityEngine;

namespace Mmo.Crafting.Blacksmithing.View
{
    /// <summary>Placeholder sounds generated in code, so the prototype needs no audio files.</summary>
    public sealed class ForgeAudio : MonoBehaviour
    {
        private const int Rate = 44100;
        private AudioSource _source;
        private AudioClip _tick, _clang, _clangSoft, _thud, _hiss, _scrape, _crack;

        private void Awake()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;

            var noise = new System.Random(7);
            _tick = Make("tick", 0.05f, t => Mathf.Sin(2 * Mathf.PI * 1500 * t) * Mathf.Exp(-t * 60) * 0.35f);
            _clang = Make("clang", 0.6f, t => Metal(t, 1f) * 0.3f);
            _clangSoft = Make("clang_soft", 0.4f, t => Metal(t, 0.8f) * 0.18f);
            _thud = Make("thud", 0.25f, t => Mathf.Sin(2 * Mathf.PI * 110 * t) * Mathf.Exp(-t * 18) * 0.5f);
            _hiss = Make("hiss", 2.0f, t => ((float)noise.NextDouble() * 2 - 1) * Mathf.Exp(-t * 1.2f) * 0.25f);
            _scrape = Make("scrape", 0.3f, t => ((float)noise.NextDouble() * 2 - 1) * Mathf.Sin(Mathf.PI * t / 0.3f) * 0.2f);
            _crack = Make("crack", 0.3f, t => ((float)noise.NextDouble() * 2 - 1) * Mathf.Exp(-t * 25) * 0.6f);
        }

        // A few inharmonic partials decaying at different speeds sound roughly like a hammer on steel.
        private static float Metal(float t, float pitch)
        {
            float s = 0;
            s += Mathf.Sin(2 * Mathf.PI * 820 * pitch * t) * Mathf.Exp(-t * 6);
            s += Mathf.Sin(2 * Mathf.PI * 1340 * pitch * t) * Mathf.Exp(-t * 9) * 0.7f;
            s += Mathf.Sin(2 * Mathf.PI * 2210 * pitch * t) * Mathf.Exp(-t * 14) * 0.5f;
            s += Mathf.Sin(2 * Mathf.PI * 3050 * pitch * t) * Mathf.Exp(-t * 22) * 0.4f;
            return s * Mathf.Min(1f, t * 2000f);
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

        public void Play(ForgeEvent e)
        {
            switch (e.Kind)
            {
                case ForgeEventKind.Beat: _source.PlayOneShot(_tick); break;
                case ForgeEventKind.Strike: _source.PlayOneShot(e.Judgement == Judgement.Perfect ? _clang : _clangSoft); break;
                case ForgeEventKind.OffBeat:
                case ForgeEventKind.ColdStrike:
                case ForgeEventKind.Miss: _source.PlayOneShot(_thud); break;
                case ForgeEventKind.QuenchStart: _source.PlayOneShot(_hiss); break;
                case ForgeEventKind.Cracked:
                case ForgeEventKind.QuenchCrack: _source.PlayOneShot(_crack); break;
                case ForgeEventKind.FinishStroke: _source.PlayOneShot(_scrape); break;
            }
        }
    }
}
