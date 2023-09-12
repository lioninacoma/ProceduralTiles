using MBaske.Octree;
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
    private Dictionary<int, ChunkNode> PendingNodes;
    private LayerMask ChunkMask;
    private Node<ChunkNode> ChunkTree;
    private int BufferSize;

    private void Awake()
    {
        GetChunkMeta(out _, out _, out _, out BufferSize);

        ChunkMask = LayerMask.GetMask("Chunk");
        ChunkBuilder = GetComponent<ChunkBuilder.ChunkBuilder>();
        Chunks = new Chunk[ChunkDims.x * ChunkDims.y * ChunkDims.z];
        PendingNodes = new Dictionary<int, ChunkNode>();
        
        float rootSize = math.max(ChunkDims.x, math.max(ChunkDims.y, ChunkDims.z));
        rootSize *= ChunkSize;

        ChunkTree = new Node<ChunkNode>(Vector3.one * (rootSize / 2f), rootSize);
    }

    void Start()
    {
        for (int z = 0; z < ChunkDims.z; z++)
            for (int y = 0; y < ChunkDims.y; y++)
                for (int x = 0; x < ChunkDims.x; x++)
                {
                    int3 min = new int3(x, y, z) * ChunkSize;

                    var pos = new Vector3(min.x, min.y, min.z);
                    var node = new ChunkNode();
                    var bounds = new Bounds();
                    bounds.SetMinMax(pos, pos + Vector3.one * ChunkSize);

                    node.Min = min;
                    node.Size = ChunkSize;
                    node.Bounds = bounds;
                    node.Position = bounds.center;

                    BuildChunk(node);
                    ChunkTree.Add(node);
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
        PendingNodes[chunkIndex] = node;
        ChunkBuilder.AddJob(node.Min, chunkIndex, OnChunkBuilt);
    }

    private void OnChunkBuilt(int chunkIndex, int indexCount, Chunk.Data data)
    {
        if (indexCount > 0)
        {
            var chunk = AddChunk(PendingNodes[chunkIndex]);
            chunk.SetChunkData(data);
        }

        PendingNodes.Remove(chunkIndex);
    }

    private void UpdateChunk(int3 min, NativeArray<float> sdf, NativeArray<bool> ccs)
    {
        int chunkIndex = GetChunkIndex(min);
        ChunkBuilder.AddJob(min, chunkIndex, sdf, ccs, OnChunkUpdated);
    }

    private void OnChunkUpdated(int chunkIndex, int indexCount, Chunk.Data data)
    {
        if (indexCount > 0)
        {
            var chunk = Chunks[chunkIndex];
            chunk.SetChunkData(data);
        }

        PendingNodes.Remove(chunkIndex);
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
    }

    private void SetVoxels(Vector3 pos, int size, bool place, HashSet<int> updatingChunks)
    {
        const float maxValue = 100000f;
        int3 p;
        Chunk chunk;
        
        var bounds = new Bounds();
        bounds.SetMinMax(
            pos - (.1f * Vector3.one), 
            pos + (size * new Vector3(1, 1, 1)) + (.1f * Vector3.one));
        DebugBounds = bounds;

        var nodes = new HashSet<ChunkNode>();
        ChunkTree.FindBoundsIntersectBounds(nodes, bounds);

        foreach (var node in nodes)
        {
            p = new int3(pos) - node.Min;
            chunk = node.Chunk;

            if (chunk == null) // empty chunk
            {
                chunk = AddChunk(node);
                chunk.InitEmptyBuffers(maxValue, true); // FIXME: defaults to unsolid sdf (air)
            }

            chunk.SetVoxelCube(p, size, place);
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

                int gridSize = 5;
                bool place = false;
                
                var gridPos = new Vector3(
                    (int)((point.x + normal.x * (place ? 1 : -1)) / gridSize) * gridSize,
                    (int)((point.y + normal.y * (place ? 1 : -1)) / gridSize) * gridSize,
                    (int)((point.z + normal.z * (place ? 1 : -1)) / gridSize) * gridSize);

                var updatingChunks = new HashSet<int>();

                SetVoxels(gridPos, gridSize, place, updatingChunks);
                SetVoxels(gridPos - new Vector3(0, 1, 0) * gridSize, gridSize, true, updatingChunks);  // set solid below
                SetVoxels(gridPos + new Vector3(0, 1, 0) * gridSize, gridSize, false, updatingChunks); // set air above

                foreach (int index in updatingChunks)
                {
                    var chunk = Chunks[index];
                    UpdateChunk(chunk.Min, chunk.GetVolume(), chunk.GetCentroidCells());
                }
            }
        }
    }
}
