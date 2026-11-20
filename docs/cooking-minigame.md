# Cooking minigame (prototype)

Open `unity/Assets/Game/Scenes/Cooking.unity` and press Play.

The first time, an old cook explains the hearth once (the setup screen can replay it). After that the hearth gives no help beyond a keys bar: no doneness bars, prompts, markers or timers. You read the food: its colour, the smoke, how hard the pot boils, the scum on top, the sheen on a roast. The results screen explains each dish afterwards.

## A session

1. **Season.** Pick a wild herb for each dish (or none). Which herb suits which food is rolled per world; one suits it, one clashes, the rest do little.
2. **Chop** (only if a pot dish is on). Chop in your own steady rhythm. Each gap between chops becomes a piece, so an uneven rhythm shows as uneven pieces and cooks unevenly.
3. **Cook.** Everything goes on the fire at once and cooks whether you watch it or not. Juggle them, then take each one off when it looks done. A roast rests on the board until you carve it.

| Key | Does |
|---|---|
| 1-5 (or click) | Pick a dish |
| W / S | Closer to the coals (hotter) / further away (cooler) |
| SPACE | Turn the spit, flip the bread, stir the pot |
| Q | Skim the pot, or baste the roast with its fat |
| A | Add the next ingredient to the pot |
| F | Take it off the fire (roast: to the board, then F again to carve) |
| H | Change the herb (seasoning only) |
| ESC | Leave |

## Dishes

From the spec's cooking table, Novice to Journeyman. Pie and the banquet need an oven, so they come later.

| Dish | Rank | Station | What matters |
|---|---|---|---|
| Meat on a Stick | Novice | spit | turning, taking it off at the right colour |
| Hearth Flatbread | Novice | griddle stone | only the bottom cooks: peek at the rim and flip |
| Spit Roast | Apprentice | spit | turning, basting (3 bastes of fat), resting before carving |
| Bone Broth | Journeyman | pot | stirring, skimming, adding the vegetables late enough |
| Hunter's Stew | Journeyman | pot | broth, then meat, then vegetables, each in its own time |

Flatbread is an addition to the spec's table, because the economy treats bread as an everyday food.

## Rules

- **Stations by skill:** 2 (Novice), 3 (Apprentice and Journeyman), 4 (Expert), 5 (Master). Skill also widens the done band from ±6% to ±12%.
- **Doneness:** spits and griddles have two sides; the side facing the heat cooks fully, the other side barely (griddle: almost not at all). Each side, and each thing in a pot, is scored against this world's done point. Raw is bad, burnt is bad. Past 2.2× done it is ruined and comes off by itself.
- **Pots** catch on the bottom if not stirred (after about 7 s at medium, 3.5 s at high; never at low). Scum rises early in the simmer. A fully burnt pot is ruined.
- **Hearth:** a campfire's heat wanders up to ±25% (you see the flames flare and sink), a town kitchen ±10%, a shop kitchen holds steady.
- **Cookware:** the starter kit has no pot, so broth and stew need smith-made cookware.
- **Score** per dish: `S = doneness × handling × prep`
  - handling = pot: (1 − scorch) × (1 − 0.4 × scum left); roast: (0.6 + 0.4 × moisture) × rest
  - prep = herb (suits 1.0, nothing or neutral 0.9, clashes 0.72) × chop (0.7 + 0.3 × evenness) for vegetable dishes
- **Quality** per dish: `Q = floor + (ceiling − floor) × clamp(S ± 0.1, 0, 1)`, botch below 0.15. Ceiling is the lowest of method, hearth, cookware and the ingredients' weighted quality. Same formula as the forge.
- **Rolled per world** (spec, "Rolled per world"): each dish's done point (±10%), the roast's best rest (1 to 5 counts) and the herb pairings. Fixed everywhere: salt and resting help. The setup screen has World 1-3 to try different rolls.

## Code layout

```
unity/Assets/Game/Crafting/Cooking/
  Core/   rules only, no UnityEngine (asmdef with noEngineReferences) -> reusable on the C# server
  View/   CookView (placeholder visuals drawn with IMGUI), CookAudio (sounds generated in code)
unity/Assets/Game/Scenes/Cooking.unity
tests/Cooking.Tests/
```

Run the rules tests with `dotnet test tests/Cooking.Tests` (needs the .NET 8 SDK).
