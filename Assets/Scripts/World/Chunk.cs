using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshCollider))]
[RequireComponent(typeof(MeshRenderer))]
public class Chunk : MonoBehaviour
{    
    public class Data
    {
        public NativeArray<float> SDF;
        public NativeArray<bool> CCs;
        public Mesh.MeshDataArray MeshData;
    }

    private MeshFilter ChunkMeshFilter;
    private MeshRenderer ChunkMeshRenderer;
    private Mesh ChunkMesh;
    private MeshCollider ChunkMeshCollider;
    private NativeArray<float> SignedDistanceField;
    private NativeArray<bool> CentroidCells;

    private int BufferSize;
    private int DataSize;

    public int3 Min { get; set; }
    public int Size { get; set; }

    private void Awake()
    {
        var world = GetComponentInParent<World>();
        world.GetChunkMeta(out _, out _, out DataSize, out BufferSize);

        gameObject.layer = LayerMask.NameToLayer("Chunk");
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
    }

    private void OnDestroy()
    {
        SignedDistanceField.Dispose();
        CentroidCells.Dispose();
    }

    public NativeArray<float> GetVolume()
    {
        return SignedDistanceField;
    }

    public NativeArray<bool> GetCentroidCells()
    {
        return CentroidCells;
    }

    public void SetVolumeData(int3 p, float density)
    {
        if (p.x < 0 || 
            p.y < 0 || 
            p.z < 0 || 
            p.x >= DataSize ||
            p.y >= DataSize || 
            p.z >= DataSize)
        {
            return;
        }

        int index = Utils.I3(p.x, p.y, p.z, DataSize, DataSize);
        SignedDistanceField[index] = density;
    }

    public void SetCentroidCell(int3 p, bool isCentroid)
    {
        if (p.x < 0 ||
            p.y < 0 ||
            p.z < 0 ||
            p.x >= DataSize ||
            p.y >= DataSize ||
            p.z >= DataSize)
        {
            return;
        }

        int index = Utils.I3(p.x, p.y, p.z, DataSize, DataSize);
        CentroidCells[index] = isCentroid;
    }

    public void SetVoxelCube(int3 p, int size, bool place)
    {
        int3 la, lb, l;
        int x, y, z;

        la = 1; lb = size - 1;
        for (z = la.z; z <= lb.z; z++)
            for (y = la.y; y <= lb.y; y++)
                for (x = la.x; x <= lb.x; x++)
                {
                    l = p + new int3(x, y, z);
                    SetCentroidCell(l, true);
                }

        la = 1; lb = size;
        for (z = la.z; z <= lb.z; z++)
            for (y = la.y; y <= lb.y; y++)
                for (x = la.x; x <= lb.x; x++)
                {
                    l = p + new int3(x, y, z);
                    SetVolumeData(l, place ? -1 : 1);
                }
    }

    public void InitEmptyBuffers(float defaultSDF, bool defaultCC)
    {
        SignedDistanceField = new NativeArray<float>(BufferSize, Allocator.Persistent);
        CentroidCells = new NativeArray<bool>(BufferSize, Allocator.Persistent);

        for (int i = 0; i < BufferSize; i++)
        {
            SignedDistanceField[i] = defaultSDF;
            CentroidCells[i] = defaultCC;
        }
    }

    public void SetChunkData(Data data)
    {
        ChunkMesh.Clear();

        var flags =
              MeshUpdateFlags.DontRecalculateBounds
            | MeshUpdateFlags.DontValidateIndices
            | MeshUpdateFlags.DontNotifyMeshUsers
            | MeshUpdateFlags.DontResetBoneBounds;
        Mesh.ApplyAndDisposeWritableMeshData(data.MeshData, ChunkMesh, flags);

        ChunkMesh.RecalculateBounds();
        ChunkMesh.RecalculateNormals();
        ChunkMeshCollider.sharedMesh = ChunkMesh;

        SignedDistanceField = data.SDF;
        CentroidCells = data.CCs;
    }

}
