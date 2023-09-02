using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

using static IsoMeshStructs;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshCollider))]
[RequireComponent(typeof(MeshRenderer))]
public class Chunk : MonoBehaviour
{
    public static readonly VertexAttributeDescriptor[] VERTEX_ATTRIBUTES = new[] {
        new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
        new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
        new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2)
    };

    public static readonly IndexFormat INDEX_FORMAT = IndexFormat.UInt32;
    public static readonly int VERTEX_BUFFER_SIZE = 65536;
    public static readonly int INDEX_BUFFER_SIZE = VERTEX_BUFFER_SIZE * 3;

    public int Size = 32;
    public int3 Min = 0;
    public int CellSize = 1;

    private int MeshSize;
    private int MeshDataSize;
    private int MeshBufferSize;
    private MeshFilter ChunkMeshFilter;
    private MeshRenderer ChunkMeshRenderer;
    private Mesh ChunkMesh;
    private MeshCollider ChunkMeshCollider;

    private NativeArray<float> SignedDistanceField;
    private NativeArray<Vertex> TempVerticesArray;
    private NativeArray<int> TempIndicesArray;
    private NativeArray<int> IndexCacheArray;
    private NativeArray<int> MeshCountsArray;
    
    // TODO: Native Array Pool nutzen um exzessives Chunk building einzuschraenken und um Ressourcen zu sparen.
    // Chunk building wird ueber World initiiert und dem jeweiligen Chunk die Ressourcen zur Verfuegung gestellt.
    // Dazu gehoeren alle NativeArrays die fuer das Bauen des Chunks benoetigt werden. Die Arrays werden
    // aus dem Pool geladen, sobald diese verfuegbar stehen.

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

        MeshSize = (Size / CellSize) + 1;
        MeshDataSize = MeshSize + 1;
        MeshBufferSize = MeshDataSize * MeshDataSize * MeshDataSize;

        IndexCacheArray = new NativeArray<int>(INDEX_BUFFER_SIZE, Allocator.Persistent);
        TempIndicesArray = new NativeArray<int>(INDEX_BUFFER_SIZE, Allocator.Persistent);
        TempVerticesArray = new NativeArray<Vertex>(VERTEX_BUFFER_SIZE, Allocator.Persistent);
        SignedDistanceField = new NativeArray<float>(MeshBufferSize, Allocator.Persistent);
        MeshCountsArray = new NativeArray<int>(2, Allocator.Persistent);
    }

    private void OnDestroy()
    {
        if (IndexCacheArray != null)
            IndexCacheArray.Dispose();
        if (TempIndicesArray != null)
            TempIndicesArray.Dispose();
        if (TempVerticesArray != null)
            TempVerticesArray.Dispose();
        if (SignedDistanceField != null)
            SignedDistanceField.Dispose();
        if (MeshCountsArray != null)
            MeshCountsArray.Dispose();
    }

    private void Start()
    {
        var chunkBuilder = new ChunkBuilder
        {
            ChunkMin = Min,
            ChunkSize = MeshSize,
            DataSize = MeshDataSize,
            BufferSize = MeshBufferSize,
            CellSize = CellSize,
            SignedDistanceField = SignedDistanceField,
            IndexCacheArray = IndexCacheArray,
            TempIndicesArray = TempIndicesArray,
            TempVerticesArray = TempVerticesArray,
            MeshCountsArray = MeshCountsArray
        };
        var jobHandle = chunkBuilder.Schedule();
        StartCoroutine(UpdateMeshAsync(jobHandle));
    }

    IEnumerator UpdateMeshAsync(JobHandle job)
    {
        yield return new WaitUntil(() => job.IsCompleted);
        job.Complete();

        var counts = new Counts();
        counts.VertexCount = MeshCountsArray[0];
        counts.IndexCount = MeshCountsArray[1];

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

    /*private void Update()
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
                        ChunkSurface.SetVolumeData(idx, 1);
                    }

                UpdateSurface();
            }
        }
    }*/

}
