# Water experiment (round 9)

An experimental alternative to the water. **Nothing replaces the current water.**

* Switch: **Esc → Settings → Lighting and effects → Water**: `classic` (default, the water of build 14),
  `experimental` or `experimental 2`. It switches instantly, so you can flip back and forth on the same spot.
* Backup point: commit `3511e4b` (docs/SNAPSHOTS.md, "Water baseline"). `Game/Rewrite/Water.gdshader` and
  `OceanWaves.cs` are unchanged since then; the experiment lives in `Game/Rewrite/WaterExperimental.gdshader` plus
  extra per-vertex data that the classic shader ignores. To drop the experiment entirely: delete that shader and
  the setting; nothing else depends on it.
* Kept as it was: biome water tint (UV2) inside the absorption and scattering, depth colour and clarity, refraction,
  caustics, river plumes, screen-space reflections, underwater view, the local water simulation, wind and sea state.

## What the experiment changes

### Smooth at every distance (no faceting)
The big waves still move the mesh where the mesh is fine enough to carry them, but the light on every wave is
computed per pixel from the exact wave formula. Far away, where the water mesh is coarse, the surface still shades as
smooth rolling water instead of flat triangles.

### Oceans
* **Swells**: the same Gerstner sea as before (boats and swimming can use the same surface), plus wind waves:
  six short travelling waves fanned around the wind, with wobbling crests and gusty patches, and fine ripples near
  the camera. Different scales move at different speeds and headings.
* **Shore waves**: near land, crest lines follow the depth contours, so waves always run along the shore and roll in
  toward it, getting closer together and steeper as the water shallows, growing in height until they break (when they
  are ~¾ of the depth) into foam that trails behind the crest. The open-sea waves fade out over the shallows.
* **Exposure**: open coasts get the full surf, bays and coves only a fraction (measured from how much open sea lies
  around each coast point).
* **Rocky coasts**: steep shores churn — rough chop and foam — instead of rolling breakers.
* **Swash**: the last of each wave runs up the beach in step with the breakers; the thin film fades softly.
* **Storms**: bigger, longer, steeper seas, stronger wind waves, milky aerated crests, whitecaps, rougher and greyer
  reflections, longer breakers. Calm weather: small smooth waves and glassy reflections.

### Lakes
The same wave set cut to the lake's size (fetch): small lakes only carry short ripples and stay calm, big lakes
(tens of km) get real wind waves and approach a small sea. Calmer along the shore. Ponds stay almost still.

### Rivers
No swells. Three layers of ripples carried downstream at the current's speed and smeared along it, so the flow
direction reads; glassy in slow pools, rough in fast reaches; whitewater patches that pulse in fast and shallow
water (riffles and rapids); foam streaks drift with the current.

### Foam
One moving, swirled, bubbly pattern revealed by a "foam amount" from every source — breakers, crests, rocks, rapids,
shore contact — with soft edges, so foam thickens where the source is strong and breaks into lace and holes as it
fades. Not a painted outline.

### Light
Fresnel reflection (look down: see into the water; look across: reflections), a tight sun glint moving over the
waves, light through thin crests and breakers, stirred-up shallows cloudier.

## Experimental 2 (round 10: Sea of Thieves references)
Everything from experimental (shore waves, lakes, rivers, biome tint), with a livelier ocean:
* **Many sharp waves**: 32 waves drawn from an ocean wave spectrum (JONSWAP) instead of 12 — overlapping crests in
  mixed directions, a swell from another heading. Storms spread the directions much wider (a confused sea) and are
  stylised steep: ~8 m seas about twice as steep as a real storm, so the maximum sea state is dramatic, not just big.
* **Wave sets**: groups of higher waves drift through with the wind.
* **Look**: deep saturated blue-teal, bright turquoise light through thin wave tops (strongest toward the sun, still
  visible from the side), dense sun sparkle; storms grey and darken the sea and dull the reflections.
* **White water**: high steep crests break white, foam spills down the backs of waves, long foam lanes run along the
  wind in strong winds, drifting old-foam fields in storms, milky aerated crests.
* **Foam detail** (layered): fresh foam is dense; as it thins it becomes a lace of thin bright threads around bubble
  cells with fine speckle; a pale milky layer under the surface shines through; foam is pulled into streaks along the
  wind or current and shaded so it is not flat white.
* **Shore**: the hand-over from open-sea waves to waves facing the shore starts further out (from ~19 m deep), so you
  see them swing round toward the beach.

### Round 12 (build 18): one water, many states (Noor's review)
* **Weather**: calm is a long soft swell with no whitecaps; breezy has a few small caps that come and go; rough has
  many; storms are dense. Settings → Sky → Sea state now says which (calm / breezy / rough / storm).
* **Foam in pieces**: whitecaps form and fade, and break into streaks and fragments along the wind instead of big
  continuous white masses; only breakers, rocks and storms make dense foam.
* **Broader swell**: the longest swells are 150–320 m, so the whole sea slowly rises and falls (boats will ride it).
* **Coasts**: from about 20 m deep toward the shore, the waves turn to face the shore and line up into sets running in,
  instead of the open-sea chaos all the way to the beach; the breakers take over in the last metres.

### Round 11 (build 17): storms from calm to huge, crisper crests, directions that depend on the place
* **Smooth storm levels**: Settings → Sky → Sea state now runs smoothly from a calm sea (a long, soft swell slowly
  lifting the whole surface) through lively moderate seas to huge ~14 m storm seas at 1. Weather storms push it up on
  top. Changing it, or a storm rolling in, grows the waves gradually; nothing pops or jumps any more.
* **Crisper crests**: the biggest waves now get most of the sharpening, so the main ridges come to sharp peaks instead
  of rounded humps.
* **Directions differ from place to place**: the waves follow the local wind, and the wind is turned by the land and
  the weather differently in different parts of the sea (often ~30°, up to ~70° either way, changing over kilometres). Each lake has its own
  valley wind. Swells from far-away storms come from their own directions, which change slowly across the oceans, so
  in one sea the swell may cross the wind waves and in another run with them. The wave height stays the same; only
  the directions change.
* **Shores** (thanks to Noor's notes): the open-sea waves keep their own angle further into the shallows while the
  shore waves build up facing the beach, so waves arrive at an angle, not all perfectly parallel; parts of a crest run
  ahead or lag, and some break sooner than others, so the foam line is ragged.
* Waves no longer jump when the game re-centres the world as you travel (every 1.5 km).
* Rocks: you stand on the stone itself now (collision used to be a ball around each rock, so you floated above flat
  rocks).

### Which way should waves go? (Tom's question)
Out at sea, waves run with the wind, plus swells from distant storms that can come from any direction; they do not
care where land is. Near land, as the water gets shallower, waves slow down — the part in shallower water slows first —
so they bend (refraction) until they face the shore, and almost always arrive at a beach nearly head-on, whatever
direction they started from. Experimental and experimental 2 imitate this: open-sea waves fade out over the shallows
while a second set of waves, whose crests follow the depth contours, takes over and rolls straight in.

## Comparing
Good places in the *Mountain kingdoms* preset (seed 42): a beach at −60420, 116300 (Settings → Sky → Sea state
0.3–0.5, then 0.9 for a storm), open sea at −66000, 115600 (fly up), a big lake at −29042, 89908. Rivers: any
mountain river; fast ones show the whitewater.

## Not done yet / later
* Wave height near the shore for boats (the CPU mirror exists: `WaterShore.Height`; swimming already uses it).
* Quality settings for the experiment (number of wave layers, foam detail); everything is cheap enough on desktop
  GPUs now, the main extra cost is the per-pixel wave sum.
* Experimental 2 at the beach: where breaker foam meets the coarser far water mesh its edge can still show a short
  straight step.
* Boat wakes, wave interaction with individual rocks (only steep shores
  churn now).

Test harness: `xvfb-run godot --path . -s tools/water_test.gd -- out=/tmp/w.png style=1|2 kind=sea|lake|river|deep sea=0.5`
renders a beach / lake / river with either style in seconds.
