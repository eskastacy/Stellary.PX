# 04 · Design system

**Concept: Holy Tuff Black Chrome.** Cybersigilism reduced to material and
engraving: near-black lacquer, brushed silver, thin ritual lines, and a single
script gesture. No neon, no glassmorphism, no gradient rainbows, no dashboard
clichés, no emoji.

## Principles

1. **Monochrome first.** Hierarchy comes from value, weight, and spacing, not hue.
   Semantic color is desaturated so it reads as oxidized metal, not a traffic
   light.
2. **Engraved, not glowing.** Chrome is a directional gradient. Lines are
   hairlines. Nothing blooms.
3. **One gesture.** The script wordmark is the only calligraphic element; every
   other label is disciplined sans or small caps.
4. **Measured voice.** Copy names the mechanism (`NtSetTimerResolution`,
   `powercfg`, `HKCU`) and reports observed state. No promises of FPS.
5. **Quiet motion.** State changes are immediate; hover and drag affordances are
   subtle. No ambient animation.

## Palette

Defined in `src/Stellary.PX/Themes/Palette.xaml`.

| Token | Hex | Role |
|---|---|---|
| `C.Ink` | `#08080A` | Window base |
| `C.Ink2` | `#0C0C0F` | Deep recesses, log well |
| `C.Surface` | `#111114` | Card base |
| `C.Surface2` | `#15151A` | Secondary surface |
| `C.Surface3` | `#1A1A20` | Raised/hover surface |
| `C.Hairline` | `#26262E` | Structural divider |
| `C.HairlineSoft` | `#1C1C23` | Interior divider |
| `C.Text` | `#ECECF0` | Primary text |
| `C.Muted` | `#95959E` | Secondary text |
| `C.Dim` | `#5F5F69` | Metadata, eyebrows, hints |
| `C.ChromeHi` | `#F6F6F8` | Chrome highlight |
| `C.ChromeMid` | `#C3C3CB` | Chrome body |
| `C.ChromeLo` | `#6C6C77` | Chrome shadow |
| `C.Ok` | `#98A9A0` | Desaturated sage; trusted/held |
| `C.Warn` | `#B39183` | Desaturated rust; attention |
| `C.Info` | `#8A95A3` | Desaturated steel; neutral status |

`B.Chrome` is a five-stop diagonal gradient from `#FBFBFC` through `#D6D6DC`,
`#9A9AA4`, and `#E9E9EE` to `#8B8B95`. It is used for the wordmark, the hex
logo, large numerals, primary button faces, and gauge values.

`B.Card` is a vertical `#141419 → #101014 → #0D0D11`; `B.CardRaised` is
`#191920 → #121217`. The difference is one material step, not a glow.

## Typography

All fonts are embedded OFL files; no machine font is assumed except the mono
fallback chain.

| Role | Face | Size | Notes |
|---|---|---|---|
| Display / wordmark | Monsieur La Doulaise | 33–54 | Script gesture; chrome gradient |
| Small caps / eyebrows | Cinzel | 8–9.5 | Section labels and categories |
| Page title | Poppins Light | 22 | Display formatting |
| Card title | Poppins Medium | 13 | Sentence case |
| Numerals | Poppins Light | 22–46 | Tabular-looking, tight leading |
| Body | Poppins Regular | 12 | 19 px line height |
| Small / hints | Poppins Regular | 9–11 | Muted or dim |
| Technical | Cascadia Mono → Consolas | 8.5–11 | Hashes, paths, mechanisms |

Each Poppins weight is a separate static file with its own pack URI
(`pack://application:,,,/assets/fonts/poppins-light.ttf#Poppins`), because a
single synthetic family would let WPF choose the wrong face.

The wordmark is one `TextBlock` with two `Run`s: script `Stellary` and a
baseline-aligned Poppins SemiBold `.PX`. No manual offset or canvas alignment.

## Layout

- Window: 1360 × 840, minimum 1180 × 760, centered, 100% DPI reference.
- Sidebar: 252 px fixed; brand block 96 px; 22 px horizontal rhythm.
- Main content: 24 px left/right, 46 px title bar, 14 px gutters.
- Card padding: 18–24 px; interior dividers use 1 px hairlines.
- Page grids use `*` columns with a fixed right rail (300–330 px) where a summary
  or store belongs.

## Components

### Buttons
- **Primary:** chrome gradient face, ink text, 1 px highlight border, 22 px
  vertical padding group. Used once per view for the main action.
- **Ghost:** transparent fill, `#2A2A33` border, muted text; hover raises the
  surface to `#17171C` and brightens the border.
- **Link:** text-only, dim, underline on hover; used for folder shortcuts.
- **Icon:** 26–30 px square, no fill, 15 px stroke icon.
- **Window:** 34 px wide title-bar buttons; close hover uses a desaturated rust,
  not pure red.

### Cards
Two levels only: `Card` and `CardRaised`. Radius 3–4 px. A 1 px border at
`#1F1F27` carries the edge; shadows are absent. Content is separated by
hairlines, not nested boxes.

### Slider
3 px rail at `#2A2A33`, filled left rail at `#777782`, 17 px chrome knob with a
1 px rim and a soft inner highlight. Hover grows the knob's rim; drag brightens
it. Height is 22 px with a 10 px hit padding.

### Switch
36 × 18 px track, 12 px chrome thumb, 2 px travel. On state uses a muted chrome
track; off state uses `#2A2A33`. No color-only state: the thumb position changes.

### Navigation
Grouped by `CONTROL`, `SYSTEM`, `TOOLS` with Cinzel headers. Selected row uses a
`#15151A` fill, a 2 px silver left rule, and a brighter label. Icons are 15 px,
1.35 px stroke.

### Ring gauge
270° track, thin value arc, tick ring, and a radial needle notch crossing the
track at the value angle. Track `#383842`, value `#D8D8E1`. No glow, no gradient
arc.

### Sparkline
CPU solid chrome line with a 6–8% white area fill; RAM dashed `#5C5C68`. Samples
stretch across the full plot until the 60-second window fills, then scroll.
No grid, no axis labels except `60 s` and `now`.

### Brand and sigil
The mark is an original hexagonal double-frame with a drawn `S` path. The hero
sigil is a vertical axis with progressively fading arcs, a diamond, and a point —
an engraved diagram, not a mystical illustration. Both are vector paths, not
bitmap assets.

## Iconography

- 15 px viewbox, 1.3–1.5 px stroke, round caps and joins, no fills.
- Five social glyphs: Linktree, WhatsApp, TikTok, Discord, Instagram.
- Six navigation glyphs: grid, phone, chip, sliders, pulse, gear.
- No emoji, no icon font, no third-party icon package.

## Motion

- Hover: 120 ms color/border transition.
- Toggle: thumb translation only.
- Page switch: instant content swap; no slide or fade.
- Capture mode disables hit testing and is not a user-facing state.
- No parallax, particles, marquees, pulsing glows, or animated chrome.

## Voice and content

- Labels are sentence case; eyebrows are small caps.
- Mechanisms are named in mono: `SetPriorityClass`, `NtSetTimerResolution`,
  `powercfg /setactive`, `HKCU · GameDVR`.
- Empty states are explicit: `no active process`, `not detected`,
  `renderer not attached`.
- Every destructive or stateful action sits next to its restore action, or has a
  visible backup path.
- No exclamation marks, no "boost your FPS now", no unverifiable numbers.

## Accessibility notes

- Primary and secondary text meet normal contrast targets on card surfaces.
- Dim metadata is intentionally lower contrast for hierarchy; essential values
  and actions always use `Text` or `Muted`.
- State is never conveyed by color alone: switches move, rows gain a left rule,
  trusted rows read `trusted signature`.
- Hit targets are at least 26 px for icon buttons and 22 px for sliders.
- The UI is keyboard navigable through standard WPF focus traversal; sliders and
  switches use native automation peers.

## Do / don't

| Do | Don't |
|---|---|
| Use value contrast and hairlines | Add neon outlines or glow shadows |
| Keep one chrome accent per view | Gradient every heading |
| Use real SHA-256 and paths | Show truncated fake hashes |
| Say "not attached" | Imply a live shader effect |
| Use 15 px stroke icons | Use emoji or filled icon packs |
| Animate state, not decoration | Add ambient loops |
