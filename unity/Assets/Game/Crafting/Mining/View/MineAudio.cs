using UnityEngine;

namespace Mmo.Crafting.Mining.View
{
    /// <summary>Placeholder sounds generated in code, so the prototype needs no audio files.</summary>
    public sealed class MineAudio : MonoBehaviour
    {
        private const int Rate = 44100;
        private AudioSource _source;
        private AudioClip _creak, _split, _crunch, _settle, _chalk;

        private void Awake()
        {
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;

            var noise = new System.Random(11);
            float N() => (float)noise.NextDouble() * 2 - 1;

            // A low groan with a fine trickle of grit: the rock settling and a crack opening.
            _creak = Make("creak", 0.45f, t =>
                Mathf.Sin(2 * Mathf.PI * (70 + 40 * t) * t) * Mathf.Sin(Mathf.PI * t / 0.45f) * 0.22f +
                N() * (noise.NextDouble() < 0.02 ? 0.25f : 0.01f));
            // Pick bites, then the chunk lets go and tumbles.
            _split = Make("split", 0.7f, t =>
                N() * Mathf.Exp(-t * 40) * 0.7f +
                Mathf.Sin(2 * Mathf.PI * 1900 * t) * Mathf.Exp(-t * 50) * 0.25f +
                Mathf.Sin(2 * Mathf.PI * 85 * t) * Mathf.Exp(-t * 6) * 0.35f * Mathf.Clamp01((t - 0.05f) * 30));
            // A dull blow and a long grind of crushed rock.
            _crunch = Make("crunch", 0.55f, t =>
                Mathf.Sin(2 * Mathf.PI * 120 * t) * Mathf.Exp(-t * 20) * 0.45f +
                N() * Mathf.Exp(-t * 5) * 0.3f * (0.6f + 0.4f * Mathf.Sin(2 * Mathf.PI * 30 * t)));
            // Grit settling as the crack closes.
            _settle = Make("settle", 0.35f, t => N() * Mathf.Sin(Mathf.PI * t / 0.35f) * 0.06f);
            _chalk = Make("chalk", 0.12f, t => N() * Mathf.Sin(Mathf.PI * t / 0.12f) * 0.12f);
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
                case MineEventKind.Marked: _source.PlayOneShot(_chalk); break;
                case MineEventKind.Opened: _source.PlayOneShot(_creak); break;
                case MineEventKind.Closed: _source.PlayOneShot(_settle); break;
                case MineEventKind.Clean: _source.PlayOneShot(_split); break;
                case MineEventKind.Fracture: _source.PlayOneShot(_crunch); break;
            }
        }
    }
}
