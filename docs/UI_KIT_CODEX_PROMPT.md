# Prompt for Codex: Thornwake UI kit (from Tom's mockup)

Paste everything below the line into Codex. Codex works in **its own folder** (it has no access to the Thornwake
project): put Tom's mockup image in that folder as `reference/ui_mockup_01.png` first. When Codex delivers, copy the
contents of its `ThornwakeUI/` folder into the project root (the paths inside match the project: `Assets/UI/…`,
`ArtKit/UI/…`, `docs/UI_KIT.md`), or give the folder to Claude to bring in.

---

## Your task

Create the **UI element kit** for the game **Thornwake** (Godot 4.7, C#), following Tom's mockup
`reference/ui_mockup_01.png` (in your working folder). The mockup is a mood and layout guide, not a final design: reproduce its
**style and parts**, not the screenshot. The game will assemble every window from your parts in code, so we need
**separate, reusable pieces** (frames, buttons, slots, icons…), each in all its states, **with no text baked in**.

You work in your own folder, without the game's project. Put **everything you make inside one folder,
`ThornwakeUI/`**, laid out like the game project so it can be copied in as it is:
`ThornwakeUI/Assets/UI/…` (the game files), `ThornwakeUI/ArtKit/UI/…` (previews and sources),
`ThornwakeUI/docs/UI_KIT.md` (notes) and `ThornwakeUI/Licenses/UI_LICENSES.txt` (one line per font or third-party
source). All paths below are relative to `ThornwakeUI/`. Everything the kit needs from the game (item names, building
categories, windows) is listed in this prompt.

If you cannot generate images directly, **draw them with code**: SVG, or scripts (Python with Pillow is fine) that
write the PNGs pixel by pixel. Keep those scripts in `ArtKit/UI/source/` with a `README` saying how to run them, so
every part can be regenerated in another colour or size. Pixel-art icons suit this well; for wood and parchment use
simple procedural grain/fibre noise rather than flat fills.

**Start with the style tile (step 1) and stop for Tom's approval before producing the rest.**

## The look (from the mockup)

* Medieval-survival, warm and earthy: **dark aged wood / dark leather** window frames with thin **brass-gold trim**
  and small corner studs; **parchment** inner panels for reading areas (settings, recipes, quests, banners);
  **dark charcoal** inner panels for item grids, tooltips and the character view.
* Edges slightly worn, never glossy or sci-fi. Light comes from the top-left: a 1–2 px highlight on top edges, a
  soft shadow under raised elements (buttons), inset look for slots and fields.
* The game world is chunky pixel art / voxel-like (32–128 px textures, nearest filtering). **Icons** must match that:
  pixel-art with a dark 1 px outline, readable at small size. **Frames and panels** may be painted at higher detail,
  but keep them crisp (no blur, no soft glow).
* Colour tokens (approximate, sample the mockup and refine; list the final values in `docs/UI_KIT.md`):

| Token | Use | Approx. |
|---|---|---|
| wood_dark | window frame, title bar | #2B2118 |
| wood_mid | frame highlights, tab background | #4A3A28 |
| charcoal | dark inner panel, slot fill | #1E1C1A |
| parchment | light inner panel | #E6D8B8 |
| parchment_shade | parchment edges, separators | #B9A57E |
| gold_trim | trim, selected outline, studs | #C9A45C |
| ink | text on parchment | #2A2016 |
| bone_text | text on dark | #EDE3CC |
| accent_green | primary action (Craft, Create World) | #2F5A3E |
| health_red / stamina_amber / food_orange / water_blue | status bar fills | #B53A2E / #D9A33A / #C9782E / #3C78C8 |

## Technical rules (all elements)

* **PNG, RGBA, sRGB, transparent background.** No text, numbers or letters in any image (the game draws text so it
  can be translated and scaled). Exceptions: the logo, and the compass letters N/E/S/W only if delivered as separate
  small images.
* **Pixel grid:** draw at **1×** for a 1920×1080 screen and also export **2×** (`@2x` suffix) for 4K / large UI scale.
  Integer scaling only; no anti-aliased resampling of pixel-art icons.
* **9-slice:** every frame, panel, button, field, slot and bar is a **9-slice** image: corners stay fixed, edges and
  centre stretch or tile. Keep the centre plain enough to stretch (put texture detail in tiling strips if needed and
  say so in the manifest). Give the slice margins (left, top, right, bottom, in 1× pixels) in the manifest.
* **States** as separate files: `normal`, `hover`, `pressed`, `disabled`, `focus` (keyboard/controller focus
  outline, gold) wherever the control is interactive; `checked` variants for toggles; `selected` for slots and tabs.
* **Naming:** `Assets/UI/<Group>/UI_<Group>_<Name>[_<State>][@2x].png`, e.g.
  `Assets/UI/Buttons/UI_Buttons_Menu_hover.png`, `Assets/UI/Icons/Items/UI_Icon_Item_stone_axe.png`.
* **Manifest** `Assets/UI/ui_manifest.json`: one entry per element: file(s), size, slice margins, content padding
  (where text/children may start), states, which Godot control it is meant for (Panel, Button, CheckBox,
  OptionButton, HSlider, LineEdit, ProgressBar, ScrollContainer, TabBar, ItemList, TextureRect, PanelContainer),
  and a one-line purpose.
* **Preview sheets:** `ArtKit/UI/preview_<group>.png` showing every element of a group in every state on both a
  dark and a light background, at 1× and 2×, labelled. Tom reviews these, not the raw files.
* **Licences:** everything self-made, or CC0. For fonts only SIL OFL or similar free-to-ship licences; put the licence
  file next to the font and add a line to `Licenses/UI_LICENSES.txt`.
* **Sources:** keep layered/vector sources in `ArtKit/UI/source/` (Aseprite, Krita, PSD or SVG).

## Step 1 — style tile (stop here for approval)

One small set that shows the whole style: a window frame with title bar, a parchment inner panel, a dark inner
panel, the menu button (all states), the primary green button (all states), an inventory slot (empty, hover,
selected), a checkbox (off/on), a slider (track, fill, grabber), one status bar (health), and six icons (stone axe,
log, stone, berries, heart, gear). Deliver `ArtKit/UI/preview_style_tile.png` plus the files and manifest entries.

## Step 2 — the full kit (after Tom approves step 1)

### Windows and panels (`Assets/UI/Panels/`)
* **Window frame** (dark wood with gold trim, corner studs) with an integrated **title bar** area; separate
  **close button** (small X, all states). Must suit windows from 300×200 to 1600×900.
* **Title icon plate**: the small square behind the window's icon in the title bar.
* **Parchment panel** (inner, light) and **dark panel** (inner, charcoal), both 9-slice; **parchment card** for
  recipe detail headers.
* **Sidebar** for category lists (dark wood strip) — used by Settings and Crafting.
* **Separators**: horizontal and vertical lines for parchment and for dark panels (tileable).
* **Tooltip** panel (dark, thin gold edge) with a small pointer arrow (separate image, four directions).
* **Banner** (parchment strip with frayed/rolled ends) for "Location discovered" and notifications; an **emblem
  socket** on its left side.
* **Quest panel** (parchment, slightly torn edge) for the quest tracker.
* **Main menu backdrop plate** behind the menu buttons (optional, subtle).

### Buttons and tabs (`Assets/UI/Buttons/`)
* **Menu button** (main menu column: Continue, New World…): long dark wood plate, gold edge — all states.
* **Primary button** (green: Create World, Craft) and **secondary button** (dark: Cancel, Apply, Reset, Back) —
  all states.
* **Small square buttons**: arrow left/right/up/down (steppers, quantity), plus, minus, close — all states.
* **Side tab** (Settings categories, Crafting categories): unselected, hover, selected (raised, lighter wood, gold
  edge on the open side).
* **Top tab** (horizontal tabs) — same states.
* **List row** (crafting recipe list, saved worlds): normal, hover, selected (dark highlight bar with gold edge).

### Form controls (`Assets/UI/Controls/`)
* **Checkbox**: box unchecked/checked × normal/hover/disabled/focus, for parchment and for dark backgrounds.
* **Dropdown** (OptionButton): field (all states), arrow, and the **popup list** background plus item hover row.
* **Slider**: track, filled part, grabber (normal/hover/pressed), for parchment and dark.
* **Text field** (LineEdit): normal, focus, disabled; caret colour noted in the manifest.
* **Number stepper** frame (the "◀ 1 ▶" quantity box in Crafting).
* **Scrollbar**: track, grabber (normal/hover/pressed), vertical and horizontal.
* **Progress/XP bar** (thin, the level bar in Character).

### Inventory and equipment (`Assets/UI/Slots/`)
* **Item slot** (dark inset square, 64×64 at 1×): empty, hover, selected (gold outline), disabled, **drop target**
  (green tint), **invalid** (red tint).
* **Hotbar slot** with a corner area for the key number (the number itself is drawn by the game).
* **Stack count backing** (tiny dark pill for the bottom-right count) — optional if text with outline reads well.
* **Equipment slots** with ghost silhouettes: head, chest, hands, legs, feet, back/cape, main hand, off hand,
  trinket ×2. Empty and filled-frame states.
* **Weight indicator** frame (top right of Inventory: "42.3 / 100") — just the small plate.

### HUD (`Assets/UI/HUD/`)
* **Status bars**: frame (9-slice), background, and fills for health (red), stamina (amber), food (orange),
  water (blue), warmth (cold blue ↔ warm orange: two fills), plus a **damage flash** overlay strip.
* **Compass strip** (top centre): bar, tick marks (tileable), cardinal markers, and marker icons: player arrow,
  quest diamond, home/camp, discovered place, other player, animal tracked.
* **Minimap frame** (round, brass ring) with a separate cardinal letter plate if needed; a **coordinates plate**.
* **Clock plate** (day and time box) and the **weather plate** under it.
* **Interaction prompt** plate ("E — Pick up the stone"): key cap background (separate) and the text plate.
* **Notification toast** (small, dark) and the **Location discovered** banner (see Panels) with a **compass-rose
  emblem**.
* **Crosshair** set: dot, small cross, bow aim (with draw ring), build placement.

### Icons (`Assets/UI/Icons/`, pixel-art, 32×32 and 64×64 at 1×, transparent, 1 px dark outline)
* **Items** (one per game item, named by these ids): stone_axe, stone_knife, spear,
  stone_sword, bow, arrow, raw_meat, cooked_meat, hide, bone, log, branch, stone, plant_fibre, berries,
  fishing_rod, bait, dried_meat, raw_fish, cooked_fish, book_felling. Also, for planned items: torch, pickaxe,
  iron_ore, iron_ingot, rope, campfire, workbench, simple_bed, wooden_shield, flint, clay, water_skin.
* **Building categories**: Foundation, Floor, Wall, Door, Stairs, Roof,
  Fence, Camp.
* **Stats**: health (heart), stamina (lightning), food (fork and knife), water (drop), warmth (thermometer),
  cold (snowflake), wet (droplets), armor (shield), damage (crossed swords), speed (boot), weight (sack).
* **Window/title icons**: settings (gear), new world (globe or house), inventory (bag), crafting (hammer),
  character (person), quests (compass), map (scroll map), building (trowel or beam), multiplayer (two figures),
  save, load, benchmark (gauge).
* **Weather** (day and night versions where it differs): clear, partly cloudy, overcast, rain, heavy rain, storm,
  snow, fog/mist, wind.
* **Input keys**: an empty key cap and a mouse (left/right/wheel) set; the game draws the letter on the cap.

### Logo (`Assets/UI/Logo/`)
* **THORNWAKE** wordmark in the mockup's spirit: carved/forged serif letters, the **O as a compass rose**.
  Variants: full colour on transparent, light (for dark backgrounds), dark (for parchment), small (for the
  window title), and the compass rose alone (emblem, also used in the discovery banner). Deliver at 1× (about
  900 px wide), 2×, and as a vector source.

### Fonts (`Assets/UI/Fonts/`)
Tom plans **two or three fonts** for the whole game. Propose them (free to ship, e.g. SIL OFL), with one sample sheet
each (`ArtKit/UI/preview_fonts.png`, Latin with Polish diacritics ąćęłńóśźż):
1. **Display** — headings and window titles (a classic serif with character, like the mockup's titles).
2. **Body** — settings, descriptions, tooltips, readable at 14–16 px (a book serif or humanist sans).
3. **Numbers/HUD** (optional) — compact, very readable for counts, bars and the clock; may be a pixel font if it
   suits the icons.
Deliver the font files with their licence files (you only make files; nothing is wired into the game).

## Delivery checklist (per step)
* Files under `Assets/UI/<Group>/` with the naming above, 1× and @2x.
* `Assets/UI/ui_manifest.json` updated.
* Preview sheet(s) in `ArtKit/UI/`.
* `docs/UI_KIT.md`: colour tokens, slice margins summary, which element is meant for which window of the mockup,
  anything unfinished.
* `Licenses/UI_LICENSES.txt` lines for fonts and any third-party source.
* A short summary for Tom: what was made, what to review first, open questions.

## What happens next (for context, not for you to do)
Claude will build a Godot `Theme` from the manifest (StyleBoxTexture with your slice margins, per-state textures,
fonts) and rebuild the game's windows (main menu, settings, new world, inventory, crafting, character, HUD, quests,
map) from these parts.
