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

    [Tooltip("Add randomness to break symmetry")]
    public float asymmetryStrength = 0.3f;

    [Header("Falloff Settings")]
    [Tooltip("How far from edges the falloff starts (0.1 = 10% of terrain size)")]
    [Range(0.05f, 0.5f)]
    public float falloffDistance = 0.25f;

    [Tooltip("Smoothness of the falloff curve - higher values = more gradual")]
    [Range(1f, 10f)]
    public float falloffSmoothness = 3f;

    [Tooltip("Type of falloff curve to use")]
    public FalloffType falloffType = FalloffType.SmoothStep;

    [Tooltip("Minimum height at edges (as fraction of base elevation)")]
    [Range(0f, 1f)]
    public float edgeMinHeight = 0.5f;

    [Tooltip("Use radial falloff instead of edge-based falloff")]
    public bool useRadialFalloff = false;

    [Tooltip("Custom falloff curve - only used if FalloffType is Custom")]
    public AnimationCurve customFalloffCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    public enum FalloffType
    {
        Linear,
        SmoothStep,
        Exponential,
        Cosine,
        Custom
    }

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

        // Apply smooth falloff
        generationProgress = 0.7f;
        if (useRadialFalloff)
        {
            yield return StartCoroutine(ApplyGradientFalloff(heights, prng, token));
        }
        else
        {
            yield return StartCoroutine(ApplyAsymmetricFalloff(heights, prng, token));
        }

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
                seedOffsetY = (float)prng.NextDouble() * 10000f,
                // Add unique rotation and scale per iteration to break symmetry
                rotationAngle = (float)prng.NextDouble() * 360f,
                scaleVariationX = 0.8f + (float)prng.NextDouble() * 0.4f, // 0.8 to 1.2
                scaleVariationY = 0.8f + (float)prng.NextDouble() * 0.4f, // 0.8 to 1.2
                // Add domain warping offsets
                warpOffsetX = (float)prng.NextDouble() * 1000f,
                warpOffsetY = (float)prng.NextDouble() * 1000f
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
                    totalHeight += CalculateHeight(x, y, data, terrainWorldPos, resolution, maxTerrainDepth);
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

    // Improved asymmetric falloff with smooth transitions
    private IEnumerator ApplyAsymmetricFalloff(float[,] heights, System.Random prng, CancellationToken token)
    {
        int chunkSize = Mathf.CeilToInt((float)resolution / processingChunks);

        // Generate variation in falloff parameters for natural asymmetry
        float leftVariation = 0.8f + (float)prng.NextDouble() * 0.4f;    // 0.8 to 1.2
        float rightVariation = 0.8f + (float)prng.NextDouble() * 0.4f;
        float topVariation = 0.8f + (float)prng.NextDouble() * 0.4f;
        float bottomVariation = 0.8f + (float)prng.NextDouble() * 0.4f;

        for (int chunk = 0; chunk < processingChunks; chunk++)
        {
            if (token.IsCancellationRequested) yield break;

            int startX = chunk * chunkSize;
            int endX = Mathf.Min(startX + chunkSize, resolution);

            // Copy values for thread safety
            float baseElev = baseElevation;
            float falloffDist = falloffDistance;
            float smoothness = falloffSmoothness;
            FalloffType fType = falloffType;
            AnimationCurve curve = new AnimationCurve(customFalloffCurve.keys);
            float minHeight = edgeMinHeight;
            int res = resolution;

            Task chunkTask = Task.Run(() =>
            {
                for (int x = startX; x < endX; x++)
                {
                    if (token.IsCancellationRequested) return;

                    for (int y = 0; y < res; y++)
                    {
                        if (token.IsCancellationRequested) return;

                        float normalizedX = (float)x / (res - 1);
                        float normalizedY = (float)y / (res - 1);

                        // Calculate distance from each edge with variations
                        float distFromLeft = normalizedX / (falloffDist * leftVariation);
                        float distFromRight = (1f - normalizedX) / (falloffDist * rightVariation);
                        float distFromTop = normalizedY / (falloffDist * topVariation);
                        float distFromBottom = (1f - normalizedY) / (falloffDist * bottomVariation);

                        // Find the minimum distance to any edge
                        float edgeDistance = Mathf.Min(
                            Mathf.Min(distFromLeft, distFromRight),
                            Mathf.Min(distFromTop, distFromBottom)
                        );

                        // Apply falloff curve based on selected type
                        float falloffFactor = CalculateFalloffFactor(edgeDistance, smoothness, fType, curve);

                        // Calculate target height at edges
                        float edgeHeight = baseElev * minHeight;

                        // Interpolate between edge height and full height
                        heights[x, y] = Mathf.Lerp(edgeHeight, heights[x, y], falloffFactor);
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
                Debug.LogError($"Smooth falloff error: {chunkTask.Exception.GetBaseException().Message}");
                yield break;
            }

            if (chunkDelayMs > 0)
                yield return new WaitForSecondsRealtime(chunkDelayMs / 1000f);
        }
    }

    // Radial falloff from center for island-like terrain
    private IEnumerator ApplyGradientFalloff(float[,] heights, System.Random prng, CancellationToken token)
    {
        int chunkSize = Mathf.CeilToInt((float)resolution / processingChunks);

        for (int chunk = 0; chunk < processingChunks; chunk++)
        {
            if (token.IsCancellationRequested) yield break;

            int startX = chunk * chunkSize;
            int endX = Mathf.Min(startX + chunkSize, resolution);

            float baseElev = baseElevation;
            float falloffDist = falloffDistance;
            float smoothness = falloffSmoothness;
            FalloffType fType = falloffType;
            AnimationCurve curve = new AnimationCurve(customFalloffCurve.keys);
            float minHeight = edgeMinHeight;
            int res = resolution;

            Task chunkTask = Task.Run(() =>
            {
                for (int x = startX; x < endX; x++)
                {
                    if (token.IsCancellationRequested) return;

                    for (int y = 0; y < res; y++)
                    {
                        if (token.IsCancellationRequested) return;

                        float normalizedX = (float)x / (res - 1);
                        float normalizedY = (float)y / (res - 1);

                        // Create a radial falloff from center
                        float centerX = 0.5f;
                        float centerY = 0.5f;
                        float distanceFromCenter = Mathf.Sqrt(
                            Mathf.Pow(normalizedX - centerX, 2) +
                            Mathf.Pow(normalizedY - centerY, 2)
                        );

                        // Normalize to 0-1 range (max distance from center to corner)
                        float maxDistance = Mathf.Sqrt(0.5f); // Distance from center to corner
                        distanceFromCenter /= maxDistance;

                        // Apply falloff based on distance from center
                        float falloffStart = 1f - falloffDist; // Start falloff this far from center
                        float falloffFactor = 1f;

                        if (distanceFromCenter > falloffStart)
                        {
                            float falloffRange = 1f - falloffStart;
                            float falloffProgress = (distanceFromCenter - falloffStart) / falloffRange;
                            falloffProgress = Mathf.Clamp01(falloffProgress);

                            // Use the selected falloff curve
                            falloffFactor = 1f - CalculateFalloffFactor(1f - falloffProgress, smoothness, fType, curve);
                        }

                        // Calculate target height
                        float edgeHeight = baseElev * minHeight;
                        heights[x, y] = Mathf.Lerp(edgeHeight, heights[x, y], falloffFactor);
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
                Debug.LogError($"Gradient falloff error: {chunkTask.Exception.GetBaseException().Message}");
                yield break;
            }

            if (chunkDelayMs > 0)
                yield return new WaitForSecondsRealtime(chunkDelayMs / 1000f);
        }
    }

    // Calculate different falloff curves
    private static float CalculateFalloffFactor(float edgeDistance, float smoothness, FalloffType falloffType, AnimationCurve customCurve)
    {
        float t = Mathf.Clamp01(edgeDistance);

        switch (falloffType)
        {
            case FalloffType.Linear:
                return t;

            case FalloffType.SmoothStep:
                // Apply smoothness by raising to a power
                float smoothT = Mathf.Pow(t, 1f / smoothness);
                return Mathf.SmoothStep(0f, 1f, smoothT);

            case FalloffType.Exponential:
                // Exponential falloff - more dramatic
                return 1f - Mathf.Exp(-t * smoothness);

            case FalloffType.Cosine:
                // Cosine-based falloff - very smooth
                float cosT = Mathf.Pow(t, 1f / smoothness);
                return 0.5f * (1f - Mathf.Cos(cosT * Mathf.PI));

            case FalloffType.Custom:
                return customCurve.Evaluate(t);

            default:
                return Mathf.SmoothStep(0f, 1f, t);
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

    // Enhanced helper class for iteration data with asymmetry features
    private class IterationData
    {
        public TerrainIteration iteration;
        public float seedOffsetX;
        public float seedOffsetY;
        public float rotationAngle;
        public float scaleVariationX;
        public float scaleVariationY;
        public float warpOffsetX;
        public float warpOffsetY;
    }

    float CalculateHeight(int x, int y, IterationData data, Vector3 worldPos,
                          int resolution, int maxTerrainDepth)
    {
        float worldPosOffsetX = worldPos.z / worldScale;
        float worldPosOffsetY = worldPos.x / worldScale;

        // Base coordinates
        float xCoord = ((float)x / (resolution - 1)) * mapSize / worldScale +
                        data.iteration.offsetX + worldOffsetX + worldPosOffsetX + data.seedOffsetX;

        float yCoord = ((float)y / (resolution - 1)) * mapSize / worldScale +
                        data.iteration.offsetY + worldOffsetY + worldPosOffsetY + data.seedOffsetY;

        // Apply rotation to break axis alignment
        float cos = Mathf.Cos(data.rotationAngle * Mathf.Deg2Rad);
        float sin = Mathf.Sin(data.rotationAngle * Mathf.Deg2Rad);
        float rotatedX = xCoord * cos - yCoord * sin;
        float rotatedY = xCoord * sin + yCoord * cos;

        xCoord = rotatedX;
        yCoord = rotatedY;

        // Domain warping for more organic shapes
        float warpStrength = asymmetryStrength * 0.1f;
        float warpX = Mathf.PerlinNoise(xCoord * 0.1f + data.warpOffsetX, yCoord * 0.1f + data.warpOffsetY) * warpStrength;
        float warpY = Mathf.PerlinNoise(xCoord * 0.1f + data.warpOffsetX + 100f, yCoord * 0.1f + data.warpOffsetY + 100f) * warpStrength;

        xCoord += warpX;
        yCoord += warpY;

        float amplitude = 1f;
        float frequency = 1f / data.iteration.scale;
        float noise = 0f;
        float totalAmplitude = 0f;

        for (int o = 0; o < data.iteration.octaves; o++)
        {
            // Apply asymmetric scaling to break uniformity
            float sampleX = xCoord * frequency * data.iteration.distortionX * data.scaleVariationX;
            float sampleY = yCoord * frequency * data.iteration.distortionY * data.scaleVariationY;

            // Add slight offset per octave to break symmetry
            float octaveOffsetX = o * 547.31f; // Prime-like numbers to avoid patterns
            float octaveOffsetY = o * 739.17f;

            float perlinValue = Mathf.PerlinNoise(sampleX + octaveOffsetX, sampleY + octaveOffsetY);
            noise += perlinValue * amplitude;
            totalAmplitude += amplitude;
            amplitude *= data.iteration.persistence;
            frequency *= data.iteration.lacunarity;
        }
        noise /= totalAmplitude;

        // Add asymmetric noise variation
        float asymmetricVariation = 1f + (Mathf.PerlinNoise(xCoord * 0.05f + 1000f, yCoord * 0.05f + 1000f) - 0.5f) * asymmetryStrength;
        noise *= asymmetricVariation;

        noise = data.iteration.depthScaling.Evaluate(noise * data.iteration.rarity - (1f - 1f / data.iteration.rarity) * data.iteration.rarity);

        return noise * data.iteration.depth / (float)maxTerrainDepth;
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