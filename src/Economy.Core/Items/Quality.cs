using System;

namespace Economy.Core.Items
{
    /// <summary>
    /// Quality is a whole number from 1 to 100 on every unit. Markets group it
    /// into five bands so order books stay manageable.
    /// </summary>
    public enum QualityBand
    {
        Poor,
        Fair,
        Good,
        Fine,
        Masterwork,
    }

    public static class Quality
    {
        public const int Min = 1;
        public const int Max = 100;

        public static int Clamp(int quality) => Math.Max(Min, Math.Min(Max, quality));

        public static int Clamp(double quality) => Clamp((int)Math.Round(quality, MidpointRounding.AwayFromZero));

        /// <summary>1-20 poor, 21-40 fair, 41-60 good, 61-80 fine, 81-100 masterwork.</summary>
        public static QualityBand BandOf(int quality)
        {
            int q = Clamp(quality);
            if (q <= 20) return QualityBand.Poor;
            if (q <= 40) return QualityBand.Fair;
            if (q <= 60) return QualityBand.Good;
            if (q <= 80) return QualityBand.Fine;
            return QualityBand.Masterwork;
        }

        /// <summary>The lowest quality that falls in a band.</summary>
        public static int BandFloor(QualityBand band) => band switch
        {
            QualityBand.Poor => 1,
            QualityBand.Fair => 21,
            QualityBand.Good => 41,
            QualityBand.Fine => 61,
            QualityBand.Masterwork => 81,
            _ => throw new ArgumentOutOfRangeException(nameof(band)),
        };
    }
}
