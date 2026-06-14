using UnityEngine;
using UnityEditor;
using UnityEditorInternal;
using System.Collections.Generic;
using System.Linq;

// ─────────────────────────────────────────────────────────────────────────────
// Rule definition
// ─────────────────────────────────────────────────────────────────────────────

[System.Serializable]
public class TerrainPaintRule
{
    public string      name        = "New Rule";
    public Color       editorColor = Color.white;
    public bool        foldout     = true;

    public TerrainLayer terrainLayer = null;

    // Height rule — stored in world-space meters, matching the active height range
    public float heightMin     = 0f;
    public float heightMax     = 10000f;
    public float heightFalloff = 0f;

    // Slope rule (degrees 0–90)
    [Range(0f, 90f)]  public float slopeMin      = 0f;
    [Range(0f, 90f)]  public float slopeMax      = 90f;
    [Range(0f, 45f)]  public float slopeFalloff  = 5f;
}

// ─────────────────────────────────────────────────────────────────────────────
// Height range mode
// ─────────────────────────────────────────────────────────────────────────────

public enum HeightRangeMode { Absolute, LocalSample, GlobalSample }

// ─────────────────────────────────────────────────────────────────────────────
// Editor Window
// ─────────────────────────────────────────────────────────────────────────────

public class TerrainAutoPainter : EditorWindow
{
    // ── Height range state (window-level, not per rule) ───────────────────────
    [SerializeField] private HeightRangeMode heightMode      = HeightRangeMode.Absolute;
    [SerializeField] private float           sampledHeightMin = 0f;
    [SerializeField] private float           sampledHeightMax = 10000f;
    private bool hasSample = false;

    // Absolute ceiling — Unity's default max terrain height
    private const float AbsoluteMax = 10000f;

    // Convenience: the slider range in effect right now
    private float ActiveMin => heightMode == HeightRangeMode.Absolute ? 0f        : sampledHeightMin;
    private float ActiveMax => heightMode == HeightRangeMode.Absolute ? AbsoluteMax : sampledHeightMax;

    [SerializeField] private List<TerrainPaintRule> rules   = new List<TerrainPaintRule>();
    [SerializeField] private List<Terrain>          targets = new List<Terrain>();

    private ReorderableList  terrainList;
    private SerializedObject serializedSelf;
    private Vector2          scroll;

    [MenuItem("Tools/Terrain Auto Painter")]
    public static void Open()
    {
        var win = GetWindow<TerrainAutoPainter>("Terrain Auto Painter");
        win.minSize = new Vector2(440f, 500f);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        serializedSelf = new SerializedObject(this);
        BuildTerrainList();
    }

    private void BuildTerrainList()
    {
        var prop = serializedSelf.FindProperty("targets");
        terrainList = new ReorderableList(serializedSelf, prop,
            draggable: true, displayHeader: true,
            displayAddButton: true, displayRemoveButton: true);

        terrainList.drawHeaderCallback = r =>
            EditorGUI.LabelField(r, "Target Terrain Tiles");

        terrainList.elementHeight = EditorGUIUtility.singleLineHeight + 4f;

        terrainList.drawElementCallback = (rect, index, active, focused) =>
        {
            var elemProp = prop.GetArrayElementAtIndex(index);
            EditorGUI.PropertyField(
                new Rect(rect.x, rect.y + 2f, rect.width, EditorGUIUtility.singleLineHeight),
                elemProp, GUIContent.none);
        };

        terrainList.onAddCallback = list =>
        {
            targets.Add(null);
            serializedSelf.Update();
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GUI
    // ─────────────────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        serializedSelf.Update();
        scroll = EditorGUILayout.BeginScrollView(scroll);

        // ── Target terrains ───────────────────────────────────────────────────
        EditorGUILayout.Space(6f);
        terrainList.DoLayoutList();

        // ── Add / Remove all scene terrains ──────────────────────────────────
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Add All Scene Terrains"))
            {
                foreach (var t in FindObjectsByType<Terrain>(FindObjectsSortMode.None))
                    if (!targets.Contains(t))
                        targets.Add(t);
                serializedSelf.Update();
            }
            if (GUILayout.Button("Remove All"))
            {
                targets.Clear();
                serializedSelf.Update();
            }
        }

        // ── Height range mode ─────────────────────────────────────────────────
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Height Range", EditorStyles.boldLabel);

        heightMode = (HeightRangeMode)EditorGUILayout.EnumPopup("Mode", heightMode);

        if (heightMode == HeightRangeMode.Absolute)
        {
            EditorGUILayout.HelpBox(
                "Sliders use 0 – 10,000 m (Unity absolute). Rules are portable across any terrain.",
                MessageType.None);
        }
        else
        {
            // Sample buttons
            bool localReady = HasValidTargets();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUI.enabled = localReady;
                if (GUILayout.Button("Sample Local Tiles"))
                    DoSample(globalScan: false);
                GUI.enabled = true;

                if (GUILayout.Button("Sample All Scene Terrains"))
                {
                    if (EditorUtility.DisplayDialog("Global Sample",
                        "This scans every Terrain in the scene and may be slow on large worlds. Continue?",
                        "Sample", "Cancel"))
                        DoSample(globalScan: true);
                }
            }

            if (!localReady && heightMode == HeightRangeMode.LocalSample)
                EditorGUILayout.HelpBox("Add target tiles before sampling locally.", MessageType.Warning);

            if (hasSample)
            {
                EditorGUILayout.HelpBox(
                    $"Sampled range: {sampledHeightMin:F1} m – {sampledHeightMax:F1} m",
                    MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox("No sample taken yet. Run a sample to set the slider range.", MessageType.Warning);
            }
        }

        // ── Rules ─────────────────────────────────────────────────────────────
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Paint Rules  (top = lowest priority)", EditorStyles.boldLabel);
        EditorGUILayout.Space(2f);

        for (int i = 0; i < rules.Count; i++)
            DrawRule(i);

        EditorGUILayout.Space(4f);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Add Rule"))
            {
                rules.Add(new TerrainPaintRule
                {
                    name      = $"Rule {rules.Count}",
                    heightMin = ActiveMin,
                    heightMax = ActiveMax
                });
            }
            GUILayout.FlexibleSpace();
        }

        // ── Actions ───────────────────────────────────────────────────────────
        EditorGUILayout.Space(10f);
        bool canPaint = HasValidTargets() && HasValidRules()
                     && (heightMode == HeightRangeMode.Absolute || hasSample);
        GUI.enabled = canPaint;
        if (GUILayout.Button("Paint Selected Terrains", GUILayout.Height(30f)))
            PaintAll();
        GUI.enabled = true;

        if (!HasValidTargets())
            EditorGUILayout.HelpBox("Add at least one target terrain tile.", MessageType.Info);
        else if (!HasValidRules())
            EditorGUILayout.HelpBox("Each rule needs a TerrainLayer asset assigned.", MessageType.Warning);
        else if (heightMode != HeightRangeMode.Absolute && !hasSample)
            EditorGUILayout.HelpBox("Run a sample before painting.", MessageType.Warning);

        EditorGUILayout.Space(6f);
        EditorGUILayout.EndScrollView();
        serializedSelf.ApplyModifiedProperties();

        if (GUI.changed)
            Repaint();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Rule drawer
    // ─────────────────────────────────────────────────────────────────────────

    private void DrawRule(int index)
    {
        var rule = rules[index];

        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            rule.editorColor = EditorGUILayout.ColorField(
                GUIContent.none, rule.editorColor,
                showEyedropper: false, showAlpha: false, hdr: false,
                GUILayout.Width(30f));

            rule.foldout = EditorGUILayout.Foldout(rule.foldout, $"{index}: {rule.name}", toggleOnLabelClick: true);

            GUILayout.FlexibleSpace();

            if (index > 0 && GUILayout.Button("▲", EditorStyles.toolbarButton, GUILayout.Width(22f)))
            {
                (rules[index], rules[index - 1]) = (rules[index - 1], rules[index]);
                return;
            }
            if (index < rules.Count - 1 && GUILayout.Button("▼", EditorStyles.toolbarButton, GUILayout.Width(22f)))
            {
                (rules[index], rules[index + 1]) = (rules[index + 1], rules[index]);
                return;
            }
            if (GUILayout.Button("✕", EditorStyles.toolbarButton, GUILayout.Width(22f)))
            {
                rules.RemoveAt(index);
                return;
            }
        }

        if (!rule.foldout) return;

        using (new EditorGUI.IndentLevelScope())
        {
            rule.name = EditorGUILayout.TextField("Name", rule.name);

            EditorGUILayout.Space(4f);

            EditorGUILayout.LabelField("Terrain Layer", EditorStyles.boldLabel);
            rule.terrainLayer = (TerrainLayer)EditorGUILayout.ObjectField(
                "Layer Asset", rule.terrainLayer, typeof(TerrainLayer), false);

            EditorGUILayout.Space(4f);

            // Height rule — slider range driven by active mode
            float rangeMin = ActiveMin;
            float rangeMax = ActiveMax;

            string heightLabel = heightMode == HeightRangeMode.Absolute
                ? "Height Rule  (meters, 0 – 10,000)"
                : $"Height Rule  (meters, {rangeMin:F1} – {rangeMax:F1})";
            EditorGUILayout.LabelField(heightLabel, EditorStyles.boldLabel);

            rule.heightMin     = EditorGUILayout.FloatField("Min (m)", rule.heightMin);
            rule.heightMax     = EditorGUILayout.FloatField("Max (m)", rule.heightMax);
            rule.heightFalloff = EditorGUILayout.Slider("Falloff", rule.heightFalloff, 0f, 1f);

            EditorGUILayout.Space(4f);

            EditorGUILayout.LabelField("Slope Rule  (degrees)", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                float sMin = rule.slopeMin, sMax = rule.slopeMax;
                EditorGUILayout.MinMaxSlider(ref sMin, ref sMax, 0f, 90f);
                EditorGUILayout.LabelField($"{sMin:F1}°–{sMax:F1}°", GUILayout.Width(90f));
                rule.slopeMin = sMin;
                rule.slopeMax = sMax;
            }
            rule.slopeFalloff = EditorGUILayout.Slider("Falloff (°)", rule.slopeFalloff, 0f, 45f);

            EditorGUILayout.Space(6f);
        }

        var divRect = EditorGUILayout.GetControlRect(false, 1f);
        EditorGUI.DrawRect(divRect, new Color(0.5f, 0.5f, 0.5f, 0.35f));
        EditorGUILayout.Space(4f);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Sampling
    // ─────────────────────────────────────────────────────────────────────────

    private void DoSample(bool globalScan)
    {
        IEnumerable<Terrain> pool = globalScan
            ? FindObjectsByType<Terrain>(FindObjectsSortMode.None)
            : targets.Where(t => t != null);

        float foundMin = float.MaxValue;
        float foundMax = float.MinValue;
        int   count    = 0;

        try
        {
            var terrainArray = pool.ToArray();
            for (int t = 0; t < terrainArray.Length; t++)
            {
                var terrain = terrainArray[t];
                EditorUtility.DisplayProgressBar("Sampling Terrain Heights",
                    terrain.name, (float)t / terrainArray.Length);

                TerrainData data  = terrain.terrainData;
                int         hmRes = data.heightmapResolution;
                float       baseY = terrain.transform.position.y;

                // Sample every heightmap texel
                float[,] heights = data.GetHeights(0, 0, hmRes, hmRes);
                float terrH = data.size.y;

                for (int row = 0; row < hmRes; row++)
                for (int col = 0; col < hmRes; col++)
                {
                    float worldH = baseY + heights[row, col] * terrH;
                    if (worldH < foundMin) foundMin = worldH;
                    if (worldH > foundMax) foundMax = worldH;
                }
                count++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (count == 0)
        {
            Debug.LogWarning("[TerrainAutoPainter] No terrains found to sample.");
            return;
        }

        // Add a small margin so nothing at the exact extremes gets clipped
        float margin = (foundMax - foundMin) * 0.01f;
        sampledHeightMin = Mathf.Floor(foundMin - margin);
        sampledHeightMax = Mathf.Ceil(foundMax  + margin);
        hasSample        = true;

        Debug.Log($"[TerrainAutoPainter] Sampled {count} terrain(s): {sampledHeightMin:F1} m – {sampledHeightMax:F1} m");
        Repaint();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Painting
    // ─────────────────────────────────────────────────────────────────────────

    private void PaintAll()
    {
        var layerAssets = new TerrainLayer[rules.Count];
        for (int i = 0; i < rules.Count; i++)
            layerAssets[i] = rules[i].terrainLayer;

        // Pass the active height range into the paint step so normalization is consistent
        float hMin = ActiveMin;
        float hMax = ActiveMax;

        try
        {
            for (int t = 0; t < targets.Count; t++)
            {
                if (targets[t] == null) continue;
                EditorUtility.DisplayProgressBar("Terrain Auto Painter",
                    $"Painting {targets[t].name}  ({t + 1}/{targets.Count})",
                    (float)t / targets.Count);
                PaintTerrain(targets[t], layerAssets, rules, hMin, hMax);
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[TerrainAutoPainter] Done. {targets.Count} tile(s), {rules.Count} rule(s).");
    }

    private static void PaintTerrain(Terrain terrain, TerrainLayer[] layerAssets,
                                     List<TerrainPaintRule> rules, float hRangeMin, float hRangeMax)
    {
        TerrainData data       = terrain.terrainData;
        int         layerCount = layerAssets.Length;
        int         aw         = data.alphamapWidth;
        int         ah         = data.alphamapHeight;
        int         hmRes      = data.heightmapResolution;
        float       terrainH   = data.size.y;
        float       baseY      = terrain.transform.position.y;
        float       hSpan      = hRangeMax - hRangeMin;

        Undo.RegisterCompleteObjectUndo(data, "Terrain Auto Paint");

        data.terrainLayers = new TerrainLayer[0];
        data.terrainLayers = layerAssets;

        float[,,] alphamap = new float[ah, aw, layerCount];
        float[]   weights  = new float[layerCount];

        for (int ay = 0; ay < ah; ay++)
        {
            for (int ax = 0; ax < aw; ax++)
            {
                float nx = (float)ax / (aw - 1);
                float nz = (float)ay / (ah - 1);

                // World-space height of this point
                float worldH = baseY + data.GetHeight(
                    Mathf.RoundToInt(nx * (hmRes - 1)),
                    Mathf.RoundToInt(nz * (hmRes - 1)));

                float slope = data.GetSteepness(nx, nz);

                float total = 0f;
                for (int r = 0; r < layerCount; r++)
                {
                    // EvaluateRule now receives world-space height and the active range,
                    // so rule thresholds in meters map directly without any extra normalization
                    weights[r] = EvaluateRule(rules[r], worldH, slope, hRangeMin, hSpan);
                    total += weights[r];
                }

                if (total <= 0f) { weights[layerCount - 1] = 1f; total = 1f; }

                for (int r = 0; r < layerCount; r++)
                    alphamap[ay, ax, r] = weights[r] / total;
            }
        }

        data.SetAlphamaps(0, 0, alphamap);
        EditorUtility.SetDirty(data);
    }

    private static float EvaluateRule(TerrainPaintRule rule, float worldH, float slope,
                                      float hRangeMin, float hSpan)
    {
        // heightFalloff is 0-1, scale it to meters using the active height span
        float hFalloffMeters = rule.heightFalloff * hSpan;
        float hWeight = RangeWeight(worldH, rule.heightMin, rule.heightMax, hFalloffMeters);
        float sWeight = RangeWeight(slope,  rule.slopeMin,  rule.slopeMax,  rule.slopeFalloff);
        return hWeight * sWeight;
    }

    private static float RangeWeight(float value, float min, float max, float falloff)
    {
        if (value < min - falloff || value > max + falloff) return 0f;
        if (falloff <= 0f) return (value >= min && value <= max) ? 1f : 0f;
        float lower = Mathf.SmoothStep(0f, 1f, (value - (min - falloff)) / falloff);
        float upper = Mathf.SmoothStep(0f, 1f, ((max + falloff) - value) / falloff);
        return Mathf.Min(lower, upper);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Validation
    // ─────────────────────────────────────────────────────────────────────────

    private bool HasValidTargets() => targets.Count > 0 && targets.Exists(t => t != null);
    private bool HasValidRules()   => rules.Count  > 0 && rules.Exists(r => r.terrainLayer != null);
}
