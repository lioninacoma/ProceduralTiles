using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

public unsafe class TileMesh
{
    private static readonly VertexAttributeDescriptor[] VERTEX_ATTRIBUTES = new[] {
        new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3)
    };

    private static readonly IndexFormat INDEX_FORMAT = IndexFormat.UInt32;

    public string tileName = "";
    public Mesh mesh = null;
    public NativeArray<Vector3> verticesArray;
    public Vector3* vertices;
    public NativeArray<ushort> indicesArray;
    public ushort* indices;

    public void InitFromTile(Tile tile)
    {
        verticesArray = default;
        indicesArray = default;

        if (tile.Asset != null)
        {
            tileName = tile.name;
            mesh = tile.Asset.GetComponent<MeshFilter>().sharedMesh;
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

    public void GetMesh(MeshFilter meshFilter, Cell cell, Grid grid)
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

    public void Dispose()
    {
        verticesArray.Dispose();
        indicesArray.Dispose();
    }
}
