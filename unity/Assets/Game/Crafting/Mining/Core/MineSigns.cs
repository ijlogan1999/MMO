using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Mmo.Crafting.Mining
{
    /// <summary>Which surface sign marks which ore in a world. Fixed for the world's life, different between worlds.</summary>
    public static class SignTable
    {
        public static readonly Sign[] AllSigns = (Sign[])Enum.GetValues(typeof(Sign));

        public static Sign TrueSign(int worldSeed, OreKind ore)
        {
            var rng = new Random(worldSeed * 7919 + 17);
            var pool = new List<Sign>(AllSigns);
            var order = new[] { OreKind.Iron, OreKind.Coal, OreKind.Copper };
            foreach (var kind in order)
            {
                int i = rng.Next(pool.Count);
                if (kind == ore) return pool[i];
                pool.RemoveAt(i);
            }
            return pool[0];
        }

        public static string Describe(Sign s) => s switch
        {
            Sign.RustStreaks => "rust-red streaks",
            Sign.GreenStain => "green-blue staining",
            Sign.BlackBands => "thin black bands",
            Sign.QuartzVeins => "white quartz veins",
            Sign.YellowLichen => "yellow lichen",
            Sign.GreyLichen => "grey-green lichen",
            Sign.CubeCrystals => "tiny cube crystals",
            _ => "fine needle crystals",
        };
    }

    /// <summary>
    /// The miner's journal: for each ore and sign, how often that sign turned out to sit on ore versus barren rock.
    /// Filled in after every dig, when the face can be seen for what it was. Saved as a short string.
    /// </summary>
    public sealed class MinersJournal
    {
        private readonly Dictionary<(OreKind, Sign), (int Ore, int Rock)> _seen = new Dictionary<(OreKind, Sign), (int, int)>();

        public (int Ore, int Rock) Seen(OreKind ore, Sign sign) =>
            _seen.TryGetValue((ore, sign), out var v) ? v : (0, 0);

        public void Record(OreKind ore, Sign sign, bool onOre)
        {
            var v = Seen(ore, sign);
            _seen[(ore, sign)] = onOre ? (v.Ore + 1, v.Rock) : (v.Ore, v.Rock + 1);
        }

        /// <summary>Records every sign on a dug face against what each block really was.</summary>
        public void RecordFace(OreKind ore, IEnumerable<Block> blocks)
        {
            foreach (var b in blocks)
                foreach (var s in b.Signs)
                    Record(ore, s, b.Kind == BlockKind.Ore);
        }

        public bool IsEmpty(OreKind ore)
        {
            foreach (var k in _seen.Keys) if (k.Item1 == ore) return false;
            return true;
        }

        public string Serialize()
        {
            var sb = new StringBuilder();
            foreach (var kv in _seen)
                sb.Append(kv.Key.Item1).Append('.').Append(kv.Key.Item2).Append('=')
                  .Append(kv.Value.Ore.ToString(CultureInfo.InvariantCulture)).Append('/')
                  .Append(kv.Value.Rock.ToString(CultureInfo.InvariantCulture)).Append(';');
            return sb.ToString();
        }

        public static MinersJournal Parse(string text)
        {
            var j = new MinersJournal();
            if (string.IsNullOrEmpty(text)) return j;
            foreach (var entry in text.Split(';'))
            {
                var eq = entry.Split('=');
                if (eq.Length != 2) continue;
                var key = eq[0].Split('.');
                var val = eq[1].Split('/');
                if (key.Length != 2 || val.Length != 2) continue;
                if (!Enum.TryParse(key[0], out OreKind ore) || !Enum.TryParse(key[1], out Sign sign)) continue;
                if (!int.TryParse(val[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int o)) continue;
                if (!int.TryParse(val[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int r)) continue;
                j._seen[(ore, sign)] = (o, r);
            }
            return j;
        }
    }
}
