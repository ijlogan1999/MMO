// Pure C# (no UnityEngine) so the same rules can run on the game server and in tests.
using System.Collections.Generic;

namespace Mmo.Crafting.Skinning
{
    /// <summary>
    /// Open: choose how to skin. Skin: gut, and work the hide off the carcass. Flesh: clean the hide on the beam.
    /// Preserve: salt, stretch or roll it. Done: results.
    /// </summary>
    public enum SkinStage { Open, Skin, Flesh, Preserve, Done }

    /// <summary>Skill ranks from the crafting spec (skill 1-100).</summary>
    public enum SkillRank { Novice, Apprentice, Journeyman, Expert, Master }

    public enum Season { Spring, Summer, Autumn, Winter }

    /// <summary>
    /// Open: slit the belly from throat to tail and down the inside of each leg, so the hide comes off flat.
    /// Cased: cut only across the hind legs and peel the skin forward like a sleeve, so the pelt stays a whole tube.
    /// </summary>
    public enum SkinStyle { Open, Cased }

    /// <summary>How firmly the hide holds to the body. Cooling makes every patch hold harder.</summary>
    public enum Hold { Loose, Clinging, Tight }

    /// <summary>Where on the body a patch lies. Each animal holds its hide differently in each region.</summary>
    public enum Region { Neck, Shoulder, Chest, Ribs, Belly, Haunch, Foreleg, Hindleg, Tail }

    /// <summary>Skin stage tools. Feeling costs no time; the others do.</summary>
    public enum SkinTool { Feel, Pull, Knife }

    /// <summary>Fleshing tools on the beam.</summary>
    public enum FleshTool { Scrape, Trim }

    /// <summary>How a patch came off the carcass (or didn't).</summary>
    public enum PatchState
    {
        OnBody,
        Clean,      // came away whole
        Stretched,  // pulled while it clung: off, but stretched thin
        Scored,     // a knife cut into the hide
        Holed,      // a wound, cut around neatly: a small hole
        Torn,       // ripped
        Left,       // never taken off (the skinner stopped)
    }

    /// <summary>What sits on the flesh side of a patch once it is off. Fleshing works on this.</summary>
    public enum Layer { Clean, Membrane, Fat, Meat }

    /// <summary>Extra damage done on the beam.</summary>
    public enum FleshMark { None, Thinned, Scored, Torn }

    public enum Preserve { None, Salt, Stretch, Green }

    public enum SkinEventKind
    {
        StyleChosen,
        Felt,
        CameAway,   // clean
        Stretched,
        Scored,
        Holed,
        Tore,
        Honed,
        Drag,       // the knife has started to drag (the skinner noticed it is dulling)
        Gutted,
        Swelling,   // the belly has started to swell (the skinner noticed)
        Scraped,
        Trimmed,
        Skid,       // a wasted stroke on the beam
        StageChanged,
        Done,
    }

    public struct SkinEvent
    {
        public SkinEventKind Kind;
        public string Message;
        public int PatchId;

        public SkinEvent(SkinEventKind kind, string message, int patchId = -1)
        {
            Kind = kind;
            Message = message;
            PatchId = patchId;
        }

        public override string ToString() => $"{Kind}: {Message}";
    }

    /// <summary>
    /// One patch of the hide. The hide is laid out the way it looks stretched flat: a grid with the neck at the top,
    /// legs at the corners, the belly line down the middle column and the tail at the bottom.
    /// </summary>
    public sealed class Patch
    {
        public int Id { get; }
        public int Row { get; }
        public int Col { get; }
        public Region Region { get; }
        /// <summary>How it held while the body was warm. Cooling adds to this (see SkinMinigame.HoldNow).</summary>
        public Hold BaseHold { get; }
        /// <summary>A layer of fat between hide and muscle: pulling rips the hide, only the knife frees it.</summary>
        public bool Fat { get; }
        /// <summary>Thin skin: scraping it bare on the beam goes straight through.</summary>
        public bool Thin { get; }
        /// <summary>A wound from the fight.</summary>
        public bool Wound { get; }
        /// <summary>The wound shows through the fur. Hidden wounds are found by feeling, or by tearing into them.</summary>
        public bool WoundSeen { get; internal set; }

        public PatchState State { get; internal set; } = PatchState.OnBody;
        public Layer Layer { get; internal set; } = Layer.Membrane;
        public FleshMark Mark { get; internal set; } = FleshMark.None;
        /// <summary>What the skinner's fingers last told them about the hold, if they felt it.</summary>
        public Hold? Felt { get; internal set; }
        public bool FeltFat { get; internal set; }

        public bool IsOff => State != PatchState.OnBody && State != PatchState.Left;
        public bool OnMidline => Col == 2 && Row <= 4;

        public Patch(int id, int row, int col, Region region, Hold baseHold, bool fat, bool thin, bool wound, bool woundSeen)
        {
            Id = id;
            Row = row;
            Col = col;
            Region = region;
            BaseHold = baseHold;
            Fat = fat;
            Thin = thin;
            Wound = wound;
            WoundSeen = woundSeen;
        }
    }

    /// <summary>A kind of game animal and how its hide holds.</summary>
    public sealed class Species
    {
        public string Id { get; }
        public string Name { get; }
        public string HideName { get; }
        public string MeatName { get; }
        /// <summary>How many hides this counts as at the tannery (2 hides + 1 bark make leather).</summary>
        public double HideUnits { get; }
        public int Meat { get; }
        /// <summary>Other parts taken while butchering, fixed per animal (not part of the minigame).</summary>
        public string Extras { get; }
        public int RequiredSkill { get; }
        /// <summary>A furrier pays for a whole tube: these are worth casing.</summary>
        public bool FurBearer { get; }
        /// <summary>Small enough to peel off as a sleeve. Casing anything bigger tears it.</summary>
        public bool CanCase { get; }
        /// <summary>The finished hide is a pelt (stretched and dried) rather than a hide for leather (salted).</summary>
        public bool Pelt { get; }
        /// <summary>Time (in work) an ungutted body holds before the belly sours, in a mild season.</summary>
        public int SpoilAfter { get; }
        /// <summary>How fast the body loses heat: small bodies cool fast, big ones hold it.</summary>
        public double CoolRate { get; }
        public string Description { get; }

        private readonly Dictionary<Region, Hold> _hold;
        private readonly HashSet<Region> _fat, _thin;

        public Species(string id, string name, string hideName, string meatName, double hideUnits, int meat, string extras,
            int requiredSkill, bool furBearer, bool canCase, bool pelt, int spoilAfter, double coolRate,
            Dictionary<Region, Hold> hold, Region[] fat, Region[] thin, string description)
        {
            Id = id;
            Name = name;
            HideName = hideName;
            MeatName = meatName;
            HideUnits = hideUnits;
            Meat = meat;
            Extras = extras;
            RequiredSkill = requiredSkill;
            FurBearer = furBearer;
            CanCase = canCase;
            Pelt = pelt;
            SpoilAfter = spoilAfter;
            CoolRate = coolRate;
            _hold = hold;
            _fat = new HashSet<Region>(fat);
            _thin = new HashSet<Region>(thin);
            Description = description;
        }

        public Hold HoldOf(Region r) => _hold.TryGetValue(r, out var h) ? h : Hold.Loose;
        public bool FatOn(Region r) => _fat.Contains(r);
        public bool ThinOn(Region r) => _thin.Contains(r);

        private static Dictionary<Region, Hold> H(Hold neck, Hold shoulder, Hold chest, Hold ribs, Hold belly,
            Hold haunch, Hold foreleg, Hold hindleg, Hold tail) => new Dictionary<Region, Hold>
        {
            [Region.Neck] = neck, [Region.Shoulder] = shoulder, [Region.Chest] = chest, [Region.Ribs] = ribs,
            [Region.Belly] = belly, [Region.Haunch] = haunch, [Region.Foreleg] = foreleg, [Region.Hindleg] = hindleg,
            [Region.Tail] = tail,
        };

        private const Hold L = Hold.Loose, C = Hold.Clinging, T = Hold.Tight;

        // Holds follow real skinning: deer peel easily when warm except neck, shoulders and legs; a boar's shoulders
        // carry a tough gristle shield and fat everywhere else; a hare's skin slips off like paper but tears easily;
        // wolves cling all over with tight legs and neck; a bear is wrapped in fat.
        public static readonly Species Hare = new Species(
            "hare", "Hare", "Hare Skin", "Hare Meat", 0.5, 1, "",
            1, furBearer: true, canCase: true, pelt: true, spoilAfter: 14, coolRate: 0.07,
            H(L, L, L, L, L, L, C, C, C), new Region[0],
            new[] { Region.Neck, Region.Shoulder, Region.Chest, Region.Ribs, Region.Belly, Region.Haunch, Region.Foreleg, Region.Hindleg, Region.Tail },
            "Meadow game. Small meat and a small, thin skin.");

        public static readonly Species Deer = new Species(
            "deer", "Deer", "Deer Hide", "Venison", 1, 4, "2 bone, 1 sinew",
            1, furBearer: false, canCase: false, pelt: false, spoilAfter: 7, coolRate: 0.035,
            H(C, C, C, L, L, L, T, T, C), new Region[0], new[] { Region.Belly, Region.Foreleg, Region.Hindleg },
            "Forest-edge game. Venison, hide, bone and sinew.");

        public static readonly Species Wolf = new Species(
            "wolf", "Wolf", "Wolf Pelt", "", 1, 0, "1 bone, 2 fang",
            20, furBearer: true, canCase: true, pelt: true, spoilAfter: 10, coolRate: 0.045,
            H(T, C, C, C, L, C, T, T, T), new Region[0], new[] { Region.Belly },
            "Hill packs. Pelt, bone and fangs; nobody eats wolf.");

        public static readonly Species Boar = new Species(
            "boar", "Boar", "Tough Hide", "Pork", 1, 4, "2 fat, 1 tusk",
            20, furBearer: false, canCase: false, pelt: false, spoilAfter: 6, coolRate: 0.03,
            H(T, T, C, C, L, C, T, T, C), new[] { Region.Ribs, Region.Belly, Region.Haunch }, new[] { Region.Belly },
            "Woods and thickets. Pork, fat, a tough hide and a tusk.");

        public static readonly Species Bear = new Species(
            "bear", "Bear", "Heavy Pelt", "Bear Meat", 2, 6, "3 fat",
            40, furBearer: false, canCase: false, pelt: true, spoilAfter: 5, coolRate: 0.022,
            H(T, C, C, C, C, C, T, T, C),
            new[] { Region.Shoulder, Region.Chest, Region.Ribs, Region.Belly, Region.Haunch }, new[] { Region.Belly },
            "Deep forest. Large meat, fat and a heavy pelt.");

        public static readonly Species[] All = { Hare, Deer, Boar, Wolf, Bear };
    }

    /// <summary>The skinning knife. Better knives raise the quality cap and hold an edge longer; they never add yield.</summary>
    public sealed class Knife
    {
        public string Name { get; }
        public int QualityCap { get; }
        /// <summary>Edge lost per stroke (doubled through fat).</summary>
        public double DullPerStroke { get; }
        public string Description { get; }

        public Knife(string name, int qualityCap, double dullPerStroke, string description)
        {
            Name = name;
            QualityCap = qualityCap;
            DullPerStroke = dullPerStroke;
            Description = description;
        }

        public static readonly Knife WornKnife = new Knife("Worn knife", 60, 0.22, "Starter kit. Caps hide quality at 60 and dulls fast.");
        public static readonly Knife IronKnife = new Knife("Iron skinning knife", 85, 0.14, "Smith-made. Caps hide quality at 85.");
        public static readonly Knife SteelKnife = new Knife("Steel skinning knife", 100, 0.09, "Master-made. No cap; holds its edge.");

        public static readonly Knife[] All = { WornKnife, IronKnife, SteelKnife };
    }

    /// <summary>What the skinner brings to the carcass, and the carcass itself.</summary>
    public sealed class SkinSetup
    {
        public Species Species = Species.Deer;
        public int SkillLevel = 1;
        public Knife Knife = Knife.IronKnife;
        /// <summary>Hidden animal grade 1..100 (age and health): the base of hide and meat quality.</summary>
        public int AnimalGrade = 60;
        public Season Season = Season.Autumn;
        /// <summary>Wounds the fight left in the hide. A clean kill leaves one.</summary>
        public int Wounds = 2;

        public SkinSetup() { }

        public SkinSetup(Species species) { Species = species; }
    }
}
