using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

[RequireComponent(typeof(Terrain))]
public class TreeGenerator : MonoBehaviour
{
    [Header("General Settings")]
    public int Seed = 0;
    public float MinLevel = 0;
    public float MaxLevel = 100f;
    [Range(0, 90)]
    public float MaxSteepness = 70f;
    [Range(0, 1)]
    public float Density = 0.5f;

    [Header("Poisson Disk Settings")]
    public float MinTreeDistance = 5f;
    public int IterationPerPoint = 30;

    [Header("Tree Clustering Settings")]
    public float ClusterRadius = 20f;
    public int MinClusterSize = 3;
    public int MaxClusterSize = 8;
    [Range(0, 1)]
    public float ClusterDensity = 0.8f;

    [Header("Tree Settings")]
    public List<GameObject> TreePrototypes;
    public int TreesPerFrame = 10;
    [Range(0.5f, 2f)]
    public float MinTreeScale = 0.8f;
    [Range(0.5f, 2f)]
    public float MaxTreeScale = 1.2f;
    public float YOffset = -0.1f;

    private Terrain terrain;
    private TerrainData terrainData;
    private HeightsGenerator heightsGenerator;
    private TexturesGenerator texturesGenerator;
    private bool hasStartedGeneration = false;
    public bool hasFinishedGeneration = false;

    void Start()
    {
        terrain = GetComponent<Terrain>();
        heightsGenerator = GetComponent<HeightsGenerator>();
        texturesGenerator = GetComponent<TexturesGenerator>();

        if (terrain == null)
        {
            Debug.LogError("Terrain component not found!");
            return;
        }
        if (texturesGenerator == null)
        {
            Debug.LogError("TexturesGenerator component not found!");
            return;
        }
        if (heightsGenerator == null)
        {
            Debug.LogError("HeightsGenerator component not found!");
            return;
        }

        terrainData = terrain.terrainData;
        Debug.Log($"[Start] Terrain Position: {terrain.transform.position}, Size: {terrainData.size}, Alphamap Layers: {terrainData.alphamapLayers}");
    }

    private void Update()
    {
        if (!hasStartedGeneration && heightsGenerator.hasFinishedGeneration && texturesGenerator.hasFinishedGeneration)
        {
            // Refresh TerrainData to ensure it reflects changes from TexturesGenerator
            terrainData = terrain.terrainData;
            Debug.Log($"[Update] Refreshed TerrainData, Alphamap Layers: {terrainData.alphamapLayers}");

            if (terrainData.alphamapLayers == 0)
            {
                Debug.LogError("Cannot start tree generation: No terrain layers assigned!");
                // Force TexturesGenerator to run again for debugging
                texturesGenerator.Generate();
                terrainData = terrain.terrainData; // Refresh again after forcing generation
                Debug.Log($"[Update] After forcing TexturesGenerator, Alphamap Layers: {terrainData.alphamapLayers}");
                if (terrainData.alphamapLayers == 0)
                {
                    Debug.LogError("Still no terrain layers after forcing TexturesGenerator!");
                    return;
                }
            }

            hasStartedGeneration = true;
            StartCoroutine(GenerateTrees());
        }
    }

    private IEnumerator GenerateTrees()
    {
        Vector3 terrainPos = terrain.transform.position;
        Vector2 bottomLeft = new Vector2(terrainPos.x, terrainPos.z);
        Vector2 topRight = bottomLeft + new Vector2(terrainData.size.x, terrainData.size.z);

        // Poisson sampling async
        Task<List<Vector2>> poissonTask = Task.Run(() =>
            Gists.FastPoissonDiskSampling.Sampling(bottomLeft, topRight, MinTreeDistance, IterationPerPoint, Seed));

        while (!poissonTask.IsCompleted) yield return null;
        List<Vector2> samples = poissonTask.Result;

        List<Vector3> validPositions = new List<Vector3>();
        System.Random random = new System.Random(Seed);

        foreach (var sample in samples)
        {
            float clampedX = Mathf.Clamp(sample.x, bottomLeft.x, topRight.x);
            float clampedZ = Mathf.Clamp(sample.y, bottomLeft.y, topRight.y);
            Vector3 worldPos = new Vector3(clampedX, 0, clampedZ);

            if (clampedX == bottomLeft.x || clampedX == topRight.x ||
                clampedZ == bottomLeft.y || clampedZ == topRight.y) continue;

            float groundHeight = terrain.SampleHeight(worldPos) + terrainPos.y;
            if (groundHeight < MinLevel || groundHeight > MaxLevel) continue;

            float normX = (clampedX - bottomLeft.x) / terrainData.size.x;
            float normZ = (clampedZ - bottomLeft.y) / terrainData.size.z;
            if (normX < 0f || normX > 1f || normZ < 0f || normZ > 1f) continue;

            float steepness = terrainData.GetSteepness(normX, normZ);
            if (steepness > MaxSteepness) continue;

            // Skip positions within the RoadOffset square
            float roadOffset = texturesGenerator.RoadOffset;
            bool isRoadArea = normX < roadOffset || normX > (1f - roadOffset) ||
                              normZ < roadOffset || normZ > (1f - roadOffset);
            if (isRoadArea)
            {
                Debug.Log($"Skipping position ({clampedX}, {clampedZ}) - Within RoadOffset: {roadOffset}");
                continue;
            }

            if (random.NextDouble() > Density) continue;

            validPositions.Add(new Vector3(clampedX, groundHeight + YOffset, clampedZ));
        }

        Debug.Log($"Valid positions found: {validPositions.Count}");

        List<TreeCluster> clusters = CreateTreeClusters(validPositions, random, bottomLeft);
        Debug.Log($"Created {clusters.Count} tree clusters");

        random = new System.Random(Seed);
        int totalSpawned = 0;

        foreach (var cluster in clusters)
        {
            foreach (var treeData in cluster.Trees)
            {
                GameObject tree = Instantiate(cluster.TreePrefab, treeData.Position, Quaternion.identity, terrain.gameObject.transform);

                tree.transform.rotation = Quaternion.Euler(0, random.Next(360), 0);

                float randomScale = GetRandomScale(random);
                tree.transform.localScale += new Vector3(randomScale, randomScale, randomScale) * 0.7f;

                totalSpawned++;
                if (totalSpawned % TreesPerFrame == 0) yield return null;
            }
        }

        Debug.Log($"Spawned {totalSpawned} trees in {clusters.Count} clusters.");
        hasFinishedGeneration = true;
    }

    private List<TreeCluster> CreateTreeClusters(List<Vector3> positions, System.Random random, Vector2 bottomLeft)
    {
        List<TreeCluster> clusters = new List<TreeCluster>();
        List<Vector3> unprocessedPositions = new List<Vector3>(positions);

        while (unprocessedPositions.Count > 0)
        {
            int centerIndex = random.Next(unprocessedPositions.Count);
            Vector3 clusterCenter = unprocessedPositions[centerIndex];
            unprocessedPositions.RemoveAt(centerIndex);

            GameObject treeType = TreePrototypes[random.Next(TreePrototypes.Count)];

            TreeCluster cluster = new TreeCluster
            {
                TreePrefab = treeType,
                Center = clusterCenter,
                Trees = new List<TreeData>()
            };

            cluster.Trees.Add(new TreeData
            {
                Position = clusterCenter,
                Scale = GetRandomScale(random)
            });

            int targetClusterSize = random.Next(MinClusterSize, MaxClusterSize + 1);

            for (int i = unprocessedPositions.Count - 1; i >= 0 && cluster.Trees.Count < targetClusterSize; i--)
            {
                Vector3 candidatePos = unprocessedPositions[i];
                float distance = Vector3.Distance(clusterCenter, candidatePos);

                // Skip positions within the RoadOffset square
                float normX = (candidatePos.x - bottomLeft.x) / terrainData.size.x;
                float normZ = (candidatePos.z - bottomLeft.y) / terrainData.size.z;
                float roadOffset = texturesGenerator.RoadOffset;
                bool isRoadArea = normX < roadOffset || normX > (1f - roadOffset) ||
                                  normZ < roadOffset || normZ > (1f - roadOffset);
                if (isRoadArea) continue;

                if (distance <= ClusterRadius && random.NextDouble() <= ClusterDensity)
                {
                    cluster.Trees.Add(new TreeData
                    {
                        Position = candidatePos,
                        Scale = GetRandomScale(random)
                    });
                    unprocessedPositions.RemoveAt(i);
                }
            }

            clusters.Add(cluster);
        }

        return clusters;
    }

    private float GetRandomScale(System.Random random)
    {
        return (float)(random.NextDouble() * (MaxTreeScale - MinTreeScale) + MinTreeScale);
    }

    [System.Serializable]
    public class TreeCluster
    {
        public GameObject TreePrefab;
        public Vector3 Center;
        public List<TreeData> Trees;
    }

    [System.Serializable]
    public class TreeData
    {
        public Vector3 Position;
        public float Scale;
    }
}