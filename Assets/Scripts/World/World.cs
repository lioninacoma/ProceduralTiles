using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class World : MonoBehaviour
{
    public int ChunkSize = 32;
    public int CellSize = 1;
    public int3 ChunkDims = new int3(2, 1, 2);

    void Start()
    {
        for (int z = 0; z < ChunkDims.z; z++)
            for (int y = 0; y < ChunkDims.y; y++)
                for (int x = 0; x < ChunkDims.x; x++)
                {
                    int3 min = new int3(x, y, z) * ChunkSize;
                    AddChunk(min);
                }
    }

    private void AddChunk(int3 chunkMin)
    {
        var chunkObj = new GameObject("chunk_" + chunkMin);
        chunkObj.transform.SetParent(transform);
        var chunk = chunkObj.AddComponent<Chunk>();
        chunk.Min = chunkMin;
        chunk.Size = ChunkSize;
        chunk.CellSize = CellSize;
    }
}
