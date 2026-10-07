# Thornwake Water — Experimental3A

## Purpose

Experimental3 is already very close to the water direction we want.

**Experimental3A should be a refinement branch based directly on Experimental3, not a redesign.**

The current wave shapes and overall animation are the strongest part and should be preserved. The main problems now are:

1. foam appearance and behaviour,
2. water looking slightly too matte / opaque,
3. insufficient translucency and depth response,
4. distant waves losing useful detail too quickly.

Reference screenshots are included with this package:

- `Experimental3_reference_overhead.png`
- `Experimental3_reference_horizon.png`

---

# Tom + Noor assessment

The wave geometry itself is working.

The storm sea finally has convincing large-scale motion, overlapping waves, irregular crests, and enough vertical mass to feel dangerous. We do **not** want to flatten this or restart the wave model.

The largest problem is the foam.

At the moment the foam is animated, but visually it often reads as **opaque white paint or snow sitting on the surface**. In several places, long streaks appear and seem to oscillate backward and forward around roughly the same location instead of behaving like material created by breaking water and then carried away by the moving surface.

The water itself also feels a little too matte. There is already some translucency, because in some views the seabed and underwater vegetation are visible, but we appear to be losing some of the depth, reflection/transmission balance and wet/glassy character that would make the surface feel like water rather than an opaque animated material.

Finally, the water loses too much character with distance. The nearby waves are rich and dramatic, while distant waves become comparatively smooth and visually simple too quickly.

So the goal for Experimental3A is:

> **Keep Experimental3's wave motion and wave shape. Improve foam, optical depth/translucency, surface response and distant-wave readability.**

---

# Prompt for Sage

Sage, please create **Experimental3A** as a safe refinement of the current **Experimental3** water.

## 0. Protect Experimental3 first

Before changing anything:

- preserve the existing Experimental3 implementation,
- make a backup/checkpoint so we can return to it,
- create Experimental3A from that state,
- do not replace Experimental3.

The current wave geometry and wave motion are valuable and should be treated as the baseline.

Do not substantially redesign the wave system unless a change is absolutely necessary to solve one of the issues below.

---

## 1. Preserve the current wave shape

We like the present storm-wave geometry.

Please keep:

- the current large-scale swell,
- the asymmetry of the waves,
- the overlapping/choppy motion,
- the irregular crest shapes,
- the sense of large water mass,
- the current storm intensity response,
- the general directional behaviour of the waves.

The goal is **not** calmer, cleaner or smoother water.

The goal is the same water, but visually more convincing.

---

## 2. Foam is the main problem

The current foam moves, but it often reads as white paint or snow.

Some streaks also appear to move backward and forward around the same area rather than being born, transported, broken apart and dissipated.

Please investigate whether this is caused by:

- oscillating UV displacement,
- wave phase being reused directly for foam motion,
- world-space / object-space anchoring,
- foam masks moving independently from the surface flow,
- procedural coordinates effectively "ping-ponging" with the wave,
- or another temporal/advection issue.

### Desired foam behaviour

Foam should primarily come from plausible breaking-wave conditions.

Strongest foam should appear:

- on steep or breaking crests,
- around sharp curvature / compression areas,
- immediately behind a breaking crest,
- in turbulent regions where multiple waves intersect.

Foam should be weaker or absent on ordinary smooth wave faces.

Not every crest needs identical foam.

### Foam life cycle

Please aim for a visible life cycle:

**crest forms -> cap appears -> wave breaks -> fresh foam spreads -> foam fragments -> fragments stretch/drift -> foam fades**

It does not need to be a full fluid simulation.

A convincing shader/procedural approximation is completely acceptable.

The important thing is that foam should have temporal continuity instead of appearing like a repeating animated mask.

### Foam hierarchy

Try to visually separate three scales:

1. **Breaking crest foam**
   - bright,
   - relatively solid,
   - attached to the lip/top of the strongest breaking waves.

2. **Fresh turbulent foam**
   - irregular patches behind the crest,
   - broken shapes,
   - less solid than the crest itself.

3. **Older foam / bubble streaks**
   - thinner,
   - fragmented,
   - stretched by water/wind direction,
   - gradually fading.

Use multi-scale breakup so the result contains large connected areas, medium fragments and fine streaks.

Avoid making the entire storm sea uniformly white.

Even in strong wind there should still be visible clean blue water between whitecaps.

---

## 3. Foam should travel, not visibly oscillate

This is important.

Right now some foam streaks seem to appear and move backward/forward in place.

Please make foam feel as though it is **advected across the moving surface**.

It should not obviously reverse direction just because the underlying wave phase reverses locally.

Possible inexpensive approaches are welcome, for example:

- directional advection,
- accumulated phase that only progresses,
- separate foam-flow coordinates,
- crest-generated masks with decay,
- multiple scrolling flow layers,
- or another cheap approximation.

We do not need physically exact ocean foam.

We need it to look temporally believable.

---

## 4. Water currently feels slightly too matte

The current surface sometimes feels more like a painted material than water.

Please investigate the balance between:

- roughness,
- specular response,
- Fresnel reflection,
- transmission/translucency,
- depth absorption,
- refraction,
- surface normals,
- environment reflection.

We do **not** want shiny plastic or glass.

We want water that has a wet, deep, partly translucent quality.

### Desired optical behaviour

At shallow depth:

- seabed and underwater plants should remain visible,
- water colour should tint them,
- visibility should depend on depth and viewing angle.

As depth increases:

- transmission should decrease,
- colour absorption should increase,
- deep water should become richer/darker,
- underwater detail should disappear progressively rather than through a hard cutoff.

At grazing angles:

- reflections should become stronger through Fresnel behaviour.

At steeper viewing angles:

- more of the water volume / underwater area can be visible where depth permits.

The ocean should still become visually opaque at sufficient depth.

---

## 5. Restore some sparkle and surface response

Experimental3 could use a little more high-frequency light response.

Please preserve the stylized Thornwake look, but allow small wave facets to catch light.

This may come from:

- better normal layering,
- lower/more varied roughness,
- small-scale normal detail,
- sun glints,
- Fresnel/specular tuning.

Do not turn it into glitter.

The goal is simply to stop broad areas from feeling flat or matte.

---

## 6. Distant waves lose detail too quickly

Nearby waves look excellent, but distant water becomes simplified too aggressively.

Please inspect the wave/water LOD behaviour.

We should preserve the perception of a living rough sea into the distance.

### Important distinction

We do **not** need expensive near-camera geometry everywhere.

Instead preserve different information at different distances.

#### Near field
Keep:

- full displaced wave form,
- crest shape,
- foam breakup,
- small normal detail.

#### Mid field
Preserve:

- major swell silhouette,
- mid-frequency wave variation,
- readable whitecaps,
- normal-map detail.

#### Far field
Preserve:

- large swell rhythm,
- enough normal variation to catch light,
- reduced but still visible whitecap patterns,
- horizon-scale movement.

The far sea should not turn into a smooth blue sheet.

Please check whether current mesh LOD, displacement LOD, normal mip behaviour, foam mipmaps or distance fades are removing detail too rapidly.

Avoid shimmering/aliasing while doing this.

---

## 7. Keep depth readability from above

The overhead reference is useful because it shows that some underwater depth information already exists.

Please keep this.

From above we should be able to read:

- shallow turquoise areas,
- deeper blue areas,
- submerged terrain where appropriate,
- underwater vegetation in sufficiently shallow/clear water,
- foam floating visually above this depth information rather than completely painting over it.

Large opaque foam sheets should not unnecessarily destroy all depth cues beneath them.

---

## 8. Wind connection

The screenshots are from very strong wind, around 19–20 m/s.

At this strength, a chaotic whitecapped sea is correct.

Please let wind affect at least the visual probability/intensity of:

- crest breaking,
- whitecaps,
- foam amount,
- foam persistence,
- optionally later, wind-blown spray.

But avoid making the foam coverage uniform.

Strong wind should increase activity, not simply turn a global foam slider upward.

---

## 9. Optional later pass: spray

Do not make this the priority for Experimental3A.

Once foam and optical behaviour are correct, the strongest storm crests could occasionally produce wind-blown spray/mist.

This should be sparse and selective.

Only particularly strong breaking crests need it.

A small amount of convincing spray will be more effective than particles everywhere.

---

## 10. Performance

Please keep an eye on performance while doing this.

The screenshots are currently showing 60 FPS in the test scene, and we do not want the visual improvement to come from an obviously disproportionate cost.

Prefer shader/procedural tricks over heavy simulation when the visual result is similar.

This is Thornwake: believable first, scientifically exact only where it actually helps the game.

---

# Suggested test views for Experimental3A

Please evaluate the result from several views instead of tuning only one camera angle:

### A. Horizon / player-height storm view
Check:

- wave silhouettes,
- crest foam,
- reflections,
- distant wave detail.

### B. High overhead view
Check:

- foam distribution,
- whether foam looks painted on,
- directional travel,
- shallow/deep colour transition.

### C. Shallow-water view
Use an area where seabed and underwater plants are visible.

Check:

- translucency,
- depth absorption,
- refraction,
- underwater readability.

### D. Looking toward distant sea
Check:

- when displacement detail disappears,
- when whitecaps disappear,
- whether the far sea becomes too smooth.

### E. Calm / moderate wind comparison
Experimental3A should scale downward gracefully rather than looking like storm foam with reduced opacity.

---

# Acceptance target

Experimental3A is successful if:

- it still immediately looks like Experimental3,
- the current wave shape is preserved,
- foam no longer resembles snow or opaque paint,
- foam does not visibly ping-pong in place,
- foam appears to be created by breaking waves and then transported/dissipated,
- shallow water has convincing translucency,
- deep water still feels deep,
- reflections/specular/Fresnel stop the surface from feeling matte,
- distant waves remain visibly alive,
- performance remains reasonable.

Do not chase perfection in one pass.

The current water is already close.

**Experimental3A should be a careful polish pass, not another restart.**

---

## Reference images

### Overhead / foam behaviour

![Experimental3 overhead reference](Experimental3_reference_overhead.png)

### Horizon / distant detail and surface response

![Experimental3 horizon reference](Experimental3_reference_horizon.png)
