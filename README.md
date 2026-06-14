# Terrain Auto Painter

A Unity 6 editor tool for painting terrain layers automatically based on height and slope rules.

---

## Installation

1. In your Unity project, locate or create the folder `Assets/Editor`.
2. Place `TerrainAutoPainter.cs` inside that folder.
3. Unity will compile it automatically. No additional dependencies required.

---

## Setup

Open the tool via **Tools → Terrain Auto Painter** in the Unity menu bar.

### Target Terrain Tiles

Add the terrain tiles you want to paint using the list at the top of the window. You can drag tiles in directly or use:

- **Add All Scene Terrains** — populates the list with every Terrain object in the active scene.
- **Remove All** — clears the list.

### Height Range Mode

This setting controls what the height values in your rules mean. It applies globally across all rules.

**Absolute** — rules use raw world-space meters, 0 to 10,000. No setup needed. Rules are portable between projects and scenes.

**Local Sample** — scans only the terrain tiles in your target list and sets the height range to their actual min/max world-space heights. Use this when your rules should be relative to the tiles you are painting.

**Global Sample** — scans every Terrain in the scene. Useful for consistent rules across a large tiled world. This can be slow on large scenes; a confirmation dialog will warn you before it runs.

After sampling, the detected height range is displayed. Rule height values should be set within that range.

---

## Paint Rules

Each rule maps a TerrainLayer asset to a height and slope range. Rules are evaluated from top (lowest priority) to bottom (highest priority). Where rules overlap, weights are blended and normalized.

**To add a rule**, click **Add Rule** at the bottom of the rules list.

Each rule contains:

**Layer Asset** — drag in an existing `.terrainlayer` asset from your project. This asset carries all visual properties: albedo, normal map, tiling, smoothness, metallic, etc. The tool does not create or modify layer assets.

**Height Rule** — set Min and Max in meters. Falloff (0–1) controls the blend width at the edges of the range, scaled to the active height span.

**Slope Rule** — set Min and Max in degrees (0–90). Falloff (0–45°) controls edge blending.

Rules can be reordered with the ▲ ▼ buttons and removed with ✕.

---

## Painting

Click **Paint Selected Terrains**. For each target tile, the tool will:

1. Discard all existing terrain layer assignments and alphamap data.
2. Assign the layers from your rules.
3. Evaluate every alphamap texel against the rules and write the result.

Every paint is a full repaint from scratch. Undo is supported.
