using ChunkOctree;
using Unity.Mathematics;
using UnityEngine;

public class ChunkNode : IOctreeNodeContent
{
    public int3 Min { get; set; }
    public int3 Max { get; set; }
    public float3 Position { get; set; }
    public int Size { get; set; }
    public int EmptySign { get; set; }
    public Chunk Chunk { get; set; }

    public ChunkNode()
    {
        Chunk = null;
        EmptySign = 0;
    }
}
