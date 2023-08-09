using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteInEditMode]
public unsafe class WorldGenerator : MonoBehaviour
{
    private static readonly VertexAttributeDescriptor[] VERTEX_ATTRIBUTES = new[] {
        new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3)
    };

    private static readonly IndexFormat INDEX_FORMAT = IndexFormat.UInt32;

    [Range(1f, 1000f)] public float radius = 3f;
    [Range(2, 80)] public int div = 3;
    [Range(0, 100)] public int relaxIterations = 50;
    [Range(0, .46f)] public float relaxScale = .1f;
    [Range(0, 1)] public int relaxType = 0;

    public Transform asset;
    public Material assetMaterial;

    private Grid grid;
    private Mesh mesh;
    private NativeArray<Vector3> verticesArray;
    private Vector3* vertices;
    private NativeArray<ushort> indicesArray;
    private ushort* indices;

    void OnEnable()
    {
        InitGrid();
    }

    void OnDisable()
    {
        verticesArray.Dispose();
        indicesArray.Dispose();
    }

    void OnValidate()
    {
        InitGrid();
    }

    private void InitGrid()
    {
        grid = new Grid();
        grid.Build(radius, div, relaxIterations, relaxScale, relaxType);
    }

    // Start is called before the first frame update
    void Start()
    {
        mesh = null;
        verticesArray = default;
        indicesArray = default;

        if (asset != null)
        {
            mesh = asset.GetComponent<MeshFilter>().sharedMesh;
            using (var dataArray = Mesh.AcquireReadOnlyMeshData(mesh))
            {
                var data = dataArray[0];
                int indexCount = (int)mesh.GetIndexCount(0);

                verticesArray = new NativeArray<Vector3>(mesh.vertexCount, Allocator.Persistent);
                indicesArray = new NativeArray<ushort>(indexCount, Allocator.Persistent);

                data.GetVertices(verticesArray);
                vertices = (Vector3*)NativeArrayUnsafeUtility.GetUnsafePtr(verticesArray);

                data.GetIndices(indicesArray, 0);
                indices = (ushort*)NativeArrayUnsafeUtility.GetUnsafePtr(indicesArray);
            }
        }
    }

    private Cell debugCell;

    private GameObject CreateObject()
    {
        var obj = new GameObject("Object");
        obj.transform.SetParent(transform);

        var meshFilter = obj.AddComponent<MeshFilter>();
        meshFilter.mesh = new Mesh();

        var meshRenderer = obj.AddComponent<MeshRenderer>();
        meshRenderer.material = assetMaterial;

        return obj;
    }

    private void SpawnAssetOnCell(Cell cell)
    {
        int i;
        Vector3 p;
        float3 q, r, v;
        int indexCount = (int)mesh.GetIndexCount(0);

        var meshData = Mesh.AllocateWritableMeshData(1);
        var data = meshData[0];

        data.SetVertexBufferParams(mesh.vertexCount, VERTEX_ATTRIBUTES);
        data.SetIndexBufferParams(indexCount, INDEX_FORMAT);

        var vertexBuffer = data.GetVertexData<Vector3>();
        var indexBuffer = data.GetIndexData<uint>();

        var vertexBufferPtr = (Vector3*)NativeArrayUnsafeUtility.GetUnsafePtr(vertexBuffer);
        var indexBufferPtr = (uint*)NativeArrayUnsafeUtility.GetUnsafePtr(indexBuffer);

        var a = grid.GetVertex(cell.Points[0]);
        var b = grid.GetVertex(cell.Points[1]);
        var c = grid.GetVertex(cell.Points[2]);
        var d = grid.GetVertex(cell.Points[3]);

        for (i = 0; i < mesh.vertexCount; i++)
        {
            p = vertices[i];
            q = math.lerp(a, b, p.x * 0.5f + 0.5f);
            r = math.lerp(d, c, p.x * 0.5f + 0.5f);
            v = math.lerp(r, q, p.z * 0.5f + 0.5f);
            vertexBufferPtr[i] = new Vector3(v.x, p.y, v.z);
        }

        for (i = 0; i < indexCount; i++)
            indexBufferPtr[i] = indices[i];

        data.subMeshCount = 1;
        data.SetSubMesh(0, new SubMeshDescriptor(0, indexCount));

        var obj = CreateObject();
        var meshFilter = obj.GetComponent<MeshFilter>();

        // apply mesh data
        var flags =
              MeshUpdateFlags.DontRecalculateBounds
            | MeshUpdateFlags.DontValidateIndices
            | MeshUpdateFlags.DontNotifyMeshUsers
            | MeshUpdateFlags.DontResetBoneBounds;
        Mesh.ApplyAndDisposeWritableMeshData(meshData, meshFilter.mesh, flags);

        meshFilter.mesh.RecalculateNormals();
        meshFilter.mesh.RecalculateBounds();
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetMouseButtonUp(0))
        {
            var camera = Camera.main;
            var mouseRay = camera.ScreenPointToRay(Input.mousePosition);
            var cell = grid.RaycastCell(mouseRay, transform);

            if (cell != null)
            {
                debugCell = cell;
                SpawnAssetOnCell(cell);
            }
        }
    }

    void OnDrawGizmos()
    {
        if (grid != null)
        {
            grid.DrawTriangles(transform);
        }

        if (debugCell != null)
        {
            //Gizmos.color = Color.green;
            grid.DrawCell(debugCell, transform);
            //grid.DrawCellNeighbours(debugCell, transform);
        }
    }
}
