using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

using static IsoMeshStructs;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshCollider))]
[RequireComponent(typeof(MeshRenderer))]
public class IsoChunk : MonoBehaviour
{
    private static readonly VertexAttributeDescriptor[] VERTEX_ATTRIBUTES = new[] {
        new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
        new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3)
    };

    private static readonly IndexFormat INDEX_FORMAT = IndexFormat.UInt32;
    private static readonly int VERTEX_BUFFER_SIZE = 65536;
    private static readonly int INDEX_BUFFER_SIZE = VERTEX_BUFFER_SIZE * 3;

    public int Size = 32;
    public int3 Min = 0;
    public int CellSize = 1;

    private IsoOctree Octree;
    private IsoNode Root;
    private MeshFilter ChunkMeshFilter;
    private MeshRenderer ChunkMeshRenderer;
    private Mesh ChunkMesh;
    private MeshCollider ChunkMeshCollider;
    NativeArray<Vertex> TempVerticesArray;
    NativeArray<int> TempIndicesArray;

    private void Awake()
    {
        ChunkMeshRenderer = GetComponent<MeshRenderer>();

        if (ChunkMeshRenderer.sharedMaterial == null)
            ChunkMeshRenderer.sharedMaterial = Resources.Load<Material>("Materials/MaterialSurface");

        ChunkMeshFilter = GetComponent<MeshFilter>();

        if (ChunkMeshFilter.sharedMesh == null)
        {
            ChunkMesh = new Mesh();
            ChunkMeshFilter.sharedMesh = ChunkMesh;
        }
        else
        {
            ChunkMesh = ChunkMeshFilter.sharedMesh;
        }

        ChunkMeshCollider = GetComponent<MeshCollider>();

        TempIndicesArray = new NativeArray<int>(INDEX_BUFFER_SIZE, Allocator.Persistent);
        TempVerticesArray = new NativeArray<Vertex>(VERTEX_BUFFER_SIZE, Allocator.Persistent);
    }

    private void OnDestroy()
    {
        if (TempVerticesArray != null)
            TempVerticesArray.Dispose();
        if(TempIndicesArray != null)
            TempIndicesArray.Dispose();
    }

    private void Start()
    {
        Octree = new IsoOctree(Min, Size, CellSize);
        Octree.BuildVolume();

        var leafs = Octree.FindActiveVoxels();
        Root = Octree.ConstructUpwards(leafs);

        //Octree.SimplifyOctree(Root, 0.0000000001f);

        var counts = new Counts();
        Octree.GenerateMeshFromOctree(Root, ref counts, TempIndicesArray, TempVerticesArray);
        
        if (counts.IndexCount > 0)
            UpdateMesh(counts);
    }

    private void UpdateMesh(Counts counts)
    {
        if (counts.IndexCount == 0) return;

        var dataArray = Mesh.AllocateWritableMeshData(1);
        var data = dataArray[0];

        data.SetVertexBufferParams(counts.VertexCount, VERTEX_ATTRIBUTES);
        data.SetIndexBufferParams(counts.IndexCount, INDEX_FORMAT);

        var vertices = data.GetVertexData<Vertex>();
        var indices = data.GetIndexData<int>();

        for (int i = 0; i < counts.VertexCount; i++)
        {
            vertices[i] = TempVerticesArray[i];
        }

        for (int i = 0; i < counts.IndexCount; i++)
        {
            indices[i] = TempIndicesArray[i];
        }

        ChunkMesh.Clear();

        data.subMeshCount = 1;
        data.SetSubMesh(0, new SubMeshDescriptor(0, counts.IndexCount));

        // apply mesh data
        var flags =
              MeshUpdateFlags.DontRecalculateBounds
            | MeshUpdateFlags.DontValidateIndices
            | MeshUpdateFlags.DontNotifyMeshUsers
            | MeshUpdateFlags.DontResetBoneBounds;
        Mesh.ApplyAndDisposeWritableMeshData(dataArray, ChunkMesh, flags);

        ChunkMesh.RecalculateBounds();
        ChunkMeshCollider.sharedMesh = ChunkMesh;
    }
}
