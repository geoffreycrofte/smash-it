# Smash It landing page: design notes

> Reference copy. The landing page lives in the
> [geoffreycrofte.com](https://github.com/geoffreycrofte/geoffreycrofte.com)
> repository, which holds the source of truth (`docs/smash-it/DESIGN.md`). The
> file paths below are paths in that repository. Live page:
> <https://geoffreycrofte.com/project/smash-it/>

The landing page for **Smash It**, Geoffrey Crofte's free Windows app that makes
images, videos and PDFs smaller. It lives at `/project/smash-it/` and
`/fr/projet/smash-it/`, inside geoffreycrofte.com, but it has its own art
direction, borrowed from the app's mascot.

Sources: `src/smash-it.en.html`, `src/smash-it.fr.html`,
`src/assets/css/smash-it.css`, `src/assets/js/smash-it.js`. Images in
`htdocs/assets/img/smash-it/`.

---

## 1. Mood

The mascot sets the tone: an angry black bear with red fur tips, painted with
rough brush strokes, holding files like he is about to crush them. The look is
close to the animated series *Arcane*: painted textures, hard ink blacks,
neon-ish red light, scratched metal, graffiti on walls.

Three words: **painted, punchy, honest.**

- **Painted.** Grain, rough edges, brush-stroke underlines, torn section edges.
  Nothing perfectly flat, nothing perfectly round.
- **Punchy.** Big spray-paint titles, strong contrast, a single loud colour
  (red) on near-black ink.
- **Honest.** The app is a utility. Every animation shows something true
  (sizes going down, files getting smaller), never decoration for its own sake.
  Real numbers come from real screenshots.

What it is not: neon cyberpunk, glossy 3D, gradients everywhere, or a grunge
texture so heavy that text becomes hard to read.

---

## 2. Colour

Two themes, like the rest of the site. Dark is the "native" one, closest to the
mascot. Light is a **poster on paper**: the same inks printed on warm, worn paper.
All colours go through `light-dark()`, declared once on `.smash`.

| Token | Dark | Light | Use |
|---|---|---|---|
| `--s-bg` | `#0E0A0C` ink | `#F1E9DE` paper | page |
| `--s-surface` | `#1A1316` | `#E7DCCC` | cards, table |
| `--s-surface-2` | `#251B1F` | `#DCCFBC` | code, chips |
| `--s-heading` | `#F3ECE4` bone | `#17100F` ink | titles, strong text |
| `--s-text` | `#DAD1C9` | `#2C2220` | body |
| `--s-muted` | `#ABA19A` | `#5A4B45` | captions, labels |
| `--s-red` | `#FF5A66` | `#B0001E` | accent text, links, focus fill |
| `--s-red-paint` | `#C8102E` | `#C8102E` | large painted shapes only, never small text |
| `--s-tan` | `#D9B48F` | `#7A5530` | muzzle tan, secondary accent |
| `--s-line` | `#7D6F72` | `#857368` | borders that are the only boundary (3:1) |
| `--s-line-faint` | `#33282C` | `#D3C5B2` | decorative borders only |

Rules:

- `--s-red` is the only red allowed on small text. It clears 4.5:1 on `--s-bg`
  **and** on `--s-surface` in both themes.
- `--s-red-paint` is for slashes, glows and bars. Text placed on it is bone
  (`#FFF6EE`), which clears 4.5:1 on that red.
- Information never lives in colour alone: savings are written as numbers, the
  bars only illustrate them.

---

## 3. Type

| Role | Font | Notes |
|---|---|---|
| Hero title (h1 only) | **Rubik Dirt** 400 | The grittiest of the family. Subset to the letters of SMASH IT (26 KB), OFL, self-hosted. |
| Display (h2, big numbers) | **Rubik Spray Paint** 400 | Spray-painted edges, still very legible. OFL, self-hosted, subset to Latin + French (106 KB). |
| Everything else | **Inclusive Sans** 400 / 700 | Already on the site, built for legibility. |

- h1 is uppercase, huge (`clamp(3.5rem, 13vw, 9rem)`), tight line height.
- h2 is sentence case, `clamp(2rem, 6vw, 3.5rem)`. Sentence case reads better
  than capitals, and the spray texture already shouts.
- h3 and below stay in Inclusive Sans 700. Grunge is for the big moments only.
- Body copy is at least 1.0625rem with a 1.7 line height and a 38rem measure.
- Rubik Dirt is kept to the h1: at 8rem its holes read as texture, at h2 size
  they start to break letters apart.

---

## 4. Texture and shapes

- **Grain.** An SVG `feTurbulence` noise, inlined as a data URI, laid over the
  whole page at low opacity. No image request, no third party.
- **Brush underline.** A rough red stroke under each h2, drawn with a CSS mask
  (inline SVG path). It "paints" in from left to right when the heading
  scrolls into view.
- **Torn edges.** Section bands use a jagged `clip-path` polygon on top and
  bottom, like torn posters.
- **Shards.** Small red polygons, taken from the app icon, burst out once behind
  the mascot when the page loads. That is the "smash".
- **Sticker buttons.** Buttons are slightly skewed blocks with a hard offset
  shadow, like stickers slapped on a wall.

---

## 5. Motion

Every animation is finite. Nothing loops, so there is nothing to pause
(RAWeb 13.8).

| Where | What | Trigger |
|---|---|---|
| Hero | Mascot drops in, shards burst out | Page load, once |
| Hero | Mascot follows the pointer by a few pixels | Pointer move, fine pointers only |
| h2 | Brush stroke paints in | Scrolled into view, once |
| Results table | Bars shrink from "before" to "after" | Scrolled into view, once |
| Total saved | Number counts up | Scrolled into view, once |
| Cards, screenshots | Lift, tilt, red slash wipes in | Hover **and** focus-within |
| Buttons | Sticker shifts, shadow grows | Hover **and** focus |

- `prefers-reduced-motion: reduce` removes all of it: the site-wide rule in
  `styles.css` already cuts durations, and `smash-it.js` skips the pointer
  effect and the counter.
- Content is never hidden waiting for a script. Reveal states are only applied
  under `.smash-js`, a class the script itself adds.

---

## 6. Layout

1. **Hero.** Text left, mascot right on a red glow. Eyebrow, h1, tagline,
   two calls to action, a status line. Stacks on narrow screens, mascot first
   but smaller.
2. **How it works.** Three steps: Drop, Smash, Done. Big spray numbers.
3. **Real results.** The eight photos from the README screenshot, as a real
   `<table>`, with bars that shrink. Total saved underneath.
4. **What it handles.** Images, videos, PDF: one card each.
5. **See it.** The three screenshots in figures with captions.
6. **Safe by design.** What Smash It promises never to do to your files.
7. **Command line.** A terminal block with a copy button.
8. **Questions.** Native `<details>` disclosures.
9. **Coming next.** The roadmap.
10. **Download** (`#download`). The mascot's head, then two download cards:
    the installer (recommended, red border and a "Recommended" tag) and the
    portable zip. Each card links straight to the file of the latest GitHub
    release (`/releases/latest/download/...`), so the page never needs editing
    for a new version. Below: the SmartScreen note, then links to all
    releases, the source code and the issues. The hero's main button jumps
    here rather than to GitHub.

Max width 72rem. Sections breathe: `clamp(4rem, 10vw, 7rem)` vertical padding.

---

## 7. Accessibility checklist (RAWeb 1.1, AA)

- One `h1`, then `h2` per section, `h3` inside cards. No level skipped.
- Landmarks: `header`, `main`, `footer`. The skip link goes to `#main`.
- The mascot image has a short alt. The app icon next to its name is
  decorative (`alt=""`). Screenshots get a short alt plus a visible
  `<figcaption>`.
- The results table has a `<caption>`, `<th scope="col">` and
  `<th scope="row">`. The bars are `aria-hidden`. It sits in a focusable,
  labelled scroll region so it never forces the page to scroll sideways at
  320px.
- The animated total is `aria-hidden`; a visually hidden copy carries the final
  value, so screen readers never hear a number counting.
- The copy button announces "Copied" through a `role="status"` region. Its
  words come from `data-` attributes, never from the JS file.
- Focus ring: 3px solid, `--s-heading` on dark, ink on light, both over 3:1.
- Every hover effect also runs on `:focus-visible` or `:focus-within`.
- External links (GitHub) are plain links in the same window.
- Forced colours: grain, glows, shards and bars are hidden, borders come back.
- Text spacing overrides (RAWeb 10.12) do not clip anything: no fixed heights
  on text containers.

---

## 8. Assets

| File | Source | Size |
|---|---|---|
| `mascot.webp` / `.png` | `assets/branding/mascot.png`, 720 px wide | 85 KB WebP |
| `mascot-head.webp` / `.png` | `assets/branding/mascot-head.png`, 480 px | 28 KB WebP |
| `app-icon.webp` / `.png` | `assets/branding/smash-it-icon.png`, 192 px | 10 KB WebP |
| `screen-*.webp` / `.jpg` | `docs/screenshots/*.png`, cropped to the app window (1265 x 792) | 31 to 148 KB WebP |
| `open-graph.png` | Generated: mascot, title, red slash, 1200 x 630 | |

Re-export with Pillow (WebP quality 82, method 6) if the smash-it repo art
changes. The share card is the same in both languages, like the rest of the
site.
