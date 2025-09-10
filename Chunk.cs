using UnityEngine;

public class Chunk : MonoBehaviour
{
    public Vector2Int coord;
    public Terrain terrain { get; private set; }
    public HeightsGenerator heightsGenerator;
    public TexturesGenerator texturesGenerator;
    void Awake()
    {
        terrain = GetComponent<Terrain>();
        heightsGenerator = GetComponent<HeightsGenerator>();
        texturesGenerator = GetComponent<TexturesGenerator>();
    }

    public void AssignNeighbors(Chunk left, Chunk top, Chunk right, Chunk bottom)
    {
        terrain.SetNeighbors(
            left != null ? left.terrain : null,
            top != null ? top.terrain : null,
            right != null ? right.terrain : null,
            bottom != null ? bottom.terrain : null
        );
    }
}