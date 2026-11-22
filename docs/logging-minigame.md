# Logging minigame (prototype)

Open `unity/Assets/Game/Scenes/Logging.unity` and press Play.

A knowledge game: no timing, no aiming, no gauges. The first time, an old forester explains how trees behave (the setup screen can replay it). After that the woods give no help beyond a keys bar: no landing markers, no depth meters, no "rotten" labels. You read the trees and judge the cuts by eye. The results screen explains what happened and shows the stand as it really was.

One tree per trip, six steps.

## A trip

1. **Survey.** Five trees in a clearing. Click one (or walk the stand with the arrows) to see it from the south and from the east: its lean, which side of its crown is heavy, a crook in the stem, branch stubs low down, and what's on the bark. Bracket fungus (conks), woodpecker holes and a dead top mean rot; moss, ivy and burls mean nothing. Knock the trunk: sound wood rings, heart rot sounds dull, a hollow booms. Knocks are limited (4 at skill 1, 8 at skill 100).
2. **Aim.** Turn your line (8 directions). The stem needs a clear lane as long as the tree is tall. Into a neighbour's crown, or far enough to reach the forest edge, and it hangs up. Across a rock it cracks there. Straight down a steep slope (contour lines and a stream at its foot) it hits hard and cracks. You can sight the height with the axe handle: a chalk X where you reckon the top will land, off by ±15% at skill 1 and ±3% at skill 100. Wind shows as leaves drifting across the clearing.
3. **Notch.** Chop it on the side of your line, a bite at a time. You see the open wedge going into a side view of the trunk and judge its depth against the trunk's width yourself. Right is a quarter to a third of the way in.
4. **Back cut.** Set the saw's height against the notch floor (below, level, a hand above, well above), then saw in from the far side. Stop with a hinge left (about a tenth of the trunk). The tree goes over by itself once its pull beats the hinge; if it won't, drive wedges. A tree pulling back against your line sits down on the saw once you're a quarter through unless a wedge is in by then; a pinched saw costs double until a wedge lifts it. The hinge creaks when it's close.
5. **Buck.** The stem lies on the ground, butt at the stump. Move the saw along it and cut from the top or from beneath. Where it bridges a dip (ground falls away under it, held on both sides) it sags: cut from beneath, or the cut closes on the saw and tears the end. Where an end hangs out past its last support: cut from the top. On the ground: from the top (from beneath the saw grinds into dirt and nothing is cut). Rot shows on cut ends and on the butt; knock along the stem to find where it stops.
6. **Yard.** For each length: leave it, stack it as timber, stack and peel it (bark), or split it for firewood. The crown is firewood or nothing. Lengths under 4 m can't be timber.

| Key | Survey | Aim | Notch | Back cut | Buck | Yard |
|---|---|---|---|---|---|---|
| Mouse / arrows | look at a tree | turn your line | | height (before sawing) | move the saw | pick a length |
| SPACE | | | chop | saw | | cycle use |
| K / H | K knock | H sight height | | | K knock | |
| Q | | | | drive a wedge | | |
| 1 / 2 | | | | | saw from top / beneath | |
| ENTER | fell this one | start the notch | notch done | | done bucking | done |
| BACKSPACE / E | | back | back / E walk away | E walk away | | |

## Rules

- **Strength** 100 per trip; skill never adds any. Notch bite or saw stroke = diameter / 25 (so 1.2 to 2.8), wedge 2, bucking cut = diameter / 12, stacking 1, peeling +3, splitting 2 per bundle. A hang-up costs 15, a pinched bucking cut 5 more.
- **Pull** = lean (0 / 1 / 2) toward its lean + 0.5 if the crown is heavy on one side + 0.5 per wind step (calm / breeze / strong). Forward pull is its share along your line.
- **Hinge** holds 0.8 per twentieth left. It goes when forward pull + 1 per wedge beats that. 5 wedges at most; a wedge needs the back cut 3/20 in.
- **Steering.** With a good hinge (2 to 3 twentieths), a notch of 5 to 7 twentieths and sound wood, it falls on your line. A thin hinge (1), a shallow or deep notch, or rot in the hinge lets a pull of 1 or more swing it one step (45°) toward the pull. Sawing through the hinge: it goes wherever it pulls.
- **At the stump.** Back cut below the notch: the butt kicks back and splits. Level: 30% chance the butt slides back and tears its end. Well above, or a hinge of 4+: fibre pulled from the butt (that length × 0.8). Shallow notch with forward pull ≥ 2: the trunk splits up from the stump (barber chair, first 3 m cracked).
- **Lengths.** A length a buyer takes as timber is 4 m+, no rot, crook or crack. Its quality = tree grade × (1 − 0.08 × knots per metre) × 0.8 if an end is torn × 0.8 if fibre was pulled.
- **Yield** = sound timber lengths (better axes never change it). Bark per peeled length: pine 1, birch 2, oak 2 (about 1.6 per timber overall, close to the sim's 1.5). Firewood: a bundle per 2 m (rotten metres count half, hollow ones nothing), the crown 2 bundles.
- **Quality** = average length quality × (0.6 + 0.4 × purity), purity = share of what you stacked as timber that a buyer takes. Capped by the lower of the axe (worn 60, iron 85, steel 100) and skill (31 at skill 1, 100 at skill 100). Bark quality = tree grade, same cap.
- **Skill.** +2 if yield × purity × (1 on line, 0.8 off) ≥ 0.5, else +1, while within 30 of the stand's rank.

## Stands

| Stand | Rank | Height | Diameter | Rot | Crook | Bark per length |
|---|---|---|---|---|---|---|
| Pine stand | 1 | 16-25 m | 30-52 cm | 20% | 12% | 1 |
| Birch copse | 1 | 14-20 m | 24-38 cm | 32% | 30% | 2 |
| Oak grove | 20 | 16-23 m | 44-70 cm | 30% | 25% | 2 |

Bigger trees are older: better wood, more rot. Usable stem is 70% of the height; the rest is crown.

## Tests

`dotnet test tests/Logging.Tests` (needs the .NET 8 SDK). A bot that knows the hidden truth lands on its line every time, clean 97-100% of the time, gets every possible length, quality ~78 at grade 80, with 20-50 strength to spare. A hasty bot (nearest tree, north, shallow notch, level back cut, sawn through) hangs up half its trees and gets well under 60% of the expert's output.
