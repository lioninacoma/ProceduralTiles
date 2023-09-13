using ChunkOctree;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

[RequireComponent(typeof(ChunkBuilder.ChunkBuilder))]
public class World : MonoBehaviour
{
    public int ChunkSize = 32;
    public int CellSize = 1;
    public int3 ChunkDims = 1;

    private ChunkBuilder.ChunkBuilder ChunkBuilder;
    private Chunk[] Chunks;
    private ChunkNode[] Nodes;
    private LayerMask ChunkMask;
    private OctreeNode<ChunkNode> ChunkTree;

    private void Awake()
    {
        ChunkMask = LayerMask.GetMask("Chunk");
        ChunkBuilder = GetComponent<ChunkBuilder.ChunkBuilder>();
        Chunks = new Chunk[ChunkDims.x * ChunkDims.y * ChunkDims.z];
        Nodes = new ChunkNode[ChunkDims.x * ChunkDims.y * ChunkDims.z];
        
        int maxDims = math.max(ChunkDims.x, math.max(ChunkDims.y, ChunkDims.z));
        int gridSize = (int)Utils.RoundToNextPowerOf2((uint)maxDims) * ChunkSize;
        ChunkTree = new OctreeNode<ChunkNode>(0, gridSize, ChunkSize);
    }

    void Start()
    {
        for (int z = 0; z < ChunkDims.z; z++)
            for (int y = 0; y < ChunkDims.y; y++)
                for (int x = 0; x < ChunkDims.x; x++)
                {
                    int3 min = new int3(x, y, z) * ChunkSize;

                    var node = new ChunkNode();
                    node.Min = min;
                    node.Max = min + ChunkSize;
                    node.Position = new float3(node.Min + node.Max) / 2f;
                    node.Size = ChunkSize;

                    BuildChunk(node);
                    ChunkTree.Insert(node);
                }
    }

    public void GetChunkMeta(out int CellSize, out int ChunkSize, out int DataSize, out int BufferSize)
    {
        CellSize = this.CellSize;
        ChunkSize = (this.ChunkSize / CellSize) + 1;
        DataSize = ChunkSize + 1;
        BufferSize = DataSize * DataSize * DataSize;
    }

    private int GetChunkIndex(int3 min)
    {
        int3 i = min / ChunkSize;
        return Utils.I3(i.x, i.y, i.z, ChunkDims.x, ChunkDims.y);
    }

    private void BuildChunk(ChunkNode node)
    {
        int chunkIndex = GetChunkIndex(node.Min);
        Nodes[chunkIndex] = node;
        ChunkBuilder.AddJob(node.Min, chunkIndex, OnChunkBuilt);
    }

    private void OnChunkBuilt(int chunkIndex, int indexCount, int emptySign, Chunk.Data data)
    {
        var node = Nodes[chunkIndex];

        if (indexCount > 0)
        {
            var chunk = AddChunk(node);
            chunk.SetChunkData(data);
        }
        else
        {
            node.EmptySign = emptySign;
        }
    }

    private void UpdateChunk(ChunkNode node, NativeArray<float> sdf)
    {
        int chunkIndex = GetChunkIndex(node.Min);
        ChunkBuilder.AddJob(node.Min, chunkIndex, sdf, OnChunkUpdated);
    }

    private void OnChunkUpdated(int chunkIndex, int indexCount, int emptySign, Chunk.Data data)
    {
        var node = Nodes[chunkIndex];

        if (indexCount > 0)
        {
            var chunk = Chunks[chunkIndex];
            chunk.SetChunkData(data);
        }
        else
        {
            var chunk = Chunks[chunkIndex];
            node.EmptySign = emptySign;
            chunk.ClearMesh();
        }
    }

    private Chunk AddChunk(ChunkNode node)
    {
        int3 min = node.Min;
        int chunkIndex = GetChunkIndex(min);
        var pos = new Vector3(min.x, min.y, min.z);

        var chunkObj = new GameObject("chunk_" + min);
        chunkObj.transform.SetParent(transform);
        chunkObj.transform.localPosition = pos;
        chunkObj.transform.localScale = Vector3.one;

        var chunk = chunkObj.AddComponent<Chunk>();
        chunk.Min = min;
        chunk.Size = node.Size;

        Chunks[chunkIndex] = chunk;
        node.Chunk = chunk;

        return chunk;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(transform.TransformPoint(DebugBounds.center), transform.TransformVector(DebugBounds.size));
        if (ChunkTree != null)
        {
            Gizmos.color = Color.green;
            ChunkTree.DrawNodeBounds(transform);
        }
    }

    private void SetVoxels(float3 pos, int size, float smooth, bool place, HashSet<int> updatingChunks)
    {
        const float maxValue = 100000f;
        int3 p;
        Chunk chunk;
        int sizeMax = size - 1;

        float boundsPadding = math.max(1.5f, smooth);
        float3 min = pos - boundsPadding;
        float3 max = pos + sizeMax + boundsPadding;

        var bounds = new Bounds();
        bounds.SetMinMax(min, max);
        DebugBounds = bounds;

        var nodes = new List<ChunkNode>();
        ChunkTree.Find(min, max, nodes);

        Debug.Log(nodes.Count + " processing chunks ...");

        foreach (var node in nodes)
        {
            p = (new int3(pos) - node.Min);
            chunk = node.Chunk;

            if (chunk == null) // empty chunk
            {
                chunk = AddChunk(node);
                chunk.InitEmptyBuffers(maxValue * node.EmptySign);
            }

            chunk.SetCubeVolume(p, sizeMax, place, smooth);
            updatingChunks.Add(GetChunkIndex(chunk.Min));
        }
    }

    private Bounds DebugBounds;

    private void Update()
    {
        if (Input.GetMouseButtonUp(0))
        {
            var camera = Camera.main;
            var mouseRay = camera.ScreenPointToRay(Input.mousePosition);

            if (Physics.Raycast(mouseRay, out RaycastHit hitInfo, 10000f, ChunkMask))
            {
                var point = transform.InverseTransformPoint(hitInfo.point);
                var normal = transform.InverseTransformDirection(hitInfo.normal);

                int gridSize = 16;
                float smooth = 1.5f;
                bool place = false; 
                
                var gridPos = new Vector3(
                    (int)((point.x + normal.x * (place ? 1 : -1)) / gridSize) * gridSize,
                    (int)((point.y + normal.y * (place ? 1 : -1)) / gridSize) * gridSize,
                    (int)((point.z + normal.z * (place ? 1 : -1)) / gridSize) * gridSize);

                var updatingChunks = new HashSet<int>();

                //SetVoxels(gridPos - new Vector3(0, 1, 0) * gridSize, gridSize, smooth, true, updatingChunks);  // set solid below
                //SetVoxels(gridPos + new Vector3(0, 1, 0) * gridSize, gridSize, smooth, false, updatingChunks); // set air above
                SetVoxels(gridPos, gridSize, smooth, place, updatingChunks);

                foreach (int index in updatingChunks)
                {
                    var node = Nodes[index];
                    var chunk = Chunks[index];
                    UpdateChunk(node, chunk.GetVolume());
                }
            }
        }
    }
}
