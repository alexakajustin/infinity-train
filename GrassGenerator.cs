using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Terrain))]
public class RuntimeVegetationGenerator : MonoBehaviour
{
    [Serializable]
    public class VegetationLayer
    {
        public string Name = "Layer";
        public GameObject[] Prefabs;
        public int Density = 16;
        public float NoiseScale = 0.05f;
        public float NoiseThreshold = 0.4f;
        public bool UseMesh = true;

        [Header("Patch Settings")]
        public bool SpawnInPatches = false;
        [Range(0f, 1f)] public float PatchDensity = 0.1f;  // fraction of terrain covered by patches
        public int PatchSize = 16;                          // radius of each patch in detail grid units
    }

    [Header("Vegetation Layers")]
    public List<VegetationLayer> Layers = new List<VegetationLayer>();

    [Header("Terrain Settings")]
    public int detailResolution = 512;
    public int grassPerPatch = 32;

    [Header("Generation Flags")]
    public bool hasStartedGenerating = false;

    private Terrain terrain;
    private TerrainData terrainData;
    private HeightsGenerator heightsGenerator;
    private TexturesGenerator texturesGenerator;

    void Start()
    {
        terrain = GetComponent<Terrain>();
        terrainData = terrain.terrainData;
        heightsGenerator = GetComponent<HeightsGenerator>();
        texturesGenerator = GetComponent<TexturesGenerator>();

        terrain.detailObjectDistance = 100f;
        terrain.detailObjectDensity = 1f;
    }

    void Update()
    {
        if (!hasStartedGenerating &&
            heightsGenerator != null && heightsGenerator.hasFinishedGeneration &&
            texturesGenerator != null && texturesGenerator.hasFinishedGeneration)
        {
            hasStartedGenerating = true;
            StartCoroutine(GenerateVegetationCoroutine());
        }
    }

    private IEnumerator GenerateVegetationCoroutine()
    {
        yield return null; // wait one frame to ensure terrain ready

        int layerIndex = 0;
        terrainData.SetDetailResolution(detailResolution, grassPerPatch);

        System.Random rand = new System.Random(UnityEngine.Random.Range(0, int.MaxValue - 1));

        float[,,] alphamaps = terrainData.GetAlphamaps(0, 0, terrainData.alphamapWidth, terrainData.alphamapHeight);
        int roadLayerIndex = terrainData.alphamapLayers - 1;

        List<DetailPrototype> prototypes = new List<DetailPrototype>();
        foreach (var layer in Layers)
        {
            if (layer.Prefabs == null || layer.Prefabs.Length == 0) continue;
            foreach (var prefab in layer.Prefabs)
            {
                prototypes.Add(new DetailPrototype
                {
                    prototype = prefab,
                    renderMode = layer.UseMesh ? DetailRenderMode.VertexLit : DetailRenderMode.Grass,
                    healthyColor = Color.green,
                    dryColor = Color.yellow,
                    minWidth = 2.5f,
                    maxWidth = 3.2f,
                    minHeight = 2.5f,
                    maxHeight = 3.2f,
                    noiseSpread = 0.5f,
                    usePrototypeMesh = layer.UseMesh,
                    useInstancing = true
                });
            }
        }
        terrainData.detailPrototypes = prototypes.ToArray();

        foreach (var layer in Layers)
        {
            if (layer.Prefabs == null || layer.Prefabs.Length == 0) continue;

            // compute number of patch centers based on PatchDensity
            List<Vector2Int> patchCenters = new List<Vector2Int>();
            if (layer.SpawnInPatches)
            {
                int numCells = detailResolution * detailResolution;
                int totalPatchCells = Mathf.RoundToInt(numCells * layer.PatchDensity);
                int cellsPerPatch = Mathf.Max(1, layer.PatchSize * layer.PatchSize * 4); // approx area of circle
                int numPatches = Mathf.Max(1, totalPatchCells / cellsPerPatch);

                for (int i = 0; i < numPatches; i++)
                {
                    int cx = rand.Next(0, detailResolution);
                    int cy = rand.Next(0, detailResolution);
                    patchCenters.Add(new Vector2Int(cx, cy));
                }
            }

            foreach (var prefab in layer.Prefabs)
            {
                int[,] detailLayer = new int[detailResolution, detailResolution];
                float offsetX = rand.Next(0, 10000);
                float offsetY = rand.Next(0, 10000);

                for (int x = 0; x < detailResolution; x++)
                {
                    for (int y = 0; y < detailResolution; y++)
                    {
                        float normX = (float)x / detailResolution;
                        float normY = (float)y / detailResolution;

                        int alphaX = Mathf.FloorToInt(normX * terrainData.alphamapWidth);
                        int alphaY = Mathf.FloorToInt(normY * terrainData.alphamapHeight);

                        if (alphamaps[alphaY, alphaX, roadLayerIndex] > 0.5f)
                        {
                            detailLayer[x, y] = 0;
                            continue;
                        }

                        bool inPatch = true;
                        if (layer.SpawnInPatches)
                        {
                            inPatch = false;
                            foreach (var center in patchCenters)
                            {
                                int dx = x - center.x;
                                int dy = y - center.y;
                                if (dx * dx + dy * dy <= layer.PatchSize * layer.PatchSize)
                                {
                                    inPatch = true;
                                    break;
                                }
                            }
                        }

                        if (!inPatch)
                        {
                            detailLayer[x, y] = 0;
                            continue;
                        }

                        // if in patch, max density for that layer
                        float nx = (x + offsetX) * layer.NoiseScale;
                        float ny = (y + offsetY) * layer.NoiseScale;
                        float noiseValue = Mathf.PerlinNoise(nx, ny);

                        if (!layer.SpawnInPatches || noiseValue > layer.NoiseThreshold)
                        {
                            int density = layer.SpawnInPatches ? layer.Density : Mathf.RoundToInt((noiseValue - layer.NoiseThreshold) * layer.Density / (1f - layer.NoiseThreshold));
                            detailLayer[x, y] = density;
                        }
                        else
                        {
                            detailLayer[x, y] = 0;
                        }
                    }
                }

                terrainData.SetDetailLayer(0, 0, layerIndex, detailLayer);
                layerIndex++;
            }
        }

        terrain.Flush();
        Debug.Log("Vegetation generated with patch density and tight grass!");
    }
}
