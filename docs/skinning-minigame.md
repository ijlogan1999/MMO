# Skinning minigame (prototype)

Open `unity/Assets/Game/Scenes/Skinning.unity` and press Play.

Hunting itself is ordinary combat against animals. This minigame is what comes after: the animal is down, and the hide, meat and the rest are yours if you take them well. It is turn-based. Nothing runs on a clock, and there is no aiming or timing; time passes only as you work (a pull, a cut, honing, gutting).

The first time, an old hunter explains the craft once (the setup screen can replay it). After that there is no help beyond a keys bar: no gauges, markers or hints. You read the animal: the steam coming off it, the belly starting to swell, what your fingers tell you, and the flesh side of the hide on the beam. The results screen explains what happened and shows how the hide really held.

## A session

1. **Open.** Slit the belly and the inside of the legs (*open*, the hide comes off flat) or cut only across the hind legs and peel it forward like a sleeve (*cased*, the pelt stays a whole tube). A furrier pays more for a cased pelt, but anything big tears if you try to case it.
2. **Skin.** Work the hide off patch by patch, from an open edge (a cut, or hide already off).
   - **Feel** a patch (free, but you only have so many): it slides, clings or is held tight, and you feel fat under it or a wound hidden in the fur.
   - **Pull** loose hide off by hand. Pull clinging hide and it stretches (thin skin tears); pull tight hide, fat, or a wound and it tears.
   - **Knife** frees clinging, tight and fatty hide clean and cuts neatly around wounds, but scores loose hide (it flaps against the edge), costs twice the time, and dulls. **Hone** when the knife starts to drag; a dull blade scores.
   - Working anywhere without an open edge tears or cuts blind.
   - The body cools as you work. Cooling makes every patch hold one step harder, and once it sets, cut patches bring meat with them.
   - **Gut** before the belly sours (it swells first). Big bodies keep their heat and sour fastest. Gutting a cased animal before the skin is off means slitting the belly, which spoils the tube.
3. **Flesh.** On the beam, flesh side up: scrape off film and fat, trim meat (and fat) with the knife, and leave clean hide alone. Scraping bare hide thins it, and where the skin is thin (it glows against the light) you go straight through. The knife on film bites the hide.
4. **Keep.** Salt it (best for leather hides; stiffens fur), stretch and dry it (best for pelts; flies and hair slip in summer; leather hides dry flint-hard), or roll it green for the tanner (only fine in real cold).

| Key | Does |
|---|---|
| 1 / 2 | Open or cased (opening) |
| 1 / 2 / 3 | Feel, pull, knife (skinning); scrape, trim (beam); salt, stretch, green (keeping) |
| MOUSE / ARROWS | Choose a patch |
| CLICK / SPACE | Use the tool on it |
| H | Hone the knife |
| G | Gut |
| E | Stop skinning (whatever is still on stays on) |
| ENTER | Done on the beam |
| ESC | Leave |

## Animals

From the spec's hunting table. The fantasy beast comes later.

| Animal | Skill | Gives | How the hide holds (warm) |
|---|---|---|---|
| Hare | 1 | Hare Skin (½ hide), 1 hare meat | loose all over, legs and tail cling; thin everywhere |
| Deer | 1 | Deer Hide, 4 venison, 2 bone, 1 sinew | loose ribs, belly and haunches; neck, shoulders, chest and tail cling; legs tight; thin belly and legs |
| Boar | 20 | Tough Hide, 4 pork, 2 fat, 1 tusk | shoulders and neck tight (the gristle shield); fat under ribs, belly and haunches |
| Wolf | 20 | Wolf Pelt, 1 bone, 2 fang (no meat) | clings over most of it, belly loose; neck, legs and tail tight; thin belly |
| Bear | 40 | Heavy Pelt (2 hides), 6 bear meat, 3 fat | fat over most of the body; neck and legs tight |

Each animal varies a little (one patch in five holds a step looser or tighter). Hare and wolf want casing; deer, boar and bear must be opened.

## Rules

- **Hide quality** `= grade × (0.4 + 0.6 × skinning × beam) × style × keeping × season`, capped by the lower of the knife and skill (skill cap 31 at skill 1, 100 at skill 100, same as mining).
  - skinning = mean patch value over the whole hide: clean 1.0, stretched 0.8, cut round a wound 0.65, scored 0.6, torn 0.15, left on 0. Less than half off = hide scraps.
  - beam = mean over the patches taken off: clean 1.0, film 0.85, fat 0.6, meat 0.5, × thinned 0.8 / cut 0.7 / holed 0.25.
  - style: the right way 1.0; a fur-bearer opened (or slit to gut) 0.88; a big animal cased 0.7.
  - season: winter pelts ×1.1 (spec: thick pelts, about 10%).
- **Meat quality** `= grade × gutting × season`, capped by skill. Gutting loses 7% per unit of work past the spoil point (floor 35%). Autumn ×1.1, summer ×0.9.
- **Better tools raise quality, not yield.** Knives: worn (starter kit, cap 60, dulls fast), iron (cap 85), steel (no cap, holds its edge). Yield is fixed per animal.
- **Skill** gives more feels (5 to 11), spots more wounds at a glance (45% to 90%), notices the knife dragging and the belly swelling earlier, and raises the cap.
- **Inventory names** follow the spec: season adjective only when it changed the material, then the grade word (Torn 1-19, Rough 20-39, Common 40-59, Fine 60-79, Superb 80-100), e.g. `Winter Wolf Pelt · Fine (68)`.
- **Economy fit:** hides go to tanning (2 hides + 1 bark → leather). A hare skin counts half, a bear pelt double. Meat goes to cooks; fat, bone, sinew, tusk and fang are listed for later recipes.

## Code layout

```
unity/Assets/Game/Crafting/Skinning/
  Core/   rules only, no UnityEngine (asmdef with noEngineReferences) -> reusable on the C# server
  View/   SkinView (placeholder visuals drawn with IMGUI), SkinAudio (sounds generated in code)
unity/Assets/Game/Scenes/Skinning.unity
tests/Skinning.Tests/   dotnet test tests/Skinning.Tests
```
