---
name: "openra-map-editing"
description: "Edits OpenRA maps and rules, including unpacking .oramap archives and patching map.yaml/rules.yaml. Invoke when modifying OpenRA map content, spawns, players, or victory rules."
---

# OpenRA Map Editing

Use this skill when working on OpenRA maps in this workspace, especially when the task involves changing initial units, player relationships, victory logic, map-local rules, or converting between `.oramap` packages and directory maps.

## When To Invoke

Invoke this skill when:
- the user asks to modify an OpenRA map
- the task involves `.oramap`, `map.yaml`, `rules.yaml`, `map.bin`, or `map.png`
- the user wants to change spawns, factions, starting units, map-local rules, or win/loss behavior
- a packaged map needs to be inspected or converted into an editable directory map

Do not invoke this skill for general game logic outside map assets unless the change is specifically map-local.

## Core Mental Model

An OpenRA map may appear in two common forms:

1. A packaged map file such as `.oramap`
2. A directory map containing files such as:
   - `map.yaml`
   - `rules.yaml`
   - `map.bin`
   - `map.png`
   - optional scripts like `.lua`

In this workspace, `.oramap` can behave like a zip archive. This means you can often inspect and edit it by unpacking it into a directory map, then modifying the text files there.

## Practical Workflow

### 1. Inspect the target map format

- If the user gives a directory map, inspect `map.yaml` and `rules.yaml` first.
- If the user gives a `.oramap`, treat it as a packaged archive and inspect its contents.

Important:
- Windows archive tools may reject the `.oramap` extension directly.
- A reliable workaround is to copy the file to a temporary `.zip` file, then extract it.
- This is unpacking, not regenerating map assets.

## 2. Understand which files do what

### `map.yaml`

Typically contains:
- map metadata
- player definitions
- player relationships
- actor placements
- spawn markers
- initial units and structures already placed on the map

Typical edits:
- change `Enemies`
- adjust `PlayerReference@...`
- remove or add placed units
- change ownership of actors
- modify starting positions

### `rules.yaml`

Typically contains map-local rule overrides.

Use it to:
- disable automatic starting units
- disable buildability for specific actors such as `MCV`
- change default cash
- lock map options
- override victory-related behavior when a map-local change is enough

### `map.bin`

Binary terrain or map data.

Do not attempt manual editing unless the user explicitly wants binary-level work and a proper tool is available.

### `map.png`

Preview image for the map.

Usually keep it as-is unless the user explicitly wants preview regeneration.

## 3. Preferred editing strategy

For rule changes, prefer changing text files only:
- `map.yaml`
- `rules.yaml`

Only touch `map.bin` or `map.png` when terrain or visual preview must change and a proper engine/editor workflow is available.

## 4. Common OpenRA map tasks

### Disable default starting units

Map-local `rules.yaml` can override world behavior:

```yaml
World:
	-SpawnStartingUnits:
```

This is useful when the map should use only units explicitly placed in `map.yaml`.

In this workspace, disabling automatic starts was more reliable when both of these were removed:

```yaml
World:
	-SpawnStartingUnits:
	-MapStartingLocations:
```

This mirrors how campaign-style maps avoid default multiplayer spawn behavior.

### Prevent base-building starts

Disable `MCV` buildability in map-local `rules.yaml`:

```yaml
MCV:
	Buildable:
		Prerequisites: ~disabled
```

Also consider setting:

```yaml
Player:
	PlayerResources:
		DefaultCash: 0
```

### Fix player hostility

Check player references in `map.yaml`.

For direct PvP or symmetric combat training, ensure each playable player lists the other as an enemy. Do not assume the current map setup is correct.

### Convert “destroy buildings” style maps into unit-only skirmish maps

A practical low-risk approach is:
- remove automatic starting units
- give players only pre-placed combat units
- avoid placing player-owned structures
- ensure players are enemies

With this setup, the default conquest logic often effectively becomes elimination of the remaining combat forces.

If the user needs stricter behavior, inspect whether map-local overrides are needed for `ConquestVictoryConditions` or actor `MustBeDestroyed` behavior.

### Ensure map-local rules actually load

Adding a `rules.yaml` file is not enough by itself.  
Make sure the map references it from `map.yaml`.

Example:

```yaml
Rules: rules.yaml
```

Without this, the engine may continue using default multiplayer behavior, which can cause symptoms like:
- players still receiving an MCV at match start
- default spawn handling still applying
- map-local overrides appearing to have no effect

## 5. Validation Checklist

After editing:
- verify `map.yaml` parses cleanly
- verify `rules.yaml` parses cleanly
- confirm the directory contains the expected files
- confirm playable players have correct enemy relationships
- confirm the map no longer relies on automatic starting units if the scenario is unit-only
- confirm no accidental duplicate packaged and directory versions exist unless intentional

## 6. Safe Packaging Guidance

When a packaged `.oramap` is inconvenient to edit:
- unpack it into a directory map
- keep `map.bin` and `map.png`
- edit `map.yaml` and `rules.yaml`
- prefer the directory map for iterative development

Do not claim that you regenerated `map.bin` or `map.png` unless an actual engine/editor export step was run.

## 7. Workspace-Specific Lessons

In this workspace, a successful pattern was:
- copy `.oramap` to a temporary `.zip`
- extract the archive
- inspect `map.yaml`
- add or adjust `rules.yaml`
- convert the map into a directory map under the mod’s `maps` directory

Another important lesson from this workspace:
- if a map still spawns an MCV after adding `rules.yaml`, first check whether `map.yaml` includes `Rules: rules.yaml`
- for unit-only training maps, removing both `SpawnStartingUnits` and `MapStartingLocations` is safer than removing only one
- reference maps such as `copilot-fin` may work because they inherit campaign-style rule packs that already disable default spawn systems

This is a practical editing workflow for AI-assisted map modification.

## 8. Response Guidance

When explaining your changes to the user:
- clearly distinguish unpacking from asset generation
- state whether `map.bin` and `map.png` already existed in the package
- explain which gameplay behavior comes from `map.yaml` versus `rules.yaml`
- mention any assumptions about victory logic or unit-only setups
