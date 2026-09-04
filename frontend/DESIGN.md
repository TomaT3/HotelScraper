---
version: alpha
name: HotelScraper Bugatti
description: "A dark, austere data-dashboard interface derived from Bugatti's luxury-automotive design language — near-pure black canvas, white uppercase letterspaced display type, monospace labels, and transparent pill-shaped buttons. Adapted for data density: a functional categorical chart palette replaces photography as the visual voltage, monospace carries all numeric/tabular data, and the spacing rhythm is tightened from the marketing source's 120px editorial bands to a dashboard-scale rhythm."
colors:
  primary: "#ffffff"
  ink: "#ffffff"
  body: "#cccccc"
  body-strong: "#e6e6e6"
  muted: "#999999"
  muted-soft: "#757575"
  hairline: "#262626"
  hairline-strong: "#3a3a3a"
  canvas: "#000000"
  surface-soft: "#0d0d0d"
  surface-card: "#141414"
  surface-elevated: "#1f1f1f"
  on-primary: "#000000"
  on-dark: "#ffffff"
  link: "#c3d9f3"
  warning: "#d4a017"
  success: "#5fa657"
  danger: "#e05252"
  chart-1: "#60a5fa"
  chart-2: "#f87171"
  chart-3: "#4ade80"
  chart-4: "#fb923c"
  chart-5: "#c084fc"
  chart-6: "#22d3ee"
  chart-7: "#facc15"
  chart-8: "#f472b6"
  chart-9: "#a5b4fc"
  chart-10: "#34d399"
  chart-11: "#fbbf24"
  chart-12: "#e879f9"
  chart-13: "#38bdf8"
  chart-14: "#fb7185"
  chart-15: "#a3e635"
  chart-16: "#d946ef"
  chart-17: "#2dd4bf"
  chart-18: "#fca5a5"
  chart-19: "#93c5fd"
  chart-20: "#fcd34d"

typography:
  display-xl:
    fontFamily: "Saira Condensed, sans-serif"
    fontSize: 40px
    fontWeight: 400
    lineHeight: 1.1
    letterSpacing: 4px
  display-lg:
    fontFamily: "Saira Condensed, sans-serif"
    fontSize: 32px
    fontWeight: 400
    lineHeight: 1.15
    letterSpacing: 3px
  display-md:
    fontFamily: "Saira Condensed, sans-serif"
    fontSize: 24px
    fontWeight: 400
    lineHeight: 1.2
    letterSpacing: 2px
  display-sm:
    fontFamily: "Saira Condensed, sans-serif"
    fontSize: 20px
    fontWeight: 400
    lineHeight: 1.3
    letterSpacing: 1.5px
  wordmark:
    fontFamily: "Saira Condensed, sans-serif"
    fontSize: 14px
    fontWeight: 400
    lineHeight: 1
    letterSpacing: 6px
  title-md:
    fontFamily: "Saira Condensed, sans-serif"
    fontSize: 16px
    fontWeight: 400
    lineHeight: 1.3
    letterSpacing: 1px
  title-sm:
    fontFamily: "Saira Condensed, sans-serif"
    fontSize: 14px
    fontWeight: 400
    lineHeight: 1.3
    letterSpacing: 1.5px
  caption-uppercase:
    fontFamily: "IBM Plex Mono, ui-monospace, monospace"
    fontSize: 11px
    fontWeight: 400
    lineHeight: 1.4
    letterSpacing: 2px
  body-md:
    fontFamily: "IBM Plex Mono, ui-monospace, monospace"
    fontSize: 14px
    fontWeight: 400
    lineHeight: 1.5
    letterSpacing: 0
  body-sm:
    fontFamily: "IBM Plex Mono, ui-monospace, monospace"
    fontSize: 13px
    fontWeight: 400
    lineHeight: 1.5
    letterSpacing: 0
  body-serif:
    fontFamily: "EB Garamond, Georgia, serif"
    fontSize: 15px
    fontWeight: 400
    lineHeight: 1.6
    letterSpacing: 0
  button:
    fontFamily: "IBM Plex Mono, ui-monospace, monospace"
    fontSize: 13px
    fontWeight: 400
    lineHeight: 1
    letterSpacing: 2.5px
  nav-link:
    fontFamily: "IBM Plex Mono, ui-monospace, monospace"
    fontSize: 12px
    fontWeight: 400
    lineHeight: 1.4
    letterSpacing: 2px

rounded:
  none: 0px
  pill: 9999px
  full: 9999px

spacing:
  xxs: 4px
  xs: 8px
  sm: 12px
  md: 16px
  lg: 24px
  xl: 32px
  xxl: 48px

components:
  button-primary:
    backgroundColor: transparent
    textColor: "{colors.on-dark}"
    typography: "{typography.button}"
    rounded: "{rounded.pill}"
    padding: 12px 28px
    height: 40px
  button-ghost:
    backgroundColor: transparent
    textColor: "{colors.muted}"
    typography: "{typography.nav-link}"
    rounded: "{rounded.pill}"
    padding: 8px 16px
  icon-button:
    backgroundColor: transparent
    textColor: "{colors.on-dark}"
    rounded: "{rounded.full}"
    size: 40px
  panel:
    backgroundColor: "{colors.surface-card}"
    textColor: "{colors.body}"
    typography: "{typography.body-sm}"
    rounded: "{rounded.none}"
    padding: 16px
  wordmark-display:
    backgroundColor: transparent
    textColor: "{colors.on-dark}"
    typography: "{typography.wordmark}"
  top-nav:
    backgroundColor: "{colors.canvas}"
    textColor: "{colors.on-dark}"
    typography: "{typography.nav-link}"
    height: 56px
  text-input:
    backgroundColor: transparent
    textColor: "{colors.on-dark}"
    typography: "{typography.body-md}"
    rounded: "{rounded.none}"
    padding: 10px 0
    height: 40px
  tag:
    backgroundColor: transparent
    textColor: "{colors.muted}"
    typography: "{typography.caption-uppercase}"
  table-header:
    backgroundColor: "{colors.surface-soft}"
    textColor: "{colors.muted}"
    typography: "{typography.caption-uppercase}"
    padding: 8px 16px
  table-row:
    backgroundColor: transparent
    textColor: "{colors.body}"
    typography: "{typography.body-sm}"
    padding: 12px 16px
  status-success:
    backgroundColor: transparent
    textColor: "{colors.success}"
    typography: "{typography.caption-uppercase}"
  status-warning:
    backgroundColor: transparent
    textColor: "{colors.warning}"
    typography: "{typography.caption-uppercase}"
  status-danger:
    backgroundColor: transparent
    textColor: "{colors.danger}"
    typography: "{typography.caption-uppercase}"
---

## Overview

HotelScraper is a price-intelligence dashboard for hotel operators. Its visual identity is derived from Bugatti's luxury-automotive design language — the most austere interface in its category: a near-pure black canvas (`{colors.canvas}` — #000000) holding white, UPPERCASE, letterspaced display type and hairline dividers. There is no brand accent color, no shadows, no gradients, no decorative chrome. The voltage in the source system comes from full-bleed photography; in a data dashboard it comes from **the chart itself** — the price lines are the only saturated color on the page.

The system runs three typeface roles, mirroring Bugatti's trinity but remapped for data density:
- **Display** (Saira Condensed) — page titles, panel titles, the wordmark. Uppercase, wide-tracked, weight 400.
- **Mono** (IBM Plex Mono) — buttons, navigation, captions, labels, **and all numeric/tabular data**. The workhorse voice of a data interface.
- **Serif** (EB Garamond) — reserved for rare editorial prose. Not used for tables or controls.

The original Bugatti body voice (a serif text face) is deliberately swapped for monospace in this adaptation: hotel names, prices, dates, and table cells need tabular precision, not literary cadence.

## Colors

### Brand & Accent
- **Primary / Ink / On Dark** (`{colors.primary}` / `{colors.ink}` / `{colors.on-dark}` — #ffffff): The single brand color. White type and white CTA outlines on the black canvas.
- **Link** (`{colors.link}` — #c3d9f3): A desaturated ice-blue, the only non-monochrome UI color. Used for inline anchors and active/focus accents. Appears rarely, by discipline.

### Surface
- **Canvas** (`{colors.canvas}` — #000000): The default page floor. Pure black, everywhere.
- **Surface Soft** (`{colors.surface-soft}` — #0d0d0d): Table headers and dense data rows — a barely-different-from-black tone.
- **Surface Card** (`{colors.surface-card}` — #141414): Panel background (filter sidebar, chart card, status bar). Even panels stay nearly-black.
- **Surface Elevated** (`{colors.surface-elevated}` — #1f1f1f): Nested cards, hover states on rows.
- **Hairline** (`{colors.hairline}` — #262626): The 1px divider. Visible but quiet — panel borders, table row separators, chart gridlines.
- **Hairline Strong** (`{colors.hairline-strong}` — #3a3a3a): Heavier dividers and the underline on input fields (inputs have no box border — only a bottom hairline).

### Text
- **Body** (`{colors.body}` — #cccccc): Default running text (slightly cooler than pure white).
- **Body Strong** (`{colors.body-strong}` — #e6e6e6): Emphasized values, primary numbers.
- **Muted** (`{colors.muted}` — #999999): Metadata, dates, captions, secondary labels.
- **Muted Soft** (`{colors.muted-soft}` — #757575): Very-secondary text (empty states, fine print).

### Semantic
- **Success** (`{colors.success}` — #5fa657): Scheduler active, fetch-complete states.
- **Warning** (`{colors.warning}` — #d4a017): Partial-failure callouts, fetch errors.
- **Danger** (`{colors.danger}` — #e05252): Destructive/error states. (Added in this adaptation — the marketing source had no error color, but a dashboard needs one.)

### Chart Palette (functional exception)
- **Chart 1–20** (`{colors.chart-1}` … `{colors.chart-20}`): The ONLY saturated colors in the system, reserved exclusively for chart lines and their legend swatches. These are functional data-viz colors, not brand accents — the monochrome discipline applies to all UI chrome; it does not extend to distinguishing 20 price lines, which would otherwise be unreadable. All twenty are mid-to-light luminance so they hold contrast against the black canvas.

## Typography

### Font Roles
1. **Display — Saira Condensed (weight 400)** for page titles, panel titles, the wordmark. Always UPPERCASE with 1.5–6px letter-spacing. (Substitute for Bugatti Display, which is proprietary.)
2. **Mono — IBM Plex Mono (weight 400)** for buttons, navigation, captions, labels, and all data. Uppercase with 2–2.5px tracking in controls; sentence-case at 0 tracking for running data. (Substitute for Bugatti Monospace.)
3. **Serif — EB Garamond (weight 400)** for editorial prose only. Sentence-case, no tracking. Rare in a dashboard. (Substitute for Bugatti Text Regular.)

The roles are rigid: never use Display in a button, never use Serif in a table, never use Mono for a page title's body — the "machined precision" voice lives in Mono, the "engineered elegance" voice in Display.

### Principles
- **Weight 400 everywhere.** The system has no bold weight. Emphasis comes from size, tracking, case, and family contrast — never weight.
- **Display headlines are UPPERCASE.** Body copy and data stay sentence-case (or right-aligned tabular numbers).
- **Numbers use tabular/monospace figures** — the whole reason Mono carries data.

## Layout

### Spacing System
- Base unit: 4px.
- Tokens: `{spacing.xxs}` 4px · `{spacing.xs}` 8px · `{spacing.sm}` 12px · `{spacing.md}` 16px · `{spacing.lg}` 24px · `{spacing.xl}` 32px · `{spacing.xxl}` 48px.
- The marketing source's 120px editorial bands are **intentionally removed**. A dashboard compresses whitespace: panels sit at `{spacing.md}` (16px) internal padding with `{spacing.lg}` (24px) between major sections. The empty space that frames a Bugatti car has no place around a price chart — density is the point.

### Grid & Container
- Max content width ~1400px centered (dashboard needs more width than a marketing page).
- Filter sidebar + chart main column: a 1:3 split at desktop, single column at mobile.

## Elevation & Depth

- **Flat** (no shadow, no border) for the canvas, top nav, and chart area.
- **Soft hairline** (1px `{colors.hairline}`) for panel borders, table row separators, chart gridlines.
- **Card surface** (`{colors.surface-card}` background, no shadow) for panels.
- No shadows, no gradients, no glassmorphism. Depth comes only from the contrast between black canvas and minimally-elevated surfaces, and from the color of the chart lines.

## Shapes

### Border Radius Scale
- `{rounded.none}` (0px): everything — panels, tables, inputs, cards.
- `{rounded.pill}` / `{rounded.full}` (9999px): buttons and circular icon buttons only.

The radius hierarchy is binary: rectangular for everything except controls, which are pills. No 4/8/12px intermediates — those read as "designed" rather than "engineered."

## Components

### Buttons
- **`button-primary`** — the single high-emphasis action (e.g. "JETZT ABRUFEN"). Transparent background, white text, 1px white outline, pill radius, mono uppercase label with 2.5px tracking. The transparent pill IS the button; never fill it solid.
- **`button-ghost`** — secondary/toggle actions ("Verwaltung", "Abmelden", tab switches). Transparent, `{colors.muted}` text, pill radius, mono uppercase 2px tracking. The active state inverts to a white outline (still transparent fill).
- **`icon-button`** — circular 40×40px transparent buttons with a 1px white outline (close, chevron, menu).

### Surfaces
- **`panel`** — the standard container (filter sidebar, status bar, chart card, forms). Background `{colors.surface-card}`, 1px `{colors.hairline}` border, `{rounded.none}`, `{spacing.md}` padding.
- **`top-nav`** — 56px bar on `{colors.canvas}`, no fill, no border. Wordmark left/center, actions right, all labels in `{typography.nav-link}`.
- **`wordmark-display`** — the product wordmark in `{typography.wordmark}` (Saira Condensed, 14px, 6px tracking, UPPERCASE).

### Inputs
- **`text-input`** — transparent background, white text, `{colors.hairline-strong}` bottom border only (no top/left/right), mono text. Placeholder in `{colors.muted}`; focus thickens the bottom border to white.

### Data Display
- **`table-header`** — `{colors.surface-soft}` background, `{colors.muted}` uppercase mono captions.
- **`table-row`** — transparent with `{colors.hairline}` separators; hover lifts to `{colors.surface-elevated}`.
- **`tag`** — inline mono uppercase label in `{colors.muted}`. The "tag" is the type itself: no fill, no border.
- **`status-success` / `status-warning` / `status-danger`** — mono uppercase status text in the semantic colors.

## Do's and Don'ts

### Do
- Anchor the page on `{colors.canvas}` black with `{colors.hairline}` dividers — no box shadows, no gradients.
- Keep all display headlines UPPERCASE Saira Condensed weight 400 with 1.5–6px tracking.
- Use Saira Condensed for titles, IBM Plex Mono for controls/labels/data, EB Garamond only for prose.
- Keep `button-primary` transparent with a 1px white outline.
- Use weight 400 everywhere.
- Use `{colors.chart-1…20}` exclusively for chart lines/legend — nowhere in UI chrome.
- Right-align numbers; use monospace tabular figures for prices and dates.

### Don't
- Don't introduce any brand accent outside `{colors.link}` and the chart palette.
- Don't bold any type — the system has no bold weight.
- Don't fill primary buttons — transparent + outline only.
- Don't round anything except buttons — panels, tables, and inputs stay at 0px.
- Don't tighten display tracking below the specified values.
- Don't use serif (EB Garamond) for table cells or controls — it is prose-only.
- Don't port the 120px editorial spacing from the marketing source — a dashboard is dense by contract.

## Responsive Behavior

- **Mobile (< 768px):** single column; filter sidebar collapses behind a toggle; top nav keeps the wordmark centered; chart height reduces; table cells wrap.
- **Tablet (768–1024px):** sidebar + chart split; tables remain full-width.
- **Desktop (> 1024px):** 1:3 sidebar/chart split; multi-column tables.
- Touch targets: primary buttons ≥ 40px tall; icon buttons exactly 40×40px; inputs 40px tall.

## Font Loading

Fonts are self-hosted via `@fontsource` (Saira Condensed, EB Garamond, IBM Plex Mono, weight 400 only) — the deployment target is a Synology NAS that may be offline, so no CDN fonts. If the self-hosted files are unavailable, the fallback stack is `sans-serif` for Display, `Georgia, serif` for Serif, and `ui-monospace, monospace` for Mono.
