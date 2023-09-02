using System.Collections;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;

using static IsoMeshStructs;

public class ChunkBuilderGPU : MonoBehaviour
{
    private static readonly int _SignedDistanceField = Shader.PropertyToID("_SignedDistanceField");
    private static readonly int _MeshCounts = Shader.PropertyToID("_MeshCounts");
    private static readonly int _ActiveCells = Shader.PropertyToID("_ActiveCells");
    private static readonly int _IndexCache = Shader.PropertyToID("_IndexCache");
    private static readonly int _VertexBuffer = Shader.PropertyToID("_VertexBuffer");
    private static readonly int _IndexBuffer = Shader.PropertyToID("_IndexBuffer");

    public int PoolSize = 128;

    public int BufferSize;
    public int DataSize;

    public int3 ChunkMin;
    public int ChunkSize;
    public int CellSize;

    public ComputeShader BuildVolumeCS;
    public ComputeShader GenerateIndicesCS;
    public ComputeShader GenerateVerticesCS;

    private NativeArrayPool<float3> VertexBufferPool;
    private NativeArrayPool<int> IndexBufferPool;

    private ComputeBuffer SignedDistanceField;
    private ComputeBuffer IndexCache;
    private ComputeBuffer VertexBuffer;
    private ComputeBuffer IndexBuffer;
    private ComputeBuffer ActiveCells;
    private ComputeBuffer MeshCounts;

    private int BuildVolumeKernelID;
    private int GenerateIndicesKernelID;
    private int GenerateVerticesKernelID;

    private void Awake()
    {
        VertexBufferPool = new NativeArrayPool<float3>(PoolSize, Chunk.VERTEX_BUFFER_SIZE);
        IndexBufferPool = new NativeArrayPool<int>(PoolSize, Chunk.INDEX_BUFFER_SIZE);

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

        BuildVolumeKernelID = BuildVolumeCS.FindKernel("CSMain");
        GenerateIndicesKernelID = GenerateIndicesCS.FindKernel("CSMain");
        GenerateVerticesKernelID = GenerateVerticesCS.FindKernel("CSMain");
    }

    private void OnDestroy()
    {
        VertexBufferPool.Dispose();
        IndexBufferPool.Dispose();
        SignedDistanceField.Dispose();
        IndexCache.Dispose();
        ActiveCells.Dispose();
        VertexBuffer.Dispose();
        IndexBuffer.Dispose();
        MeshCounts.Dispose();
    }
}
