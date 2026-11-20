using UnityEngine;

namespace Mmo.Crafting.Cooking.View
{
    /// <summary>Placeholder sounds generated in code, so the prototype needs no audio files.</summary>
    public sealed class CookAudio : MonoBehaviour
    {
        private const int Rate = 44100;
        private AudioSource _source, _fire;
        private AudioClip _chop, _sizzle, _slosh, _skim, _plop, _crackle, _carve, _clink, _thud, _whoosh, _fireLoop;

        private void Awake()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;

            var noise = new System.Random(11);
            float N() => (float)noise.NextDouble() * 2 - 1;
            _chop = Make("chop", 0.12f, t => (Mathf.Sin(2 * Mathf.PI * 180 * t) * 0.6f + N() * 0.4f) * Mathf.Exp(-t * 45) * 0.6f);
            _sizzle = Make("sizzle", 0.7f, t => N() * Mathf.Exp(-t * 4) * Mathf.Min(1, t * 60) * 0.22f);
            _slosh = Make("slosh", 0.35f, t => N() * Mathf.Sin(Mathf.PI * t / 0.35f) * (0.5f + 0.5f * Mathf.Sin(2 * Mathf.PI * 9 * t)) * 0.18f);
            _skim = Make("skim", 0.25f, t => N() * Mathf.Sin(Mathf.PI * t / 0.25f) * 0.12f);
            _plop = Make("plop", 0.2f, t => Mathf.Sin(2 * Mathf.PI * (300 - 900 * t) * t) * Mathf.Exp(-t * 20) * 0.5f);
            _crackle = Make("crackle", 0.6f, t => (noise.NextDouble() < 0.004 ? 1f : 0f) * 0.7f + N() * Mathf.Exp(-t * 8) * 0.05f);
            _carve = Make("carve", 0.4f, t => N() * Mathf.Sin(Mathf.PI * t / 0.4f) * 0.15f);
            _clink = Make("clink", 0.3f, t => (Mathf.Sin(2 * Mathf.PI * 2400 * t) + 0.5f * Mathf.Sin(2 * Mathf.PI * 3700 * t)) * Mathf.Exp(-t * 18) * 0.15f);
            _thud = Make("thud", 0.15f, t => Mathf.Sin(2 * Mathf.PI * 120 * t) * Mathf.Exp(-t * 25) * 0.35f);
            _whoosh = Make("whoosh", 0.5f, t => N() * Mathf.Sin(Mathf.PI * t / 0.5f) * 0.12f);
            // Soft fire bed with the odd pop, looped while cooking.
            _fireLoop = Make("fire", 3f, t => N() * 0.025f + (noise.NextDouble() < 0.0006 ? 0.5f : 0f));

            _fire = gameObject.AddComponent<AudioSource>();
            _fire.clip = _fireLoop;
            _fire.loop = true;
            _fire.playOnAwake = false;
            _fire.spatialBlend = 0f;
            _fire.volume = 0.8f;
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

        public void SetFire(bool on)
        {
            if (on && !_fire.isPlaying) _fire.Play();
            else if (!on && _fire.isPlaying) _fire.Stop();
        }

        public void Play(CookEvent e)
        {
            switch (e.Kind)
            {
                case CookEventKind.Chop: _source.PlayOneShot(_chop); break;
                case CookEventKind.Turn:
                case CookEventKind.Flip:
                case CookEventKind.Baste: _source.PlayOneShot(_sizzle); break;
                case CookEventKind.Stir: _source.PlayOneShot(_slosh); break;
                case CookEventKind.Skim: _source.PlayOneShot(_skim); break;
                case CookEventKind.Add: _source.PlayOneShot(_plop); break;
                case CookEventKind.Catching:
                case CookEventKind.Charring:
                case CookEventKind.Ruined: _source.PlayOneShot(_crackle); break;
                case CookEventKind.Carve: _source.PlayOneShot(_carve); break;
                case CookEventKind.TakeOff: _source.PlayOneShot(_clink); break;
                case CookEventKind.HeatChanged: _source.PlayOneShot(_whoosh, 0.6f); break;
                case CookEventKind.Nothing: _source.PlayOneShot(_thud, 0.5f); break;
            }
        }
    }
}
