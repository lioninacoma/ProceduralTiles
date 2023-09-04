using ChunkBuilder;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

[RequireComponent(typeof(ChunkBuilder.ChunkBuilder))]
public class World : MonoBehaviour
{
    public int ChunkSize = 32;
    public int CellSize = 1;
    public int3 ChunkDims = 1;

    private ChunkBuilder.ChunkBuilder ChunkBuilder;
    private List<Chunk> Chunks;
    private List<int3> PendingChunks;

    private void Awake()
    {
        ChunkBuilder = GetComponent<ChunkBuilder.ChunkBuilder>();
        Chunks = new List<Chunk>();
        PendingChunks = new List<int3>();
    }

    void Start()
    {
        for (int z = 0; z < ChunkDims.z; z++)
            for (int y = 0; y < ChunkDims.y; y++)
                for (int x = 0; x < ChunkDims.x; x++)
                {
                    int3 min = new int3(x, y, z) * ChunkSize;
                    BuildChunk(min);
                }
    }

    private void BuildChunk(int3 chunkMin)
    {
        int chunkIndex = PendingChunks.Count;
        PendingChunks.Add(chunkMin);
        ChunkBuilder.AddJob(chunkMin, chunkIndex, OnChunkBuilt);
    }

    private void OnChunkBuilt(int chunkIndex, int indexCount, Mesh.MeshDataArray dataArray)
    {
        if (indexCount > 0)
        {
            var chunk = AddChunk(PendingChunks[chunkIndex]);
            chunk.SetMesh(dataArray);
        }
    }

    private Chunk AddChunk(int3 chunkMin)
    {
        var chunkObj = new GameObject("chunk_" + chunkMin);
        chunkObj.transform.SetParent(transform);
        chunkObj.transform.localPosition = new float3(chunkMin);
        chunkObj.transform.localScale = Vector3.one;

        var chunk = chunkObj.AddComponent<Chunk>();
        Chunks.Add(chunk);

        return chunk;
    }
}
