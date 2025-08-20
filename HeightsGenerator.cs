using System.Collections;
using System.Collections.Generic;
using UnityEngine;
// using Unity NavMesh
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine.AI;


[ExecuteInEditMode]
public class HeightsGenerator : MonoBehaviour
{
    public float worldScale = 6000f;
    public float worldDepthDivider = 1000f;
    public float mapSize = 10000f; // 3D size of each terrain tile
    public int resolution = 257;   // Heightmap resolution
    public float worldOffsetX = 0f;
    public float worldOffsetY = 0f;
    // New: Base elevation offset so terrain isn't stuck at sea level.
    [Tooltip("Raise the entire terrain by this normalized amount (0-1) so it's not at sea level.")]
    public float baseElevation = 0.2f;

    // New: Edge falloff for beach-like terrain at the borders.
    [Tooltip("Normalized distance from the edge (0-1) within which the terrain falls off.")]
    public float edgeFalloff = 0.3f;
    [Tooltip("Exponent to control the smoothness of the edge falloff. Values < 1 make it more gradual.")]
    public float edgeFalloffExponent = 0.5f;

    // Define your noise iterations to shape the terrain (mountains, valleys, flat areas).
    public TerrainIteration[] iterations;

    // Optionally, assign Terrain objects; if empty, the script finds them automatically.
    public Terrain[] terrains;

    /// <summary>
    /// Generates heightmaps for all assigned (or found) Terrain objects using the provided seed.
    /// </summary>
    public void Generate(int seed)
    {
        // Create a seeded random generator.
        System.Random prng = new System.Random(seed);
        // Generate random offsets based on the seed.
        float seedOffsetX = (float)prng.NextDouble() * 10000f;
        float seedOffsetY = (float)prng.NextDouble() * 10000f;

        // If no terrains are assigned, find all Terrain objects in the scene.
        if (terrains == null || terrains.Length == 0)
        {
            terrains = GameObject.FindObjectsOfType<Terrain>();
            if (terrains.Length == 0)
            {
                Debug.LogWarning("No Terrain objects found in the scene.");
                return;
            }
        }

        // Calculate the total depth (for normalization) from all enabled iterations.
        int maxTerrainDepth = 0;
        foreach (TerrainIteration iter in iterations)
        {
            if (iter.isUsed)
                maxTerrainDepth += iter.depth;
        }

        // Loop through each Terrain and generate its heightmap.
        foreach (Terrain terrain in terrains)
        {
            if (terrain == null)
                continue;

            TerrainData terrainData = terrain.terrainData;
            terrainData.heightmapResolution = resolution;
            // Set the terrain size (x, y, z) based on the map size and scaled depth.
            terrainData.size = new Vector3(mapSize, maxTerrainDepth * worldScale / worldDepthDivider, mapSize);

            float[,] heights = new float[resolution, resolution];

            // Sum contributions from each active noise iteration.
            for (int i = 0; i < iterations.Length; i++)
            {
                TerrainIteration iter = iterations[i];
                if (!iter.isUsed)
                    continue;
                for (int x = 0; x < resolution; x++)
                {
                    for (int y = 0; y < resolution; y++)
                    {
                        heights[x, y] += CalculateHeight(x, y, iter, terrain.transform.position, resolution, maxTerrainDepth, seedOffsetX, seedOffsetY);
                    }
                }
            }

            // Add base elevation so the terrain is raised above sea level.
            for (int x = 0; x < resolution; x++)
            {
                for (int y = 0; y < resolution; y++)
                {
                    heights[x, y] = Mathf.Clamp01(heights[x, y] + baseElevation);
                }
            }


            // Apply edge falloff so the terrain "falls" like a beach at the borders.
            for (int x = 0; x < resolution; x++)
            {
                for (int y = 0; y < resolution; y++)
                {
                    // Normalize coordinates (0 at left/bottom, 1 at right/top).
                    float nx = (float)x / (resolution - 1);
                    float ny = (float)y / (resolution - 1);
                    // Determine the minimum distance to any edge.
                    float distanceToEdge = Mathf.Min(nx, 1f - nx, ny, 1f - ny);
                    float falloffMultiplier = 1f;
                    if (distanceToEdge < edgeFalloff)
                    {
                        // Apply an exponent to control the smoothness.
                        float t = Mathf.Pow(distanceToEdge / edgeFalloff, edgeFalloffExponent);
                        falloffMultiplier = Mathf.SmoothStep(0f, 1f, t);
                    }
                    heights[x, y] *= falloffMultiplier;
                }
            }

            terrainData.SetHeights(0, 0, heights);
        }

    }

    // Calculates the height contribution at a given vertex using fractal Perlin noise and seed offsets.
    float CalculateHeight(int x, int y, TerrainIteration iteration, Vector3 worldPos, int resolution, int maxTerrainDepth, float seedOffsetX, float seedOffsetY)
    {
        // Offset based on the terrain's position.
        float worldPosOffsetX = worldPos.z / worldScale;
        float worldPosOffsetY = worldPos.x / worldScale;

        // Compute the initial noise coordinates and include seed offsets.
        float xCoord = ((float)x / (resolution - 1)) * mapSize / worldScale + iteration.offsetX + worldOffsetX + worldPosOffsetX + seedOffsetX;
        float yCoord = ((float)y / (resolution - 1)) * mapSize / worldScale + iteration.offsetY + worldOffsetY + worldPosOffsetY + seedOffsetY;

        // Multi-octave fractal noise calculation.
        float amplitude = 1f;
        float frequency = 1f;
        float noise = 0f;
        float totalAmplitude = 0f;
        for (int o = 0; o < iteration.octaves; o++)
        {
            float sampleX = xCoord * frequency;
            float sampleY = yCoord * frequency;
            float perlinValue = Mathf.PerlinNoise(sampleX, sampleY);
            noise += perlinValue * amplitude;
            totalAmplitude += amplitude;
            amplitude *= iteration.persistence;
            frequency *= iteration.lacunarity;
        }
        noise /= totalAmplitude; // Normalize the noise to 0-1 range.

        // Shape the noise using the provided AnimationCurve.
        noise = iteration.depthScaling.Evaluate(noise * iteration.rarity - (1f - 1f / iteration.rarity) * iteration.rarity);

        // Scale by the iteration's depth, normalized by the total depth.
        return noise * iteration.depth / (float)maxTerrainDepth;
    }


#if UNITY_EDITOR
    [CustomEditor(typeof(HeightsGenerator))]
    public class HeightGeneratorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            HeightsGenerator generator = (HeightsGenerator)target;
            if (GUILayout.Button("Generate Heightmaps"))
            {
                generator.Generate(Random.Range(0, 100000));
            }
        }
    }
#endif

    [System.Serializable]
    public class TerrainIteration
    {
        public string name = "Iteration";
        public bool isUsed = true;
        public AnimationCurve depthScaling = AnimationCurve.Linear(0, 0, 1, 1);
        public int depth = 20;
        public float scale = 20f;
        public float rarity = 1f;
        public float offsetX = 100f;
        public float offsetY = 100f;
        public float distortionX = 1.0f;
        public float distortionY = 1.0f;

        // Fractal noise parameters:
        // Octaves: number of layers of noise.
        // Lacunarity: frequency multiplier for each successive octave.
        // Persistence: amplitude multiplier for each successive octave.
        public int octaves = 4;
        public float lacunarity = 2f;
        public float persistence = 0.5f;
    }
}