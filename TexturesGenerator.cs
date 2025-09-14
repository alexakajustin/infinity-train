using System;
using System.Collections.Generic;
using UnityEngine;

public class TexturesGenerator : MonoBehaviour
{
    [Header("Chunk Reference (DO NOT ASSIGN MANUALLY!)")]
    [SerializeField]
    private Chunk chunk;

    [Header("Texture Settings")]
    public List<TextureData> textures = new List<TextureData>(); // Steepness-based textures
    public TextureData roadTexture; // Separate road texture slot
    [Range(0f, 0.5f)]
    public float RoadOffset = 0.1f; // Normalized offset from edges for road
    public bool OverrideSteepnessForRoad = false; // If true, road texture ignores steepness constraints

    public bool hasStartedGeneration = false;
    public bool hasFinishedGeneration = false;

    private Terrain terrain;
    private HeightsGenerator heightsGenerator;
    private void Awake()
    {
        chunk = GetComponent<Chunk>();
        terrain = GetComponent<Terrain>();
        heightsGenerator = GetComponent<HeightsGenerator>();
    }

    private void Update()
    {
        if (heightsGenerator.hasFinishedGeneration && !hasStartedGeneration)
        {
            hasStartedGeneration = true;
            Debug.Log("Textures generation started.");
            Generate();
        }
    }

    [Serializable]
    public class TextureData
    {
        public Texture2D Texture;
        public Vector2 TileSize = new Vector2(10, 10);
        public float MinSteepness; // Minimum steepness to apply this texture
        public float MaxSteepness; // Maximum steepness to apply this texture
    }

    public void Generate()
    {
        if (chunk == null || chunk.terrain == null)
        {
            Debug.LogError("Chunk or Terrain not found! Cannot generate textures.");
            return;
        }

        TerrainData terrainData = chunk.terrain.terrainData;
        Debug.Log($"[TexturesGenerator] Initial terrain layers: {terrainData.terrainLayers.Length}");

        if (textures == null || textures.Count == 0)
        {
            Debug.LogWarning("No textures assigned in the textures list! Assigning a fallback layer.");
            // Create a fallback texture
            Texture2D fallbackTexture = new Texture2D(1, 1);
            fallbackTexture.SetPixel(0, 0, Color.gray);
            fallbackTexture.Apply();
            textures = new List<TextureData>
            {
                new TextureData
                {
                    Texture = fallbackTexture,
                    TileSize = new Vector2(10, 10),
                    MinSteepness = 0f,
                    MaxSteepness = 90f
                }
            };
        }

        if (roadTexture == null || roadTexture.Texture == null)
        {
            Debug.LogWarning("Road texture is missing! Using fallback texture.");
            roadTexture = new TextureData
            {
                Texture = new Texture2D(1, 1),
                TileSize = new Vector2(10, 10),
                MinSteepness = 0f,
                MaxSteepness = 90f
            };
            roadTexture.Texture.SetPixel(0, 0, Color.black);
            roadTexture.Texture.Apply();
        }

        // Set up TerrainLayers (steepness-based textures + road texture)
        TerrainLayer[] terrainLayers = new TerrainLayer[textures.Count + 1]; // +1 for road texture
        for (int i = 0; i < textures.Count; i++)
        {
            if (textures[i].Texture == null)
            {
                Debug.LogError($"Texture {i} is missing. Skipping.");
                continue;
            }

            TerrainLayer layer = new TerrainLayer
            {
                diffuseTexture = textures[i].Texture,
                tileSize = textures[i].TileSize
            };
            terrainLayers[i] = layer;
        }

        // Add road texture as the last layer
        TerrainLayer roadLayer = new TerrainLayer
        {
            diffuseTexture = roadTexture.Texture,
            tileSize = roadTexture.TileSize
        };
        terrainLayers[textures.Count] = roadLayer;
        terrainData.terrainLayers = terrainLayers;
        Debug.Log($"[TexturesGenerator] Assigned {terrainLayers.Length} terrain layers to terrain.");

        // Prepare alpha maps (include road texture layer)
        float[,,] splatmaps = new float[terrainData.alphamapResolution, terrainData.alphamapResolution, textures.Count + 1];

        for (int x = 0; x < terrainData.alphamapResolution; x++)
        {
            for (int y = 0; y < terrainData.alphamapResolution; y++)
            {
                // Get normalized position
                float normalizedX = (float)x / terrainData.alphamapResolution;
                float normalizedY = (float)y / terrainData.alphamapResolution;

                // Check if this position is within the road offset
                bool isRoad = normalizedX < RoadOffset || normalizedX > (1f - RoadOffset) ||
                              normalizedY < RoadOffset || normalizedY > (1f - RoadOffset);

                // Get steepness
                float steepness = terrainData.GetSteepness(normalizedX, normalizedY);

                if (isRoad && (OverrideSteepnessForRoad ||
                    (steepness >= roadTexture.MinSteepness &&
                     steepness <= roadTexture.MaxSteepness)))
                {
                    splatmaps[y, x, textures.Count] = 1f;
                    for (int i = 0; i < textures.Count; i++)
                    {
                        splatmaps[y, x, i] = 0f;
                    }
                }
                else
                {
                    bool textureApplied = false;
                    for (int i = 0; i < textures.Count; i++)
                    {
                        var texture = textures[i];
                        if (steepness >= texture.MinSteepness && steepness <= texture.MaxSteepness)
                        {
                            splatmaps[y, x, i] = 1f;
                            textureApplied = true;
                        }
                        else
                        {
                            splatmaps[y, x, i] = 0f;
                        }
                    }
                    splatmaps[y, x, textures.Count] = 0f;
                    if (!textureApplied)
                    {
                        splatmaps[y, x, 0] = 1f; // Fallback to first texture
                    }
                }
            }
        }

        // Apply the alpha maps to the terrain
        terrainData.SetAlphamaps(0, 0, splatmaps);
        Debug.Log($"[TexturesGenerator] Applied alphamaps with dimensions {splatmaps.GetLength(0)}x{splatmaps.GetLength(1)}x{splatmaps.GetLength(2)}");
        hasFinishedGeneration = true;

        // Force terrain refresh
        terrain.Flush();
        Debug.Log($"[TexturesGenerator] Terrain flushed, final terrain layers: {terrainData.terrainLayers.Length}");
    }
}