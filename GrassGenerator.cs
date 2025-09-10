using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Terrain))]
public class GrassGenerator : MonoBehaviour
{
    [Header("Pooling Settings")]
    public GameObject prefab;
    public int initialPoolSize = 15000;
    public bool expandable = true;

    [Header("Spawning Settings")]
    public int spawnAmount = 20000;
    public float maxSlopeAngle = 60f;

    [Header("Clustering Settings")]
    public float noiseScale = 0.005f;           // scale of Perlin noise (smaller = larger clusters)
    public float densityThreshold = 0.7f;      // minimum noise value to spawn grass (higher = more island-like)
    public float maxDensityMultiplier = 8f;    // max density in thick areas

    [Header("Island Settings")]
    public float islandClusterRadius = 2.5f;   // radius for additional grass around each point
    public int maxGrassPerPoint = 12;           // max grass objects per spawn point
    public float minGrassDistance = 0.1f;      // minimum distance between grass instances within a cluster

    private Queue<GameObject> pool;
    private Terrain terrain;
    private HeightsGenerator heightsGenerator;
    public bool hasStartedGeneration = false;

    // Noise offset for this chunk (to ensure continuity across chunks if needed)
    private Vector2 noiseOffset;

    void Awake()
    {
        terrain = GetComponent<Terrain>();
        pool = new Queue<GameObject>();
        heightsGenerator = GetComponent<HeightsGenerator>();

        // Generate random noise offset for this chunk
        noiseOffset = new Vector2(Random.Range(0f, 1000f), Random.Range(0f, 1000f));

        // Pre-instantiate pool
        for (int i = 0; i < initialPoolSize; i++)
        {
            GameObject obj = Instantiate(prefab);
            obj.SetActive(false);
            pool.Enqueue(obj);
        }
    }

    private void Update()
    {
        if (heightsGenerator.hasFinishedGeneration && !hasStartedGeneration)
        {
            hasStartedGeneration = true;
            GenerateGrass();
        }
    }

    private void GenerateGrass()
    {
        TerrainData data = terrain.terrainData;
        Vector3 terrainPos = terrain.transform.position;

        int spawned = 0;
        int attempts = 0;
        int maxAttempts = spawnAmount * 20;

        while (spawned < spawnAmount && attempts < maxAttempts)
        {
            attempts++;

            // Random point within this chunk
            float randX = Random.Range(0f, data.size.x);
            float randZ = Random.Range(0f, data.size.z);

            // Calculate density at this point using noise
            float density = CalculateDensityAtPoint(randX, randZ, data);

            // Skip if density is too low
            if (density < densityThreshold)
            {
                continue;
            }

            // Check slope
            float normalizedX = randX / data.size.x;
            float normalizedZ = randZ / data.size.z;
            Vector3 normal = data.GetInterpolatedNormal(normalizedX, normalizedZ);
            float slope = Vector3.Angle(normal, Vector3.up);

            if (slope > maxSlopeAngle)
            {
                continue;
            }

            // World position
            float worldX = terrainPos.x + randX;
            float worldZ = terrainPos.z + randZ;
            float y = terrain.SampleHeight(new Vector3(worldX, 0f, worldZ)) + terrainPos.y;
            Vector3 pos = new Vector3(worldX, y, worldZ);

            // Rotation with terrain alignment and random Y rotation
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, normal);
            rotation *= Quaternion.Euler(0, Random.Range(0f, 360f), 0);

            // Calculate grass count based on density and maxDensityMultiplier
            int grassCount = CalculateGrassCount(density);

            // Start cluster with center position
            List<Vector3> clusterPositions = new List<Vector3>();
            clusterPositions.Add(pos);
            GetFromPool(pos, rotation);
            spawned++;

            if (spawned >= spawnAmount) break;

            // Add additional grass with min distance checks
            int maxTriesPerPlacement = 20; // Increased for better chance of placement
            for (int i = 1; i < grassCount; i++)
            {
                bool placed = false;
                for (int tryAttempt = 0; tryAttempt < maxTriesPerPlacement; tryAttempt++)
                {
                    Vector2 offset = Random.insideUnitCircle * islandClusterRadius;
                    Vector3 candidate = new Vector3(worldX + offset.x, 0, worldZ + offset.y);

                    // Check if within terrain bounds
                    Vector3 offsetLocalPos = candidate - terrainPos;
                    if (offsetLocalPos.x < 0 || offsetLocalPos.x >= data.size.x ||
                        offsetLocalPos.z < 0 || offsetLocalPos.z >= data.size.z)
                        continue;

                    // Set height
                    candidate.y = terrain.SampleHeight(candidate) + terrainPos.y;

                    // Check min distance to existing positions in cluster
                    bool tooClose = false;
                    foreach (var existingPos in clusterPositions)
                    {
                        if (Vector3.Distance(candidate, existingPos) < minGrassDistance)
                        {
                            tooClose = true;
                            break;
                        }
                    }
                    if (tooClose) continue;

                    // Check slope for candidate position
                    float offsetNormalizedX = offsetLocalPos.x / data.size.x;
                    float offsetNormalizedZ = offsetLocalPos.z / data.size.z;
                    Vector3 offsetNormal = data.GetInterpolatedNormal(offsetNormalizedX, offsetNormalizedZ);
                    float offsetSlope = Vector3.Angle(offsetNormal, Vector3.up);

                    if (offsetSlope > maxSlopeAngle)
                        continue;

                    // Valid position found
                    clusterPositions.Add(candidate);
                    Quaternion finalRotation = Quaternion.FromToRotation(Vector3.up, offsetNormal);
                    finalRotation *= Quaternion.Euler(0, Random.Range(0f, 360f), 0);
                    GetFromPool(candidate, finalRotation);
                    spawned++;
                    placed = true;

                    if (spawned >= spawnAmount) break;
                    break;
                }

                if (!placed || spawned >= spawnAmount) break;
            }
        }

        Debug.Log($"Spawned {spawned} grass instances in island clusters.");
    }

    private int CalculateGrassCount(float density)
    {
        // Base grass count of 1
        int grassCount = 1;

        // Scale additional grass based on density and maxDensityMultiplier
        float extraGrassChance = (density - densityThreshold) / (1f - densityThreshold);
        extraGrassChance *= maxDensityMultiplier;

        // Convert to actual grass count
        int extraGrass = Mathf.FloorToInt(extraGrassChance);
        extraGrass += Random.Range(0f, 1f) < (extraGrassChance - extraGrass) ? 1 : 0; // handle fractional part

        grassCount += extraGrass;

        return Mathf.Min(grassCount, maxGrassPerPoint);
    }

    private float CalculateDensityAtPoint(float x, float z, TerrainData data)
    {
        // Convert to world coordinates for noise sampling
        Vector3 terrainPos = terrain.transform.position;
        float worldX = terrainPos.x + x;
        float worldZ = terrainPos.z + z;

        // Primary noise (large scale clustering)
        float primaryNoise = Mathf.PerlinNoise(
            (worldX + noiseOffset.x) * noiseScale,
            (worldZ + noiseOffset.y) * noiseScale
        );

        float finalDensity = primaryNoise;

        return finalDensity;
    }

    // Method to get density at any point (useful for debugging or other systems)
    public float GetDensityAtWorldPoint(Vector3 worldPos)
    {
        Vector3 terrainPos = terrain.transform.position;
        float localX = worldPos.x - terrainPos.x;
        float localZ = worldPos.z - terrainPos.z;

        return CalculateDensityAtPoint(localX, localZ, terrain.terrainData);
    }

    public GameObject GetFromPool(Vector3 position, Quaternion rotation)
    {
        GameObject obj;

        if (pool.Count > 0)
        {
            obj = pool.Dequeue();
        }
        else if (expandable)
        {
            obj = Instantiate(prefab);
        }
        else
        {
            Debug.LogWarning("⚠️ Pool is empty and not expandable!");
            return null;
        }

        obj.transform.SetPositionAndRotation(position, rotation);
        obj.SetActive(true);
        return obj;
    }

    public void ReturnToPool(GameObject obj)
    {
        obj.SetActive(false);
        pool.Enqueue(obj);
    }
}