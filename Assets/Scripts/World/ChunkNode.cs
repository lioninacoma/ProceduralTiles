using MBaske.Octree;
using Unity.Mathematics;
using UnityEngine;

public class ChunkNode : INodeContent
{
    public int3 Min { get; set; }
    public int Size { get; set; }
    public Bounds Bounds { get; set; }
    public Vector3 Position { get; set; }
    public Chunk Chunk { get; set; }

    public ChunkNode()
    {
        Chunk = null;
    }
}
