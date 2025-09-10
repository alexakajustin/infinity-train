using UnityEngine;
using System.Collections.Generic;

public class ChunkManager : MonoBehaviour
{
    // Singleton instance
    public static ChunkManager Instance { get; private set; }

    [Header("Chunk Management")]
    public Vector2Int currentChunk; // Current chunk coordinates (x, z)
    public int renderDistance = 2; // Chunks to load in each direction
    public GameObject terrainPrefab; // Prefab with Terrain component (1000x1000x600)

    [Header("Loaded Chunks(DO NOT MODIFY)")]
    public Dictionary<Vector2Int, GameObject> loadedChunks = new Dictionary<Vector2Int, GameObject>();
    public Vector3 lastPlayerPosition;

    [Header("First Chunk generated")]
    public bool hasFirstChunkGenerated = false;

    private void Awake()
    {
        // Implement singleton pattern
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Debug.LogWarning("Multiple ChunkManager instances detected. Destroying duplicate.");
            Destroy(gameObject);
            return;
        }
    }

    private void Start()
    {
        UpdateCurrentChunk();
        GenerateInitialChunks();
        lastPlayerPosition = transform.position;
        UpdateNeighbors();
    }

    private void Update()
    {
        GameObject firstChunk = GameObject.Find("Chunk_0_0");
        if (!hasFirstChunkGenerated && firstChunk != null)
        {
            if (firstChunk.GetComponent<Chunk>().heightsGenerator.hasFinishedGeneration)
            {
                hasFirstChunkGenerated = true;
            }
        }

        if (HasPlayerMovedToNewChunk())
        {
            UpdateCurrentChunk();
            ManageChunks();
        }
        lastPlayerPosition = transform.position;
    }

    private void OnDestroy()
    {
        // Clear the singleton reference if this instance is being destroyed
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private bool HasPlayerMovedToNewChunk()
    {
        Vector2Int newChunk = new Vector2Int(
            Mathf.FloorToInt(transform.position.x / 1000f),
            Mathf.FloorToInt(transform.position.z / 1000f)
        );

        return newChunk != currentChunk;
    }

    private void UpdateCurrentChunk()
    {
        currentChunk = new Vector2Int(
            Mathf.FloorToInt(transform.position.x / 1000f),
            Mathf.FloorToInt(transform.position.z / 1000f)
        );
    }

    private void GenerateInitialChunks()
    {
        for (int x = -renderDistance; x <= renderDistance; x++)
        {
            for (int z = -renderDistance; z <= renderDistance; z++)
            {
                Vector2Int chunkCoord = new Vector2Int(currentChunk.x + x, currentChunk.y + z);
                GenerateChunk(chunkCoord);
            }
        }
    }

    private void ManageChunks()
    {
        List<Vector2Int> chunksToKeep = new List<Vector2Int>();
        for (int x = -renderDistance; x <= renderDistance; x++)
        {
            for (int z = -renderDistance; z <= renderDistance; z++)
            {
                chunksToKeep.Add(new Vector2Int(currentChunk.x + x, currentChunk.y + z));
            }
        }

        List<Vector2Int> chunksToRemove = new List<Vector2Int>();
        foreach (var chunk in loadedChunks)
        {
            if (!chunksToKeep.Contains(chunk.Key))
                chunksToRemove.Add(chunk.Key);
        }

        foreach (var chunkCoord in chunksToRemove)
        {
            Destroy(loadedChunks[chunkCoord]);
            loadedChunks.Remove(chunkCoord);
        }

        foreach (var chunkCoord in chunksToKeep)
        {
            if (!loadedChunks.ContainsKey(chunkCoord))
                GenerateChunk(chunkCoord);
        }

        UpdateNeighbors();
    }

    private void GenerateChunk(Vector2Int chunkCoord)
    {
        if (loadedChunks.ContainsKey(chunkCoord)) return;

        Vector3 position = new Vector3(chunkCoord.x * 1000f, 0, chunkCoord.y * 1000f);
        GameObject chunkObject = Instantiate(terrainPrefab, position, Quaternion.identity);
        chunkObject.name = $"Chunk_{chunkCoord.x}_{chunkCoord.y}";

        Chunk chunk = chunkObject.GetComponent<Chunk>();
        if (chunk != null) chunk.coord = chunkCoord;

        loadedChunks.Add(chunkCoord, chunkObject);
    }

    private void UpdateNeighbors()
    {
        foreach (var kvp in loadedChunks)
        {
            Chunk chunk = kvp.Value.GetComponent<Chunk>();
            if (chunk == null) continue;

            Chunk left = loadedChunks.TryGetValue(kvp.Key + new Vector2Int(-1, 0), out var l) ? l.GetComponent<Chunk>() : null;
            Chunk right = loadedChunks.TryGetValue(kvp.Key + new Vector2Int(1, 0), out var r) ? r.GetComponent<Chunk>() : null;
            Chunk top = loadedChunks.TryGetValue(kvp.Key + new Vector2Int(0, 1), out var t) ? t.GetComponent<Chunk>() : null;
            Chunk bottom = loadedChunks.TryGetValue(kvp.Key + new Vector2Int(0, -1), out var b) ? b.GetComponent<Chunk>() : null;

            chunk.AssignNeighbors(left, top, right, bottom);
        }
    }

    // Public methods for accessing singleton functionality
    public static Vector2Int GetCurrentChunk()
    {
        return Instance != null ? Instance.currentChunk : Vector2Int.zero;
    }

    public static GameObject GetChunkAt(Vector2Int coord)
    {
        if (Instance == null) return null;
        return Instance.loadedChunks.TryGetValue(coord, out var chunk) ? chunk : null;
    }

    public static bool IsChunkLoaded(Vector2Int coord)
    {
        return Instance != null && Instance.loadedChunks.ContainsKey(coord);
    }

    public static Dictionary<Vector2Int, GameObject> GetAllLoadedChunks()
    {
        return Instance?.loadedChunks ?? new Dictionary<Vector2Int, GameObject>();
    }

    public static void ForceChunkUpdate()
    {
        if (Instance != null)
        {
            Instance.UpdateCurrentChunk();
            Instance.ManageChunks();
        }
    }
}