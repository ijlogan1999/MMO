// Pure C# (no UnityEngine) so the same rules can run on the game server and in tests.
namespace Mmo.Crafting.Cooking
{
    /// <summary>What a dish cooks on. Spits and griddles have two sides; pots hold a liquid and its ingredients.</summary>
    public enum StationKind { Spit, Griddle, Pot }

    public enum CookStage { Season, Chop, Cook, Done }

    /// <summary>How close the dish sits to the coals.</summary>
    public enum Heat { Low = 0, Medium = 1, High = 2 }

    public enum DishState { Cooking, Resting, Served }

    /// <summary>Skill ranks from the crafting spec (skill 1-100).</summary>
    public enum SkillRank { Novice, Apprentice, Journeyman, Expert, Master }

    /// <summary>Wild herbs from the herb pool. Which one suits which food is rolled per world.</summary>
    public enum Herb { None, Thyme, WildGarlic, Juniper, Sage }

    /// <summary>What a dish tastes of, for herb pairing.</summary>
    public enum FlavorBase { GameMeat, Bones, Bread }

    /// <summary>Raw materials, each with its own quality slider in the prototype.</summary>
    public enum Ingredient { Meat, LargeCut, Fat, Flour, Salt, Bones, Vegetables, Broth }

    public enum CookEventKind
    {
        StageChanged,
        Selected,
        Chop,
        Turn,
        Flip,
        Stir,
        Skim,
        Baste,
        Add,
        HeatChanged,
        TakeOff,
        Carve,
        Catching,   // a pot starts to stick to the bottom
        Charring,   // a side or the pot's contents start to blacken
        Ruined,     // burnt past saving and pulled off by itself
        Nothing,    // the key did nothing for this dish (dull thud, no message)
        Done,
    }

    public struct CookEvent
    {
        public CookEventKind Kind;
        public int Slot;
        public string Message;

        public CookEvent(CookEventKind kind, int slot, string message)
        {
            Kind = kind;
            Slot = slot;
            Message = message;
        }

        public override string ToString() => $"{Kind}[{Slot}]: {Message}";
    }

    /// <summary>Where the cook works. Better hearths hold a steadier heat and raise the quality ceiling.</summary>
    public sealed class Hearth
    {
        public string Name { get; }
        public int QualityCap { get; }
        /// <summary>How far the fire wanders from the set heat (0.25 = up to 25% hotter or cooler).</summary>
        public double Wobble { get; }
        public string Description { get; }

        public Hearth(string name, int qualityCap, double wobble, string description)
        {
            Name = name;
            QualityCap = qualityCap;
            Wobble = wobble;
            Description = description;
        }

        public static readonly Hearth Campfire = new Hearth("Campfire", 50, 0.25, "Anywhere. The fire wanders, caps quality at 50.");
        public static readonly Hearth TownKitchen = new Hearth("Town kitchen", 75, 0.10, "Public hearth in town. Steadier, caps quality at 75.");
        public static readonly Hearth ShopKitchen = new Hearth("Shop kitchen", 100, 0.0, "Owned kitchen. Even heat, no cap.");

        public static readonly Hearth[] All = { Campfire, TownKitchen, ShopKitchen };
    }

    /// <summary>The cook's kit. Pots come from smiths: without one, broth and stew can't be made.</summary>
    public sealed class Cookware
    {
        public string Name { get; }
        public int QualityCap { get; }
        public bool HasPot { get; }
        public string Description { get; }

        public Cookware(string name, int qualityCap, bool hasPot, string description)
        {
            Name = name;
            QualityCap = qualityCap;
            HasPot = hasPot;
            Description = description;
        }

        public static readonly Cookware StarterKit = new Cookware("Stick and flat stone", 60, false, "Starter kit. No pot. Caps quality at 60.");
        public static readonly Cookware IronCookware = new Cookware("Iron pot and spit", 85, true, "Smith-made. Caps quality at 85.");
        public static readonly Cookware SteelCookware = new Cookware("Steel cookware", 100, true, "Master-made. No cap.");

        public static readonly Cookware[] All = { StarterKit, IronCookware, SteelCookware };
    }
}
