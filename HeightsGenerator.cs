using UnityEditor;
using UnityEngine;
using System.Threading.Tasks;
using System.Threading;
using System.Collections;

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

    [Tooltip("Number of chunks to divide the generation into for better performance")]
    public int processingChunks = 8;

    [Tooltip("Milliseconds to wait between chunks to prevent frame drops")]
    public int chunkDelayMs = 10;

    public TerrainIteration[] iterations;
    public Terrain terrain;
    public Erosion erosion;

    public bool hasFinishedGeneration = false;
    public bool isGenerating = false;

    // Progress tracking
    public float generationProgress = 0f;

    private CancellationTokenSource cancellationTokenSource;

    private void Start()
    {
        terrain = GetComponent<Terrain>();
        erosion = GetComponent<Erosion>();
        int seed = Random.Range(0, 999999);
        terrain.terrainData = new UnityEngine.TerrainData();
        StartCoroutine(GenerateAsync(seed));
        Debug.Log("Heightmap generation started with seed: " + seed);
    }

    public void Generate(int seed)
    {
        if (isGenerating)
        {
            Debug.LogWarning("Generation already in progress!");
            return;
        }

        StartCoroutine(GenerateAsync(seed));
    }

    public void CancelGeneration()
    {
        if (cancellationTokenSource != null)
        {
            cancellationTokenSource.Cancel();
            isGenerating = false;
            Debug.Log("Terrain generation cancelled.");
        }
    }

    private IEnumerator GenerateAsync(int seed)
    {
        isGenerating = true;
        hasFinishedGeneration = false;
        generationProgress = 0f;

        cancellationTokenSource?.Dispose();
        cancellationTokenSource = new CancellationTokenSource();
        var token = cancellationTokenSource.Token;

        var generationCoroutine = StartCoroutine(GenerateHeightmapCoroutine(seed, token));
        yield return generationCoroutine;

        if (token.IsCancellationRequested)
        {
            Debug.Log("Terrain generation was cancelled.");
        }

        isGenerating = false;
    }

    private IEnumerator GenerateHeightmapCoroutine(int seed, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            Debug.Log("Generation cancelled before starting.");
            yield break;
        }

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

        // Generate noise-based terrain with multithreading
        generationProgress = 0.1f;
        yield return StartCoroutine(GenerateNoiseHeights(heights, prng, maxTerrainDepth, token));

        if (token.IsCancellationRequested) yield break;

        // Add base elevation
        generationProgress = 0.6f;
        yield return StartCoroutine(ApplyBaseElevation(heights, token));

        if (token.IsCancellationRequested) yield break;

        // Apply edge falloff
        generationProgress = 0.7f;
        yield return StartCoroutine(ApplyEdgeFalloff(heights, token));

        if (token.IsCancellationRequested) yield break;

        // Apply erosion if component is attached
        if (erosion != null)
        {
            generationProgress = 0.8f;
            yield return StartCoroutine(ApplyErosion(heights, token));
        }

        if (token.IsCancellationRequested) yield break;

        // Apply final terrain data
        generationProgress = 0.9f;
        terrain.terrainData.SetHeights(0, 0, heights);

        // Ensure collider is updated
        TerrainCollider terrainCollider = terrain.GetComponent<TerrainCollider>();
        if (terrainCollider == null) terrainCollider = terrain.gameObject.AddComponent<TerrainCollider>();
        terrainCollider.terrainData = terrain.terrainData;

        hasFinishedGeneration = true;
        generationProgress = 1f;
        Debug.Log("Heightmap generation completed.");
    }

    private IEnumerator GenerateNoiseHeights(float[,] heights, System.Random prng, int maxTerrainDepth, CancellationToken token)
    {
        // Pre-calculate all Unity-dependent values on the main thread
        Vector3 terrainWorldPos = terrain.transform.position;

        // Pre-calculate iteration data to avoid repeated calculations
        var iterationData = new System.Collections.Generic.List<IterationData>();
        foreach (var iter in iterations)
        {
            if (!iter.isUsed) continue;

            iterationData.Add(new IterationData
            {
                iteration = iter,
                seedOffsetX = (float)prng.NextDouble() * 10000f,
                seedOffsetY = (float)prng.NextDouble() * 10000f
            });
        }

        int chunkSize = Mathf.CeilToInt((float)resolution / processingChunks);

        for (int chunkX = 0; chunkX < processingChunks; chunkX++)
        {
            if (token.IsCancellationRequested) yield break;

            int startX = chunkX * chunkSize;
            int endX = Mathf.Min(startX + chunkSize, resolution);

            // Process this chunk on a background thread
            Task chunkTask = Task.Run(() => ProcessHeightChunk(heights, iterationData, startX, endX, 0, resolution, maxTerrainDepth, terrainWorldPos, token), token);

            // Wait for chunk completion while yielding control
            while (!chunkTask.IsCompleted)
            {
                if (token.IsCancellationRequested)
                {
                    yield break;
                }
                yield return null;
            }

            if (chunkTask.Exception != null)
            {
                Debug.LogError($"Height generation error: {chunkTask.Exception.GetBaseException().Message}");
                yield break;
            }

            // Update progress
            generationProgress = 0.1f + (0.5f * (chunkX + 1) / processingChunks);

            // Brief delay to prevent frame drops
            if (chunkDelayMs > 0)
            {
                yield return new WaitForSecondsRealtime(chunkDelayMs / 1000f);
            }
        }
    }

    private void ProcessHeightChunk(float[,] heights, System.Collections.Generic.List<IterationData> iterationData,
                                  int startX, int endX, int startY, int endY, int maxTerrainDepth, Vector3 terrainWorldPos, CancellationToken token)
    {
        for (int x = startX; x < endX; x++)
        {
            if (token.IsCancellationRequested) return;

            for (int y = startY; y < endY; y++)
            {
                if (token.IsCancellationRequested) return;

                float totalHeight = 0f;
                foreach (var data in iterationData)
                {
                    totalHeight += CalculateHeight(x, y, data.iteration, terrainWorldPos,
                                                 resolution, maxTerrainDepth, data.seedOffsetX, data.seedOffsetY);
                }
                heights[x, y] = totalHeight;
            }
        }
    }

    private IEnumerator ApplyBaseElevation(float[,] heights, CancellationToken token)
    {
        int chunkSize = Mathf.CeilToInt((float)resolution / processingChunks);

        for (int chunk = 0; chunk < processingChunks; chunk++)
        {
            if (token.IsCancellationRequested) yield break;

            int startX = chunk * chunkSize;
            int endX = Mathf.Min(startX + chunkSize, resolution);
            float baseElev = baseElevation; // Copy to local variable for thread safety

            Task chunkTask = Task.Run(() =>
            {
                for (int x = startX; x < endX; x++)
                {
                    if (token.IsCancellationRequested) return;
                    for (int y = 0; y < resolution; y++)
                    {
                        if (token.IsCancellationRequested) return;
                        heights[x, y] = Mathf.Clamp01(heights[x, y] + baseElev);
                    }
                }
            }, token);

            while (!chunkTask.IsCompleted)
            {
                if (token.IsCancellationRequested) yield break;
                yield return null;
            }

            if (chunkTask.Exception != null)
            {
                Debug.LogError($"Base elevation error: {chunkTask.Exception.GetBaseException().Message}");
                yield break;
            }

            if (chunkDelayMs > 0)
                yield return new WaitForSecondsRealtime(chunkDelayMs / 1000f);
        }
    }

    private IEnumerator ApplyEdgeFalloff(float[,] heights, CancellationToken token)
    {
        int chunkSize = Mathf.CeilToInt((float)resolution / processingChunks);

        for (int chunk = 0; chunk < processingChunks; chunk++)
        {
            if (token.IsCancellationRequested) yield break;

            int startX = chunk * chunkSize;
            int endX = Mathf.Min(startX + chunkSize, resolution);
            float baseElev = baseElevation; // Copy to local variable for thread safety
            int res = resolution; // Copy to local variable for thread safety

            Task chunkTask = Task.Run(() =>
            {
                for (int x = startX; x < endX; x++)
                {
                    if (token.IsCancellationRequested) return;
                    for (int y = 0; y < res; y++)
                    {
                        if (token.IsCancellationRequested) return;

                        float edgeX = Mathf.Min((float)x / (res - 1), 1f - (float)x / (res - 1));
                        float edgeY = Mathf.Min((float)y / (res - 1), 1f - (float)y / (res - 1));
                        float edgeFactor = Mathf.Min(edgeX, edgeY) * 2f;
                        float falloff = Mathf.SmoothStep(0, 1, edgeFactor);

                        heights[x, y] = Mathf.Lerp(baseElev, heights[x, y], falloff);
                    }
                }
            }, token);

            while (!chunkTask.IsCompleted)
            {
                if (token.IsCancellationRequested) yield break;
                yield return null;
            }

            if (chunkTask.Exception != null)
            {
                Debug.LogError($"Edge falloff error: {chunkTask.Exception.GetBaseException().Message}");
                yield break;
            }

            if (chunkDelayMs > 0)
                yield return new WaitForSecondsRealtime(chunkDelayMs / 1000f);
        }
    }

    private IEnumerator ApplyErosion(float[,] heights, CancellationToken token)
    {
        if (token.IsCancellationRequested) yield break;

        float[] flatHeights = FlattenHeights(heights, resolution);
        bool erosionComplete = false;
        System.Exception erosionException = null;

        // Run erosion on background thread
        Task erosionTask = Task.Run(() =>
        {
            try
            {
                int numIterations = 200000;
                erosion.Erode(flatHeights, resolution, numIterations, true);
                erosionComplete = true;
            }
            catch (System.Exception e)
            {
                erosionException = e;
            }
        }, token);

        // Wait for erosion with progress updates
        while (!erosionTask.IsCompleted && !erosionComplete)
        {
            if (token.IsCancellationRequested) yield break;
            yield return null;
        }

        if (erosionException != null)
        {
            Debug.LogError($"Erosion failed: {erosionException.Message}");
            yield break;
        }

        // Convert back to 2D array
        float[,] newHeights = UnflattenHeights(flatHeights, resolution);
        System.Array.Copy(newHeights, heights, newHeights.Length);
    }

    // Helper class for iteration data
    private class IterationData
    {
        public TerrainIteration iteration;
        public float seedOffsetX;
        public float seedOffsetY;
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

    private void OnDestroy()
    {
        cancellationTokenSource?.Dispose();
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(HeightsGenerator))]
    public class HeightGeneratorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            HeightsGenerator generator = (HeightsGenerator)target;

            // Progress bar
            if (generator.isGenerating)
            {
                Rect progressRect = GUILayoutUtility.GetRect(0, 18);
                EditorGUI.ProgressBar(progressRect, generator.generationProgress,
                                    $"Generating... {(generator.generationProgress * 100f):F1}%");
                GUILayout.Space(5);
            }

            EditorGUI.BeginDisabledGroup(generator.isGenerating);
            if (GUILayout.Button("Generate Heightmaps"))
            {
                int seed = Random.Range(0, 100000);
                generator.Generate(seed);
                Debug.Log("Heightmap generation started with seed: " + seed);
            }
            EditorGUI.EndDisabledGroup();

            if (generator.isGenerating && GUILayout.Button("Cancel Generation"))
            {
                generator.CancelGeneration();
            }

            // Force repaint during generation to update progress bar
            if (generator.isGenerating)
            {
                EditorUtility.SetDirty(generator);
                this.Repaint();
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