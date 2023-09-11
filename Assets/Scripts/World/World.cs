using MBaske.Octree;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UIElements;
using static UnityEditor.PlayerSettings;

[RequireComponent(typeof(ChunkBuilder.ChunkBuilder))]
public class World : MonoBehaviour
{
    public int ChunkSize = 32;
    public int CellSize = 1;
    public int3 ChunkDims = 1;

    private ChunkBuilder.ChunkBuilder ChunkBuilder;
    private Chunk[] Chunks;
    private Dictionary<int, int3> PendingChunks;
    private LayerMask ChunkMask;
    private Node<Chunk> ChunkTree;

    private void Awake()
    {
        ChunkMask = LayerMask.GetMask("Chunk");
        ChunkBuilder = GetComponent<ChunkBuilder.ChunkBuilder>();
        Chunks = new Chunk[ChunkDims.x * ChunkDims.y * ChunkDims.z];
        PendingChunks = new Dictionary<int, int3>();
        
        float rootSize = math.max(ChunkDims.x, math.max(ChunkDims.y, ChunkDims.z));
        rootSize *= ChunkSize;

        ChunkTree = new Node<Chunk>(Vector3.one * (rootSize / 2f), rootSize);
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

    private int3 ToChunkMin(float3 p)
    {
        return new int3(p / ChunkSize) * ChunkSize;
    }

    private static int3 ToCellPos(int3 min, float3 p, float3 n)
    {
        int3 c = new int3(p - n * .001f) - min;
        return c;
    }

    private void BuildChunk(int3 min)
    {
        int chunkIndex = GetChunkIndex(min);
        PendingChunks[chunkIndex] = min;
        ChunkBuilder.AddJob(min, chunkIndex, OnChunkBuilt);
    }

    private void OnChunkBuilt(int chunkIndex, int indexCount, Chunk.Data data)
    {
        if (indexCount > 0)
        {
            var chunk = AddChunk(PendingChunks[chunkIndex]);
            chunk.SetChunkData(data);
        }

        PendingChunks.Remove(chunkIndex);
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

        PendingChunks.Remove(chunkIndex);
    }

    private Chunk AddChunk(int3 min)
    {
        int chunkIndex = GetChunkIndex(min);
        var pos = new Vector3(min.x, min.y, min.z);

        var chunkObj = new GameObject("chunk_" + min);
        chunkObj.transform.SetParent(transform);
        chunkObj.transform.localPosition = pos;
        chunkObj.transform.localScale = Vector3.one;

        var chunk = chunkObj.AddComponent<Chunk>();
        chunk.ChunkMin = min;
        chunk.ChunkSize = ChunkSize;

        var bounds = new Bounds();
        bounds.SetMinMax(pos, pos + Vector3.one * ChunkSize);

        chunk.Bounds = bounds;
        chunk.Position = bounds.center;

        Chunks[chunkIndex] = chunk;
        ChunkTree.Add(chunk);

        return chunk;
    }

    private void Update()
    {
        if (Input.GetMouseButtonUp(0))
        {
            var camera = Camera.main;
            var mouseRay = camera.ScreenPointToRay(Input.mousePosition);

            if (Physics.Raycast(mouseRay, out RaycastHit hitInfo, 10000f, ChunkMask))
            {
                var localPoint = transform.InverseTransformPoint(hitInfo.point);
                var localNormal = transform.InverseTransformDirection(hitInfo.normal);
                var localMin = ToChunkMin(localPoint);

                int size = 4;
                var pos = new Vector3((int)localPoint.x, (int)localPoint.y, (int)localPoint.z);
                var bounds = new Bounds(pos, size * Vector3.one);

                var results = new HashSet<Chunk>();
                ChunkTree.FindBoundsIntersectBounds(results, bounds);

                Debug.Log(results.Count + " chunks updating ...");

                foreach (var chunk in results)
                {
                    int3 p = ToCellPos(chunk.ChunkMin, localPoint, localNormal) + 1;
                    chunk.SetVoxelCubeOnGrid(p, size, false);
                    UpdateChunk(chunk.ChunkMin, chunk.GetVolume(), chunk.GetCentroidCells());
                }
            }
        }
    }
}
