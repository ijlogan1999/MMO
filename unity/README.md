# Unity client

Unity project for the game client. Built with the built-in renderer and no extra packages so far.

- **Editor:** Unity 6.3 LTS (`6000.3.x`). Any 6000.3 patch works; if Unity Hub asks, pick the 6000.3 version you have installed.
- **First open:** Unity fills in the rest of `ProjectSettings/` and writes `Packages/packages-lock.json`. Commit those files after the first open.
- **Git LFS:** run `git lfs install` once per machine before adding art or audio. `unity/.gitattributes` sends images, models, audio, video and fonts to LFS.

## Blacksmithing minigame (first playable)

Open `Assets/Game/Scenes/Blacksmithing.unity` and press Play.

| Step | What you do | Keys |
|---|---|---|
| Heat | Pull the bar when it reaches the target colour. Too cold cracks it, too hot burns it. | SPACE (or click the forge) |
| Strike | Markers slide to the hammer line. Hit the right part of the blade on the beat. | A tip, S body, D edge (or J K L, or click the lane) |
| Cooling | Below cherry red, strikes stop counting. Reheat; the first two are free. | R |
| Quench (Journeyman) | Pick water (hardest, risky), brine, or oil (safe). Hold, release in the green zone. | 1 2 3, then hold SPACE |
| Finish | Sharpen three strokes; good strokes can add bonus traits. | SPACE |

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
