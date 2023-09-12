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
        public Mesh.MeshDataArray MeshData;
    }

    private MeshFilter ChunkMeshFilter;
    private MeshRenderer ChunkMeshRenderer;
    private Mesh ChunkMesh;
    private MeshCollider ChunkMeshCollider;
    private NativeArray<float> Volume;

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
        Volume.Dispose();
    }

    public NativeArray<float> GetVolume()
    {
        return Volume;
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
        Volume[index] = density;
    }

    public void SetCubeVolume(int3 p, int size, bool place)
    {
        int3 la, lb, l;
        int x, y, z;

        la = 1; lb = size;
        for (z = la.z; z <= lb.z; z++)
            for (y = la.y; y <= lb.y; y++)
                for (x = la.x; x <= lb.x; x++)
                {
                    l = p + new int3(x, y, z);
                    SetVolumeData(l, place ? -1 : 1);
                }
    }

    public void InitEmptyBuffers(float defaultSDF)
    {
        Volume = new NativeArray<float>(BufferSize, Allocator.Persistent);

        for (int i = 0; i < BufferSize; i++)
        {
            Volume[i] = defaultSDF;
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

        Volume = data.SDF;
    }

}
