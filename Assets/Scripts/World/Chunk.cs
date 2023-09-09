using Unity.Collections;
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
    private NativeArray<float> SignedDistanceField;

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
    }

    private void OnDestroy()
    {
        SignedDistanceField.Dispose();
    }

    public NativeArray<float> GetSignedDistanceField()
    {
        return SignedDistanceField;
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
