# PressureMap UI / motion plan

Phase 0: preserve the current composition, capture 1440 / 820 / 390 × zh / en × light / dark. No production edits before capture.

Existing motion: smooth anchor scrolling; button and platform hover translation; drawer entrance; per-character hero entrance and perpetual rotation; checkbox checkmark, strike and color transitions; pressure-ring hover/press. No section reveals, exit transitions, FAQ motion, theme/language continuity, ring counting or functional widget movement.

Confirmed obsolete rules: pressure dialog `[open]`, `::backdrop`, `#pressure-confirm`; download dialog after replacement; nav-download styles without markup; duplicate h1 / hero-title / nav / pressure overrides; task-strike implementation superseded by pseudo-element strike. Translation-only orphan entries: 了解了, 知道了, old release placeholder and old tagline.

Phase 1: strong accent, readable type, clean screenshot frame, persistent nav CTA, cascade consolidation. Preserve source order of media rules while merging consecutive duplicate selectors; do not reorder responsive overrides.

Phase 2: shared observer, motion tokens, safe progressive enhancement, stored preferences, navigation slider, toast, finite hero sequence.

Phase 3: functional widget drag/lock/collapse/snap, animated FAQ and contextual pressure panel, real release configuration and pending state, staged feature/calendar entries.

Phase 4: browser / keyboard / fallback / reduced-motion / 320px overflow checks; screenshots, interaction frames, sizes and measured performance. Report unavailable metrics honestly.

Conflict resolution: only transform/opacity/clip-path motion takes precedence over requested background-position shimmer and animated header blur/color. Heatmap scale begins at .96 to obey the explicit .96–1 bound. FAQ and widget use grid expansion as explicitly allowed. Strong accent decoration is preserved separately from text.
