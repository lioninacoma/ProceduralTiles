using System;
using System.Collections;
using System.Collections.Generic;
using System.Resources;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

using static IsoMeshStructs;

public class ChunkBuilderGPU : MonoBehaviour
{
    private class BuildJob
    {
        public Action<int, Mesh.MeshDataArray> Callback;
        public int3 ChunkMin;
        public int ChunkIndex;

        private float[] Vertices;
        private int[] Indices;
    }

    private static readonly int _SignedDistanceField = Shader.PropertyToID("_SignedDistanceField");
    private static readonly int _MeshCounts = Shader.PropertyToID("_MeshCounts");
    private static readonly int _ActiveCells = Shader.PropertyToID("_ActiveCells");
    private static readonly int _IndexCache = Shader.PropertyToID("_IndexCache");
    private static readonly int _VertexBuffer = Shader.PropertyToID("_VertexBuffer");
    private static readonly int _IndexBuffer = Shader.PropertyToID("_IndexBuffer");
    private static readonly int _DataSize = Shader.PropertyToID("_DataSize");
    private static readonly int _CellSize = Shader.PropertyToID("_CellSize");
    private static readonly int _ChunkSize = Shader.PropertyToID("_ChunkSize");
    private static readonly int _ChunkMin = Shader.PropertyToID("_ChunkMin");
    private static readonly int _ArgsBuffer = Shader.PropertyToID("_ArgsBuffer");

    public int PoolSize = 128;

    private int BufferSize;
    private int DataSize;
    private int CellSize;
    private int ChunkSize;

    public ComputeShader BuildVolumeCS;
    public ComputeShader GenerateIndicesCS;
    public ComputeShader GenerateVerticesCS;

    private ComputeBuffer SignedDistanceField;
    private ComputeBuffer IndexCache;
    private ComputeBuffer VertexBuffer;
    private ComputeBuffer IndexBuffer;
    private ComputeBuffer ActiveCells;
    private ComputeBuffer MeshCounts;
    private ComputeBuffer ArgsBuffer;

    private int BuildVolumeKernelID;
    private int GenerateIndicesKernelID;
    private int GenerateVerticesKernelID;

    private Queue<BuildJob> BuildJobs;

    private void Awake()
    {
        var world = GetComponentInParent<World>();

        CellSize = world.CellSize;
        ChunkSize = (world.ChunkSize / CellSize) + 1;
        DataSize = ChunkSize + 1;
        BufferSize = DataSize * DataSize * DataSize;

        SignedDistanceField = new ComputeBuffer(BufferSize, sizeof(float));
        SignedDistanceField.SetData(new float[BufferSize]);

        IndexCache = new ComputeBuffer(Chunk.INDEX_BUFFER_SIZE, sizeof(int));
        IndexCache.SetData(new uint[Chunk.INDEX_BUFFER_SIZE]);

        ActiveCells = new ComputeBuffer(BufferSize, UnsafeUtility.SizeOf<ActiveCell>(), ComputeBufferType.Counter);
        ActiveCells.SetData(new ActiveCell[BufferSize]);

        VertexBuffer = new ComputeBuffer(Chunk.VERTEX_BUFFER_SIZE, UnsafeUtility.SizeOf<float3>());
        VertexBuffer.SetData(new float3[Chunk.VERTEX_BUFFER_SIZE]);

        IndexBuffer = new ComputeBuffer(Chunk.INDEX_BUFFER_SIZE, sizeof(int));
        IndexBuffer.SetData(new int[Chunk.INDEX_BUFFER_SIZE]);

        MeshCounts = new ComputeBuffer(1, UnsafeUtility.SizeOf<Counts>());
        ArgsBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.IndirectArguments);

        BuildVolumeKernelID = BuildVolumeCS.FindKernel("CSMain");
        GenerateIndicesKernelID = GenerateIndicesCS.FindKernel("CSMain");
        GenerateVerticesKernelID = GenerateVerticesCS.FindKernel("CSMain");

        BuildVolumeCS.SetBuffer(BuildVolumeKernelID, _SignedDistanceField, SignedDistanceField);
        BuildVolumeCS.SetInt(_DataSize, DataSize);
        BuildVolumeCS.SetInt(_CellSize, CellSize);

        GenerateVerticesCS.SetBuffer(GenerateVerticesKernelID, _SignedDistanceField, SignedDistanceField);
        GenerateVerticesCS.SetBuffer(GenerateVerticesKernelID, _MeshCounts, MeshCounts);
        GenerateVerticesCS.SetBuffer(GenerateVerticesKernelID, _ActiveCells, ActiveCells);
        GenerateVerticesCS.SetBuffer(GenerateVerticesKernelID, _IndexCache, IndexCache);
        GenerateVerticesCS.SetBuffer(GenerateVerticesKernelID, _VertexBuffer, VertexBuffer);
        GenerateVerticesCS.SetInt(_DataSize, DataSize);
        GenerateVerticesCS.SetInt(_CellSize, CellSize);
        GenerateVerticesCS.SetInt(_ChunkSize, ChunkSize);

        BuildJobs = new Queue<BuildJob>();
    }

    public void AddJob(int3 chunkMin, int chunkIndex, Action<int, Mesh.MeshDataArray> Callback)
    {
        BuildJobs.Enqueue(new BuildJob()
        {
            ChunkMin = chunkMin,
            ChunkIndex = chunkIndex,
            Callback = Callback
        });
    }

    private static readonly int GROUP_SIZE_XYZ = 8;

    private void Update()
    {
        if (BuildJobs.Count == 0) return;

        var job = BuildJobs.Dequeue();
        var chunkMin = new float4(job.ChunkMin, 0);
        var counts = new Counts[1];

        MeshCounts.SetData(counts);
        ActiveCells.SetCounterValue(0);

        BuildVolumeCS.SetVector(_ChunkMin, chunkMin);
        GenerateVerticesCS.SetVector(_ChunkMin, chunkMin);

        int groupsXYZ = Mathf.Max(1, (int)Mathf.Ceil(DataSize / (float)GROUP_SIZE_XYZ));
        BuildVolumeCS.Dispatch(BuildVolumeKernelID, groupsXYZ, groupsXYZ, groupsXYZ);

        groupsXYZ = Mathf.Max(1, (int)Mathf.Ceil(ChunkSize / (float)GROUP_SIZE_XYZ));
        GenerateVerticesCS.Dispatch(GenerateVerticesKernelID, groupsXYZ, groupsXYZ, groupsXYZ);

        var activeCells = new ActiveCell[BufferSize];
        ActiveCells.GetData(activeCells);

        ComputeBuffer.CopyCount(ActiveCells, ArgsBuffer, 0);

        var activeCellCount = new int[1];
        ArgsBuffer.GetData(activeCellCount);

        job.Callback(job.ChunkIndex, default);
    }

    private void OnDestroy()
    {
        SignedDistanceField.Dispose();
        IndexCache.Dispose();
        ActiveCells.Dispose();
        VertexBuffer.Dispose();
        IndexBuffer.Dispose();
        MeshCounts.Dispose();
        ArgsBuffer.Dispose();
    }
}
