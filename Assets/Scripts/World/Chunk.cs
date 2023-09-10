using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using static UnityEditor.ObjectChangeEventStream;

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
    private NativeArray<float> SignedDistanceField;

    private int BufferSize;
    private int DataSize;
    private int CellSize;
    private int ChunkSize;

    private void Awake()
    {
        var world = GetComponentInParent<World>();
        world.GetChunkMeta(out CellSize, out ChunkSize, out DataSize, out BufferSize);

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
    }

    public NativeArray<float> GetVolume()
    {
        return SignedDistanceField;
    }

    public void SetVolumeData(int3 p, float density)
    {
        int index = Utils.I3(p.x, p.y, p.z, DataSize, DataSize);
        if (index < 0 || index >= BufferSize) return;
        SignedDistanceField[index] = density;
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
    }

}
