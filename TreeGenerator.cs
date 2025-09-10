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
    public float Density = 0.5f; // Probability to spawn a tree at each candidate position

    [Header("Poisson Disk Settings")]
    public float MinTreeDistance = 5f; // Minimum distance between trees for natural spacing
    public int IterationPerPoint = 30; // Higher values improve distribution quality but take longer

    [Header("Tree Settings")]
    public List<GameObject> TreePrototypes;
    public int TreesPerFrame = 10; // Incremental spawning per frame
    [Range(0.5f, 2f)]
    public float MinTreeScale = 0.8f; // Minimum random scale for tree variation
    [Range(0.5f, 2f)]
    public float MaxTreeScale = 1.2f; // Maximum random scale for tree variation

    private Terrain terrain;
    private TerrainData terrainData;

    private HeightsGenerator heightsGenerator;

    private bool hasStartedGeneration = false;

    public bool hasFinishedGeneration = false;
    void Start()
    {
        terrain = GetComponent<Terrain>();
        heightsGenerator = GetComponent<HeightsGenerator>();
        if (terrain == null)
        {
            Debug.LogError("Terrain component not found!");
            return;
        }
        terrainData = terrain.terrainData;
    }

    private void Update()
    {
        if (!hasStartedGeneration && heightsGenerator.hasFinishedGeneration)
        {
            hasStartedGeneration = true;
            StartCoroutine(GenerateTrees());
        }
    }


    private IEnumerator GenerateTrees()
    {
        // Define the bounding box for the terrain in XZ plane
        Vector2 bottomLeft = new Vector2(terrain.transform.position.x, terrain.transform.position.z);
        Vector2 topRight = bottomLeft + new Vector2(terrainData.size.x, terrainData.size.z);

        // Generate Poisson disk samples in a background task
        Task<List<Vector2>> poissonTask = Task.Run(() => Gists.FastPoissonDiskSampling.Sampling(bottomLeft, topRight, MinTreeDistance, IterationPerPoint, Seed));

        // Wait for Poisson generation to complete without blocking the main thread
        while (!poissonTask.IsCompleted) yield return null;

        List<Vector2> samples = poissonTask.Result;

        // Collect valid tree positions respecting terrain constraints on the main thread
        List<Vector3> treePositions = new List<Vector3>();
        System.Random random = new System.Random(Seed); // Use seeded random for determinism
        foreach (var sample in samples)
        {
            if (random.NextDouble() > Density) continue; // Apply density probability

            Vector3 worldPos = new Vector3(sample.x, 0, sample.y);
            float groundHeight = terrain.SampleHeight(worldPos) + terrain.transform.position.y;
            if (groundHeight < MinLevel || groundHeight > MaxLevel) continue;

            float normX = (sample.x - bottomLeft.x) / terrainData.size.x;
            float normZ = (sample.y - bottomLeft.y) / terrainData.size.z;
            float steepness = terrainData.GetSteepness(normX, normZ);
            if (steepness > MaxSteepness) continue;

            treePositions.Add(new Vector3(sample.x, groundHeight, sample.y));
        }

        Debug.Log($"Candidate tree count: {treePositions.Count}");

        // Spawn trees incrementally on the main thread with variation
        random = new System.Random(Seed); // Reset random for spawning to ensure consistency if needed
        int spawned = 0;
        foreach (Vector3 pos in treePositions)
        {
            GameObject treePrefab = TreePrototypes[random.Next(TreePrototypes.Count)];
            GameObject tree = Instantiate(treePrefab, pos, Quaternion.identity);

            // Add random rotation and scale for natural variation
            tree.transform.rotation = Quaternion.Euler(0, random.Next(360), 0);
            float scale = (float)(random.NextDouble() * (MaxTreeScale - MinTreeScale) + MinTreeScale);
            tree.transform.localScale = Vector3.one * scale;

            spawned++;
            if (spawned % TreesPerFrame == 0) yield return null;
        }

        Debug.Log($"Spawned {spawned} trees.");
        hasFinishedGeneration = true;
    }
}