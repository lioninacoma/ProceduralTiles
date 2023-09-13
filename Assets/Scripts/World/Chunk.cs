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

    public float GetVolumeData(int3 p)
    {
        int index = Utils.I3(p.x, p.y, p.z, DataSize, DataSize);
        if (index < 0 || index >= BufferSize) return -100000f;
        return Volume[index];
    }

    public void SetVolumeData(int3 p, float density)
    {
        int index = Utils.I3(p.x, p.y, p.z, DataSize, DataSize);
        if (index < 0 || index >= BufferSize) return;
        Volume[index] = density;
    }

    public void SetCubeVolume(int3 p, int size, bool place, float smooth)
    {
        int3 la, lb, l;
        int x, y, z;
        float d0, d1, d;
        const float eps = .1f;
        float r = size * .5f;
        float3 s, c = new float3(p) + r;

        la = -(int)smooth; lb = size + (int)math.ceil(smooth);
        for (z = la.z; z <= lb.z; z++)
            for (y = la.y; y <= lb.y; y++)
                for (x = la.x; x <= lb.x; x++)
                {
                    l = p + new int3(x, y, z);

                    if (l.x < 0 ||
                        l.y < 0 ||
                        l.z < 0 ||
                        l.x >= DataSize ||
                        l.y >= DataSize ||
                        l.z >= DataSize)
                    {
                        continue;
                    }

                    s = l;
                    d0 = GetVolumeData(l);
                    d1 = Csg.SdBox(s - c, r + eps);
                    d = place 
                        ? Csg.OpUnionSmooth(d0, d1, smooth) 
                        : Csg.OpSubtractSmooth(d1, d0, smooth);
                    SetVolumeData(l, d);
                }
    }

    public void ClearMesh()
    {
        ChunkMesh.Clear();
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
