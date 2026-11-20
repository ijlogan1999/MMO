namespace Economy.Core.Currency
{
    /// <summary>
    /// Gold, silver and copper (Ian, 2026-11-19): 100 copper = 1 silver,
    /// 100 silver = 1 gold. The ledger counts copper; this only formats it.
    /// </summary>
    public static class Money
    {
        public const long CopperPerSilver = 100;
        public const long CopperPerGold = 100 * CopperPerSilver;

        public static string Format(long copper)
        {
            string sign = copper < 0 ? "-" : "";
            long c = System.Math.Abs(copper);
            long g = c / CopperPerGold, s = c % CopperPerGold / CopperPerSilver, cu = c % CopperPerSilver;
            if (g > 0) return $"{sign}{g}g {s}s {cu}c";
            if (s > 0) return $"{sign}{s}s {cu}c";
            return $"{sign}{cu}c";
        }

        public static string Format(double copper) => Format((long)System.Math.Round(copper));
    }
}
