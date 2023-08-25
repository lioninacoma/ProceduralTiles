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

    public void GetMesh(int[] counts, int[] indexBuffer, float[] vertexBuffer, IrregularGrid grid, int[] baseEdges, float yOffset, float cellHeightY, float yRotation, float yMirror)
    {
        int i;
        Vector3 p;
        float3 q, r, v;
        int indexCount = (int)mesh.GetIndexCount(0);

        var a = grid.GetVertex(baseEdges[0]);
        var b = grid.GetVertex(baseEdges[1]);
        var c = grid.GetVertex(baseEdges[2]);
        var d = grid.GetVertex(baseEdges[3]);
        var rot = (yRotation > 0f) ? Quaternion.Euler(0, yRotation, 0) : Quaternion.identity;
        var scl = (yMirror < 0f) ? Quaternion.Euler(0, 180f, 180f) : Quaternion.identity;
        var cnt = new Vector3(.5f, .5f, .5f);
        int indexOffset = counts[2];

        for (i = 0; i < mesh.vertexCount; i++)
        {
            p = vertices[i];

            if (yMirror < 0f)
            {
                //p = (scl * (p - cnt)) + cnt;
                p.y = (yMirror * (p.y - .5f)) + .5f;
            }

            if (yRotation > 0f)
            {
                p = (rot * (p - cnt)) + cnt;
            }

            q = math.lerp(a, b, p.x);
            r = math.lerp(d, c, p.x);
            v = math.lerp(r, q, p.z);

            vertexBuffer[counts[0]++] = v.x;
            vertexBuffer[counts[0]++] = (p.y + yOffset) * cellHeightY;
            vertexBuffer[counts[0]++] = v.z;

            counts[2]++;
        }

        for (i = 0; i < indexCount; i += 3)
        {
            if (yMirror < 0f)
            {
                indexBuffer[counts[1]++] = indexOffset + indices[i];
                indexBuffer[counts[1]++] = indexOffset + indices[i + 2];
                indexBuffer[counts[1]++] = indexOffset + indices[i + 1];
            }
            else
            {
                indexBuffer[counts[1]++] = indexOffset + indices[i];
                indexBuffer[counts[1]++] = indexOffset + indices[i + 1];
                indexBuffer[counts[1]++] = indexOffset + indices[i + 2];
            }
        }
    }

    public void Dispose()
    {
        verticesArray.Dispose();
        indicesArray.Dispose();
    }
}
