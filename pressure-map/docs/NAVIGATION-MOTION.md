# Navigation and page motion

Reference inspected: local `Plain Craft Launcher 2` source supplied by the user.
No PCL executable was run; reference timings below come from source inspection.

- `Controls/MyListItem.xaml.vb`, `RefreshColor`: separate hover background,
  opacity 120 ms in / 180 ms out, scale 0.75 to 1 over 192 ms,
  pressed item scale 0.98.
- `Controls/MyPageLeft.vb`, `TriggerShowAnimation`: navigation items enter
  from x=-25, opacity over 100 ms, movement over 300 ms; stagger begins at
  30 ms and decreases by 2 ms per item.
- `Controls/MyPageRight.vb`, `TriggerEnterAnimation`: cards enter from y=-16,
  opacity over 100 ms, movement combines 5 px cubic-out over 250 ms and
  11 px back-out over 350 ms; each card starts 25 ms after the previous.
- `Modules/Base/ModAnimation.vb`: Fluent and Back easing definitions.

Adaptations requested by the user: retain the app's rounded light-purple
selection, omit PCL's vertical selection indicator, and add a subtle hover
lift/scale/shadow. Page headings remain fixed. Card delay is capped at 200 ms
so long settings pages remain immediately usable. Hidden items are never
waiting for a scroll-triggered reveal.

Implementation uses independent hover, pressed and startup transform layers.
Page motion animates individual cards rather than the whole content pane.
Layout-owned composition offsets are never animated. Cancellation restores
the original opacity and transform. The app/system reduced-motion preference
disables spatial motion.

Run focused checks with an isolated output folder (the normal development
output can be locked by an open app): build the app, then run its DLL with
`--ui-smoke --motion-smoke --data <new-empty-folder> --out <qa-folder>`.
Checks sample actual in-flight translation/opacity, rapid navigation,
restored geometry, hover/press surfaces, rounded corners and reduced motion.
