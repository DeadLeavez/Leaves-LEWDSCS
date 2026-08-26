# Leaves-LEWDS — Handoff

Last updated: 2026-07-15. Read `4.0 Mods\HANDOFF.md` first for project-wide rules.
("L.E.W.D.S." = Low End Weapon-spawn Drop-rate Searcher.)

---

## 1. What the mod does

Raises loose-loot spawn probabilities that fall **below a configured floor** — the near-zero
spawns (e.g. 0.0002) that effectively never appear in current SPT. Per-item and per-category
floors. It only ever raises, never lowers.

Single class, `src/LEWDS.cs` (~160 lines). `Config.cs` = `{ categories, items, logAtStartup }`.

## 2. Load order (the important part)

`[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 90000)]` — deliberately a huge
offset (still inside the PostDBModLoader stage, which ends at +100000) so LEWDS's `OnLoad`, and
therefore its `AddTransformer` registration, runs **dead last**, after LootFuckery's. Because
`LazyLoad<T>.Value` replays transformers in registration order, LEWDS's floor-raise is the
final word on top of whatever LootFuckery already multiplied.

Note: the readme's "load LEWDS before LootFuckery" line is **stale/contradictory** — the TS
original explicitly deferred its logic to run last, and this port preserves that. Trust the
code comment, not the readme.

## 3. How it works

For each playable map, it registers a transformer on `location.LooseLoot` that walks every
spawnpoint and calls `AdjustPoint`:
- Only the root item of each entry is considered (children skipped).
- A floor applies if the item's category id (`config.categories`) or the item id
  (`config.items`) has a non-zero entry. A floor explicitly set to `0` means "no floor" (falls
  through to the other) — the project-wide truthy-guard convention.
- The point's `Probability` is raised to the **highest** matching floor, only if that's above
  its current value.

`logAtStartup` opts into eagerly reading every map's loot at boot just to print the "Found N
spawns below floor" line immediately. This is a **real cost**, not a free toggle:
`LazyLoad<LooseLoot>.Value` re-deserializes each map's multi-MB loot JSON from disk on every
access (SPT 4.0 keeps nothing resident, unlike 3.x), so this parses 13 map files back-to-back.
Left off, the same log line appears the first time each map is naturally read (raid load).

## 4. Files

| File | Role |
|---|---|
| `src/LEWDS.cs` | Everything (§2–§3). |
| `src/Config.cs` | `categories` / `items` floor maps + `logAtStartup`. |
| `config/config.jsonc` | The floors. |
| `allItems.jsonc`, `looseLoot.json` | Reference data (item ids / loose-loot shape) for hand-editing the config — not read at runtime. |
