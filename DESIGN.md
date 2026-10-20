# Design System: OpenVoiceSpeak web manager

Character: a quiet, dense operations console in the OpenVoiceSpeak blue; the server's state is found at a glance, nothing else shouts.

Scope: the web manager (`src/OVS.Manager/wwwroot/app.css`). The desktop client keeps its own theme (`src/OVS.Client/Styles/Theme.axaml`); the colours below are taken from it, so the two read as one product.

## Color

Tokens in `:root`, again for dark under `prefers-color-scheme` and `[data-theme="dark"]`. Values are light / dark.

- `--background` #F4F5F7 / #111318, `--surface` #FFFFFF / #1E2128, `--surface-2` #EBEDF1 / #272B34, `--sidebar` #EBEDF1 / #17191F: the client's cool grey ramp, so manager and client share a temperature.
- `--text` #151922 / #E8EAEE, `--muted` #556070 / #9CA3AF: muted carries labels and secondary columns (6.38:1 on the light surface, 7.32:1 on the dark background).
- `--border` #D5D9E0 / #2B2F38, `--border-strong` #BEC4CE / #3A3F4A: the one separation device (see Shape).
- `--accent` #2F6FEB in both themes: the logo's blue, used as a fill only: the primary button, the brand mark, chart lines. White label 4.57:1; against the surface 4.57:1 (light) and 3.52:1 (dark).
- `--accent-text` #1D4FC4 / #7AA7FF: the accent as text (links, the current nav item, the update chip): 7.08:1 and 6.75:1. `#2F6FEB` as text on `--background` is only 4.19:1, so it is never used for text.
- `--accent-soft` #E3ECFE / #1B2A45: the plane behind the current nav item and info banners (accent text on it 5.96:1 and 6.01:1).
- `--danger` #B91C1C / #F87171, `--success` #15803D / #22C55E, `--warning` #92400E / #FBBF24, each with a `-soft` plane: semantic only (status dots, results, the danger zone, the "Alpha" badge beside the name), never decorative. All pass 4.5:1 as text.
- `--console-bg` #0B0D11, `--console-text` #D6DBE2: the log console stays dark in both themes, as a terminal does. `--qr-bg` #FFFFFF: a QR code needs white.

## Typography

- Body: the platform's UI face (`"Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif`). The manager runs on the operator's own machine; a bundled web font would add weight and no meaning.
- Machine values: `"Cascadia Mono", "JetBrains Mono", ui-monospace, Consolas, monospace` (`--font-mono`) for tile figures, code, the console and recovery codes: the second family marks "a value the system reports", not prose.
- Scale: 12 (small labels, table heads) / 13 (form labels, chips) / 14 (body) / 15 (h2) / 22 (h1), tile figures 24 in mono. Deliberately low size contrast for a dense expert tool; the contrast lives in weight (400 body, 600 labels and h2, 700 h1) and in muted versus full text.

## Spacing

- Base 4px; tokens `--space-1` 4, `--space-2` 8, `--space-3` 12, `--space-4` 16, `--space-5` 24, `--space-6` 32.
- Rhythm: dense. Tight inside a group (8 to 12: fields, buttons, tiles), generous between sections (24), the page edge 32 (16 on a phone).

## Shape & elevation

- Radii: `--radius` 8px (cards, tables, tiles, banners), `--radius-small` 6px (controls, nav items), 999px only for pills (switches, chips).
- Separation language: 1px borders. No shadows on cards, tables or tiles; a plane of `--surface-2` or `--accent-soft` only marks the current or hovered item.

## Motion

- 120ms ease for hover and focus colour changes; none under `prefers-reduced-motion`.

## Components

- Button: primary (submit) = `--accent` fill, white label; secondary = surface with `--border-strong`; destructive (`.danger`) = `--danger` text and border, `--danger-soft` on hover. Pressed: 1px down. Disabled: 50 % opacity.
- Form: labels above fields, at most 7 fields open at once; the optional rest folds into a `details.form-more` whose summary names it and ends in "(freiwillig)", unfolded once it holds a value or the form failed. A password field sits in `.password-field` with the `PasswordShow` button ("Anzeigen") beside it; a new password is typed once, with no field to repeat it (except the backup password, which nobody types again until a restore). Two or three choices are radio buttons in a `.choice-group`, not a dropdown. Every submit button names its action ("Passwort ändern", "Zeitplan speichern"), never a bare "Speichern".
- Card: every page section; surface, 1px border, `--radius`, padding 20px, 24px apart.
- Table (`table.list`): surface, 1px border, uppercase 12px muted head on `--surface-2`, row hover on `--surface-2`.
- Tabs (`.server-nav`) and side nav: muted text; hover and current on a `--surface-2` plane (tabs) or `--accent-soft` with `--accent-text` (side nav).

## Voice

- German first (English second), sentence case, verbs on buttons ("Anlegen und starten", "Übernehmen").
