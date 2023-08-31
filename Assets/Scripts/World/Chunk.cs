using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

using static IsoMeshStructs;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshCollider))]
[RequireComponent(typeof(MeshRenderer))]
public class Chunk : MonoBehaviour
{
    private static readonly VertexAttributeDescriptor[] VERTEX_ATTRIBUTES = new[] {
        new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
        new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
        new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2)
    };

    private static readonly IndexFormat INDEX_FORMAT = IndexFormat.UInt32;
    private static readonly int VERTEX_BUFFER_SIZE = 65536;
    private static readonly int INDEX_BUFFER_SIZE = VERTEX_BUFFER_SIZE * 3;

    public int Size = 32;
    public int3 Min = 0;
    public int CellSize = 1;

    private int PlaceHeight;

    private IsoSurface ChunkSurface;
    private MeshFilter ChunkMeshFilter;
    private MeshRenderer ChunkMeshRenderer;
    private Mesh ChunkMesh;
    private MeshCollider ChunkMeshCollider;
    NativeArray<Vertex> TempVerticesArray;
    NativeArray<int> TempIndicesArray;
    NativeArray<int> IndexCacheArray;

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

        IndexCacheArray = new NativeArray<int>(INDEX_BUFFER_SIZE, Allocator.Persistent);
        TempIndicesArray = new NativeArray<int>(INDEX_BUFFER_SIZE, Allocator.Persistent);
        TempVerticesArray = new NativeArray<Vertex>(VERTEX_BUFFER_SIZE, Allocator.Persistent);
        PlaceHeight = 0;
    }

    private void OnDestroy()
    {
        if (IndexCacheArray != null)
            IndexCacheArray.Dispose();
        if (TempIndicesArray != null)
            TempIndicesArray.Dispose();
        if (TempVerticesArray != null)
            TempVerticesArray.Dispose();
    }

    private void Start()
    {
        ChunkSurface = new IsoSurface(Min, Size, CellSize);
        ChunkSurface.BuildVolume();
        UpdateSurface();
    }

    private void UpdateSurface()
    {
        var counts = new Counts();
        ChunkSurface.Triangulate(ref counts, TempIndicesArray, TempVerticesArray, IndexCacheArray);

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
        ChunkMesh.RecalculateNormals();
        ChunkMeshCollider.sharedMesh = ChunkMesh;
    }

    private void Update()
    {
        if (Input.GetAxis("Mouse ScrollWheel") > 0f)
        {
            PlaceHeight++;
            PlaceHeight = Mathf.Min(PlaceHeight, Size - 1);
        }
        else if (Input.GetAxis("Mouse ScrollWheel") < 0f)
        {
            PlaceHeight--;
            PlaceHeight = Mathf.Max(PlaceHeight, 0);
        }

        if (Input.GetMouseButtonUp(0))
        {
            var camera = Camera.main;
            var mouseRay = camera.ScreenPointToRay(Input.mousePosition);
            
            if (ChunkMeshCollider.Raycast(mouseRay, out RaycastHit hitInfo, 10000f))
            {
                var position = hitInfo.point;
                var localPos = transform.InverseTransformPoint(position);
                var idxPos = (new int3(localPos) - Min) / CellSize;

                int min = 0, max = 1;
                for (int z = min; z <= max; z++)
                    for (int x = min; x <= max; x++)
                    {
                        var idx = idxPos + new int3(x, 0, z);
                        //ChunkSurface.SetSurfaceHeightField(idx, PlaceHeight);
                        ChunkSurface.SetVolumeData(idx, 1);
                    }

                //ChunkSurface.UpdateVolumeData();
                UpdateSurface();
            }
        }
    }

}
