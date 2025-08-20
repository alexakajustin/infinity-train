using System;
using System.Collections.Generic;
using UnityEngine;

namespace Assets.Scripts.MapGenerator.Generators
{
    public class TexturesGenerator : MonoBehaviour
    {
        [Serializable]
        public class TextureData
        {
            public Texture2D Texture;
            public Vector2 TileSize = new Vector2(10, 10);
            public float MinSteepness; // Minimum steepness to apply this texture
            public float MaxSteepness; // Maximum steepness to apply this texture
        }

        public List<TextureData> textures = new List<TextureData>();

        public void Generate(float offset)
        {
            if (textures == null || textures.Count == 0)
            {
                Debug.LogError("No textures assigned!");
                return;
            }

            TerrainData terrainData = Terrain.activeTerrain.terrainData;

            // Set up TerrainLayers
            TerrainLayer[] terrainLayers = new TerrainLayer[textures.Count];
            for (int i = 0; i < textures.Count; i++)
            {
                if (textures[i].Texture == null)
                {
                    Debug.LogError($"Texture {i} is missing. Please assign a valid texture.");
                    continue;
                }

                TerrainLayer layer = new TerrainLayer
                {
                    diffuseTexture = textures[i].Texture,
                    tileSize = textures[i].TileSize
                };
                terrainLayers[i] = layer;
            }
            terrainData.terrainLayers = terrainLayers;

            // Prepare alpha maps
            float[,,] splatmaps = new float[terrainData.alphamapResolution, terrainData.alphamapResolution, textures.Count];

            for (int x = 0; x < terrainData.alphamapResolution; x++)
            {
                for (int y = 0; y < terrainData.alphamapResolution; y++)
                {
                    // Get normalized position
                    float normalizedX = (float)x / terrainData.alphamapResolution;
                    float normalizedY = (float)y / terrainData.alphamapResolution;

                    // Get steepness
                    float steepness = terrainData.GetSteepness(normalizedX, normalizedY);

                    // Assign textures based on steepness
                    for (int i = 0; i < textures.Count; i++)
                    {
                        var texture = textures[i];

                        if (steepness >= texture.MinSteepness && steepness <= texture.MaxSteepness)
                        {
                            splatmaps[y, x, i] = 1f; // Fully apply this texture
                        }
                        else
                        {
                            splatmaps[y, x, i] = 0f; // Exclude this texture
                        }
                    }
                }
            }

            // Apply the alpha maps to the terrain
            terrainData.SetAlphamaps(0, 0, splatmaps);
        }
    }
}
