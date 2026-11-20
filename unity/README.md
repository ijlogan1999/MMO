# Unity client

Unity project for the game client. Built with the built-in renderer and no extra packages so far.

- **Editor:** Unity 6.3 LTS, pinned to `6000.3.29f1`.
- **First open:** Unity fills in the rest of `ProjectSettings/` and writes `Packages/packages-lock.json`. Commit those files after the first open.
- **Git LFS:** run `git lfs install` once per machine before adding art or audio. `unity/.gitattributes` sends images, models, audio, video and fonts to LFS.

## Blacksmithing minigame (first playable)

Open `Assets/Game/Scenes/Blacksmithing.unity` and press Play.

The forge gives no on-screen help: no heat gauge, no markers, no timing zones, no quench timer. You read the colour of the metal, listen to the beat, and judge the blade against the ghost outline. The results screen explains what happened afterwards.

| Step | What you do | Keys |
|---|---|---|
| Heat | Pull the bar when its colour looks right for the recipe. Too cold cracks it, too hot burns it. | SPACE (or click the forge) |
| Strike | A beat plays. On any beat, strike the part that needs work. Off the beat, or twice on one beat, dents the blade; overworking a part ruins its shape. | A tip, S body, D edge (or J K L, or click the part) |
| Done shaping | Set the hammer down when the blade matches the outline, or play out the beats. | E |
| Cooling | Cold metal stops moving. Reheat; reheats after the second cost quality. | R |
| Quench (Journeyman) | Pick water (hardest, risky), brine, or oil (safe), then hold for your own count. | 1 2 3, then hold SPACE |
| Finish | Three sharpening strokes; stroke the stone over the dull spot. Good strokes can add bonus traits. | SPACE |

Scoring follows the Crafting & Gathering Spec:

- Score `S = shape × strike accuracy × heat control`
- Quality `Q = floor + (ceiling − floor) × clamp(S ± 0.1, 0, 1)`, botch below 0.15 (Q 1–10)
- Ceiling = lowest of method, station, hammer, and the inputs' weighted quality (longsword: blade 60%, guard + pommel 25%, grip 15%)
- Skill widens the perfect window from ±60 ms (skill 1) to ±140 ms (skill 100) and raises the floor
- Better tools (hammer, station) raise the ceiling

Recipes are the iron path for level 1–20: Plain Iron Sword (Novice), Iron Shortsword (Apprentice), Iron Longsword (Journeyman).

## Code layout

```
Assets/Game/Crafting/Blacksmithing/
  Core/   rules only, no UnityEngine (asmdef with noEngineReferences) -> reusable on the C# server
  View/   ForgeView (placeholder visuals drawn with IMGUI), ForgeAudio (sounds generated in code)
Assets/Game/Scenes/Blacksmithing.unity
```

The Core rules are tested outside Unity: `dotnet test tests/Blacksmithing.Tests` (needs the .NET 8 SDK).
