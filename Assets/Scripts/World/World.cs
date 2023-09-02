using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using Unity.Mathematics;
using UnityEngine;

[RequireComponent(typeof(ChunkBuilderGPU))]
public class World : MonoBehaviour
{
    public int ChunkSize = 32;
    public int CellSize = 1;
    public int3 ChunkDims = new int3(2, 1, 2);

    private ChunkBuilderGPU ChunkBuilder;
    private List<Chunk> chunks;

    private void Awake()
    {
        ChunkBuilder = GetComponent<ChunkBuilderGPU>();
        chunks = new List<Chunk>();
    }

    void Start()
    {
        //for (int z = 0; z < ChunkDims.z; z++)
        //    for (int y = 0; y < ChunkDims.y; y++)
        //        for (int x = 0; x < ChunkDims.x; x++)
        //        {
        //            int3 min = new int3(x, y, z) * ChunkSize;
        //            AddChunk(min);
        //        }

        ChunkBuilder.AddJob(0, 0, OnChunkBuilt);
    }

    private void OnChunkBuilt(int chunkIndex, Mesh.MeshDataArray dataArray)
    {
        Debug.Log("Chunk built, index: " + chunkIndex);
    }

    private int AddChunk(int3 chunkMin)
    {
        var chunkObj = new GameObject("chunk_" + chunkMin);
        chunkObj.transform.SetParent(transform);
        chunkObj.transform.localPosition = new float3(chunkMin);
        chunkObj.transform.localScale = Vector3.one;

        var chunk = chunkObj.AddComponent<Chunk>();
        chunk.Min = chunkMin;
        chunk.Size = ChunkSize;
        chunk.CellSize = CellSize;

        int index = chunks.Count;
        chunks.Add(chunk);

        return index;
    }
}
