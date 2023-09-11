using MBaske.Octree;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshCollider))]
[RequireComponent(typeof(MeshRenderer))]
public class Chunk : MonoBehaviour, INodeContent
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

    public int3 ChunkMin { get; set; }
    public int ChunkSize { get; set; }
    public Bounds Bounds { get; set; }
    public Vector3 Position { get; set; }

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
        int index = Utils.I3(p.x, p.y, p.z, DataSize, DataSize);
        if (index < 0 || index >= BufferSize) return;
        SignedDistanceField[index] = density;
    }

    public void SetCentroidCell(int3 p, bool isCentroid)
    {
        int index = Utils.I3(p.x, p.y, p.z, DataSize, DataSize);
        if (index < 0 || index >= BufferSize) return;
        CentroidCells[index] = isCentroid;
    }

    public void SetVoxelCubeOnGrid(int3 p, int size, bool place)
    {
        int3 gridPos = (p / size) * size;
        int3 la, lb, l;
        int x, y, z;

        //la = 0; lb = size;
        //for (z = la.z; z <= lb.z; z++)
        //    for (y = la.y; y <= lb.y; y++)
        //        for (x = la.x; x <= lb.x; x++)
        //        {
        //            l = gridPos + new int3(x, y, z);
        //            SetCentroidCell(l - 1, !place);
        //        }

        la = 0; lb = size - 1;
        for (z = la.z; z <= lb.z; z++)
            for (y = la.y; y <= lb.y; y++)
                for (x = la.x; x <= lb.x; x++)
                {
                    l = gridPos + new int3(x, y, z);
                    SetVolumeData(l, (place) ? 0 : 1);
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
