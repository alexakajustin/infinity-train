using UnityEditor;
using UnityEngine;

[ExecuteInEditMode]
public class HeightsGenerator : MonoBehaviour
{
    public float worldScale = 6000f;
    public float worldDepthDivider = 1000f;
    public float mapSize = 10000f; // 3D size of each terrain tile
    public int resolution = 257;   // Heightmap resolution
    public float worldOffsetX = 0f;
    public float worldOffsetY = 0f;

    [Tooltip("Raise the entire terrain by this normalized amount (0-1) so it's not at sea level.")]
    public float baseElevation = 0.2f;

    public TerrainIteration[] iterations;
    public Terrain terrain;
    public Erosion erosion;

    public bool hasFinishedGeneration = false;
    private void Start()
    {
        terrain = GetComponent<Terrain>();
        erosion = GetComponent<Erosion>();
        int seed = Random.Range(0, 999999);
        terrain.terrainData = new UnityEngine.TerrainData();
        terrain.Flush();
        Generate(seed);
        Debug.Log("Heightmap generated with seed: " + seed);
    }

    public void Generate(int seed)
    {
        System.Random prng = new System.Random(seed);
        int maxTerrainDepth = 0;
        foreach (TerrainIteration iter in iterations)
        {
            if (iter.isUsed)
                maxTerrainDepth += iter.depth;
        }

        UnityEngine.TerrainData terrainData = terrain.terrainData;
        terrainData.heightmapResolution = resolution;
        terrainData.size = new Vector3(mapSize, maxTerrainDepth * worldScale / worldDepthDivider, mapSize);

        float[,] heights = new float[resolution, resolution];

        // Generate noise-based terrain
        for (int i = 0; i < iterations.Length; i++)
        {
            TerrainIteration iter = iterations[i];
            if (!iter.isUsed) continue;

            // Generate unique offsets per iteration for more seed-based variation
            float seedOffsetX = (float)prng.NextDouble() * 10000f;
            float seedOffsetY = (float)prng.NextDouble() * 10000f;

            for (int x = 0; x < resolution; x++)
            {
                for (int y = 0; y < resolution; y++)
                {
                    heights[x, y] += CalculateHeight(x, y, iter, terrain.transform.position,
                                                     resolution, maxTerrainDepth,
                                                     seedOffsetX, seedOffsetY);
                }
            }
        }

        // Add base elevation
        for (int x = 0; x < resolution; x++)
        {
            for (int y = 0; y < resolution; y++)
            {
                heights[x, y] = Mathf.Clamp01(heights[x, y] + baseElevation);
            }
        }

        // === Edge falloff mask (smoothly flatten edges) ===
        for (int x = 0; x < resolution; x++)
        {
            for (int y = 0; y < resolution; y++)
            {
                float edgeX = Mathf.Min((float)x / (resolution - 1), 1f - (float)x / (resolution - 1));
                float edgeY = Mathf.Min((float)y / (resolution - 1), 1f - (float)y / (resolution - 1));
                float edgeFactor = Mathf.Min(edgeX, edgeY) * 2f;

                float falloff = Mathf.SmoothStep(0, 1, edgeFactor);

                heights[x, y] = Mathf.Lerp(baseElevation, heights[x, y], falloff);
            }
        }

        // === Apply erosion if component is attached ===
        if (erosion != null)
        {
            float[] flatHeights = FlattenHeights(heights, resolution);

            // Number of droplets to simulate (adjust for performance/quality tradeoff)
            int numIterations = 200000;
            erosion.Erode(flatHeights, resolution, numIterations, true);

            heights = UnflattenHeights(flatHeights, resolution);
        }

        terrain.terrainData.SetHeights(0, 0, heights);
        terrain.Flush();

        // Ensure collider is updated
        TerrainCollider terrainCollider = terrain.GetComponent<TerrainCollider>();
        if (terrainCollider == null) terrainCollider = terrain.gameObject.AddComponent<TerrainCollider>();
        terrainCollider.terrainData = terrain.terrainData;

        hasFinishedGeneration = true;
    }

    float CalculateHeight(int x, int y, TerrainIteration iteration, Vector3 worldPos,
                          int resolution, int maxTerrainDepth,
                          float seedOffsetX, float seedOffsetY)
    {
        float worldPosOffsetX = worldPos.z / worldScale;
        float worldPosOffsetY = worldPos.x / worldScale;

        float xCoord = ((float)x / (resolution - 1)) * mapSize / worldScale +
                        iteration.offsetX + worldOffsetX + worldPosOffsetX + seedOffsetX;

        float yCoord = ((float)y / (resolution - 1)) * mapSize / worldScale +
                        iteration.offsetY + worldOffsetY + worldPosOffsetY + seedOffsetY;

        float amplitude = 1f;
        float frequency = 1f / iteration.scale; // Use 'scale' to control initial feature size (smaller scale = larger features)
        float noise = 0f;
        float totalAmplitude = 0f;

        for (int o = 0; o < iteration.octaves; o++)
        {
            float sampleX = xCoord * frequency * iteration.distortionX; // Apply distortion for more variety
            float sampleY = yCoord * frequency * iteration.distortionY;
            float perlinValue = Mathf.PerlinNoise(sampleX, sampleY);
            noise += perlinValue * amplitude;
            totalAmplitude += amplitude;
            amplitude *= iteration.persistence;
            frequency *= iteration.lacunarity;
        }
        noise /= totalAmplitude;

        noise = iteration.depthScaling.Evaluate(noise * iteration.rarity - (1f - 1f / iteration.rarity) * iteration.rarity);

        return noise * iteration.depth / (float)maxTerrainDepth;
    }

    // === Helpers for erosion ===
    float[] FlattenHeights(float[,] heights, int resolution)
    {
        float[] flat = new float[resolution * resolution];
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                flat[y * resolution + x] = heights[x, y];
            }
        }
        return flat;
    }

    float[,] UnflattenHeights(float[] flat, int resolution)
    {
        float[,] heights = new float[resolution, resolution];
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                heights[x, y] = flat[y * resolution + x];
            }
        }
        return heights;
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
                int seed = Random.Range(0, 100000);
                generator.Generate(seed);
                Debug.Log("Heightmap generated with seed: " + seed);
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

        public int octaves = 4;
        public float lacunarity = 2f;
        public float persistence = 0.5f;
    }
}
