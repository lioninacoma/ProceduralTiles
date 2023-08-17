using System;
using System.Linq;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

public class GameGrid
{
    private static readonly int VERTEX_SIZE = 3;
    private static readonly int MAX_VERTICES = 48000;
    private static readonly int MAX_INDICES = 64000;
    private static readonly int VERTEX_BUFFER_SIZE = MAX_VERTICES * VERTEX_SIZE;
    private static readonly int INDEX_BUFFER_SIZE = MAX_INDICES;
    private static readonly int CELL_XY_BUFFER_SIZE = 32000;
    private static readonly int HALFEDGES_BUFFER_SIZE = CELL_XY_BUFFER_SIZE * 6; // edge cell has 2 triangles with each 3 halfedges

    private IrregularGrid baseGrid;
    private int volumeBufferSize, vertexCountXZ;
    private int cellCountY;
    private float[] volume;

    public GameGrid(float radius, int height, int div, int seed)
    {
        baseGrid = new IrregularGrid(HALFEDGES_BUFFER_SIZE);
        baseGrid.Build(radius, div, 50, .2f, 0, seed);

        vertexCountXZ = baseGrid.GetVertexCount();
        cellCountY = height;
        volumeBufferSize = vertexCountXZ * (cellCountY + 1);

        volume = new float[volumeBufferSize];
        Array.Fill(volume, float.MaxValue);

        //for (int p = 0; p < vertexCountXZ; p++)
        //    for (int y = 0; y < cellCountY + 1; y++)
        //    {
        //        int index = GetVolumeIndex(p, y);
        //        float3 v = baseGrid.GetVertex(p) + new float3(0, y, 0);
        //        volume[index] = SdSphere(v - new float3(0, height * .5f, 0), height * .5f);
        //    }

        for (int p = 0; p < vertexCountXZ; p++)
            for (int y = 0; y < cellCountY + 1; y++)
            {
                int index = GetVolumeIndex(p, y);
                float3 v = baseGrid.GetVertex(p) + new float3(0, y, 0);
                volume[index] = SdPlane(v, 0f);
            }
    }

    private static float SdSphere(float3 p, float s)
    {
        return math.length(p) - s;
    }

    private static float SdPlane(float3 p, float h)
    {
        return p.y - h;
    }

    public int RaycastCell(Ray ray, Transform transform)
    {
        foreach (int f in baseGrid.GetFaceIndices())
        {
            if (RaycastCellIntersection(ray.origin, ray.direction, transform, f))
            {
                return f;
            }
        }

        return -1;
    }

    private bool RaycastCellIntersection(float3 origin, float3 dir, Transform transform, int f)
    {
        int ae = Halfedges.NextHalfedge(f);
        int be = Halfedges.NextHalfedge(ae);
        int ce = Halfedges.NextHalfedge(baseGrid.GetHalfedge(f));
        int de = Halfedges.NextHalfedge(ce);

        int[] baseEdges = new int[] {
            baseGrid.GetEdge(ae),
            baseGrid.GetEdge(be),
            baseGrid.GetEdge(ce),
            baseGrid.GetEdge(de)
        };

        var a = baseGrid.GetVertex(baseEdges[0]);
        var b = baseGrid.GetVertex(baseEdges[1]);
        var c = baseGrid.GetVertex(baseEdges[2]);
        var d = baseGrid.GetVertex(baseEdges[3]);

        return Utils.RayTriangleIntersection(origin, dir, a, b, c, out _, out _, out _, out _)
            || Utils.RayTriangleIntersection(origin, dir, c, d, a, out _, out _, out _, out _);
    }

    public void SetCellVolume(int f, int y, float v)
    {
        int a = Halfedges.NextHalfedge(f);
        int b = Halfedges.NextHalfedge(a);
        int c = Halfedges.NextHalfedge(baseGrid.GetHalfedge(f));
        int d = Halfedges.NextHalfedge(c);

        int[] baseEdges = new int[] {
            baseGrid.GetEdge(a),
            baseGrid.GetEdge(b),
            baseGrid.GetEdge(c),
            baseGrid.GetEdge(d)
        };

        int[] volumeIndices = new int[] {
            GetVolumeIndex(baseEdges[1], y),
            GetVolumeIndex(baseEdges[0], y),
            GetVolumeIndex(baseEdges[0], y + 1),
            GetVolumeIndex(baseEdges[1], y + 1),
            GetVolumeIndex(baseEdges[2], y),
            GetVolumeIndex(baseEdges[3], y),
            GetVolumeIndex(baseEdges[3], y + 1),
            GetVolumeIndex(baseEdges[2], y + 1)
        };

        for (int i = 0; i < 8; i++)
        {
            volume[volumeIndices[i]] = v;
        }
    }

    public IrregularGrid GetBaseGrid()
    {
        return baseGrid;
    }

    private int GetVolumeIndex(int p, int y)
    {
        return p + y * vertexCountXZ;
    }

    public void BuildMesh(Mesh mesh)
    {
        float[] vertexBuffer = new float[VERTEX_BUFFER_SIZE];
        int[] indexBuffer = new int[INDEX_BUFFER_SIZE];
        int[] counts = new int[3];

        foreach (int f in baseGrid.GetFaceIndices())
        {
            for (int y = 0; y < cellCountY; y++)
            {
                BuildCell(f, y, counts, indexBuffer, vertexBuffer);
            }
        }
        
        if (counts[2] > 0)
        {
            var indices = new int[counts[1]];
            var vertices = new Vector3[counts[2]];

            for (int i = 0; i < counts[1]; i++)
                indices[i] = indexBuffer[i];

            for (int i = 0; i < counts[2]; i++)
            {
                vertices[i] = new Vector3(
                    vertexBuffer[i * VERTEX_SIZE + 0], 
                    vertexBuffer[i * VERTEX_SIZE + 1], 
                    vertexBuffer[i * VERTEX_SIZE + 2]);
            }

            mesh.SetVertices(vertices);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }
    }

    private unsafe float* GridCellLerp(float* vp, float yOffset, int[] baseEdges)
    {
        var a = baseGrid.GetVertex(baseEdges[0]);
        var b = baseGrid.GetVertex(baseEdges[1]);
        var c = baseGrid.GetVertex(baseEdges[2]);
        var d = baseGrid.GetVertex(baseEdges[3]);

        float3 q = math.lerp(a, b, 1f - vp[0]);
        float3 r = math.lerp(d, c, 1f - vp[0]);
        float3 v = math.lerp(r, q, 1f - vp[2]);

        vp[0] = v.x; vp[1] = vp[1] + yOffset; vp[2] = v.z;
        return vp;
    }

    private unsafe void BuildCell(int f, int y, int[] counts, int[] indexBuffer, float[] vertexBuffer)
    {
        int i;
        float s;

        var e = stackalloc int[2];
        var v = stackalloc int[3];
        var p = stackalloc float[3];
        var vp = stackalloc float[3];
        var vn = stackalloc float[3];
        var grid = stackalloc float[8];
        var edges = stackalloc int[12];

        int a = Halfedges.NextHalfedge(f);
        int b = Halfedges.NextHalfedge(a);
        int c = Halfedges.NextHalfedge(baseGrid.GetHalfedge(f));
        int d = Halfedges.NextHalfedge(c);

        int[] baseEdges = new int[] {
            baseGrid.GetEdge(a),
            baseGrid.GetEdge(b),
            baseGrid.GetEdge(c),
            baseGrid.GetEdge(d)
        };

        int[] volumeIndices = new int[] { 
            GetVolumeIndex(baseEdges[1], y),
            GetVolumeIndex(baseEdges[0], y),
            GetVolumeIndex(baseEdges[0], y + 1),
            GetVolumeIndex(baseEdges[1], y + 1),
            GetVolumeIndex(baseEdges[2], y),
            GetVolumeIndex(baseEdges[3], y),
            GetVolumeIndex(baseEdges[3], y + 1),
            GetVolumeIndex(baseEdges[2], y + 1)
        };

        int cubeIndex = 0;

        for (i = 0; i < 8; ++i)
        {
            s = volume[volumeIndices[i]];
            grid[i] = s;
            cubeIndex |= (s > 0) ? (1 << i) : 0;
        }

        int edgeMask = MarchingCubes.edgeTable[cubeIndex];
        if (edgeMask == 0 || edgeMask == 0xFF)
            return;

        for (i = 0; i < 12; ++i)
        {
            if ((edgeMask & (1 << i)) == 0)
            {
                edges[i] = -1;
                continue;
            }

            edges[i] = counts[2];

            e[0] = MarchingCubes.edgeIndex[i, 0];
            e[1] = MarchingCubes.edgeIndex[i, 1];

            p[0] = MarchingCubes.cubeVerts[e[0], 0];
            p[1] = MarchingCubes.cubeVerts[e[0], 1];
            p[2] = MarchingCubes.cubeVerts[e[0], 2];

            //float at = grid[e[0]];
            //float bt = grid[e[1]];
            //float dt = at - bt;
            //float t = 0;

            //if (Mathf.Abs(dt) > 1e-6)
            //    t = at / dt;

            float t = 0.5f;

            // vertex position
            vp[0] = p[0] + t * MarchingCubes.edgeDirection[i, 0];
            vp[1] = p[1] + t * MarchingCubes.edgeDirection[i, 1];
            vp[2] = p[2] + t * MarchingCubes.edgeDirection[i, 2];

            vp = GridCellLerp(vp, y, baseEdges);

            vertexBuffer[counts[0]++] = vp[0];
            vertexBuffer[counts[0]++] = vp[1];
            vertexBuffer[counts[0]++] = vp[2];

            // vertex normal
            //vertexBuffer[counts[0]++] = 0;
            //vertexBuffer[counts[0]++] = 0;
            //vertexBuffer[counts[0]++] = 0;

            counts[2]++;
        }

        for (i = 0; MarchingCubes.triTable[cubeIndex, i] != -1; i += 3)
        {
            indexBuffer[counts[1]++] = edges[MarchingCubes.triTable[cubeIndex, i]];
            indexBuffer[counts[1]++] = edges[MarchingCubes.triTable[cubeIndex, i + 1]];
            indexBuffer[counts[1]++] = edges[MarchingCubes.triTable[cubeIndex, i + 2]];
        }
    }

    private static readonly Color[] DEBUG_COLORS = new Color[]
    {
        Color.green,
        Color.red,
        Color.blue,
        Color.yellow,
        Color.magenta
    };

    public void DrawCell(int f, Transform transform)
    {
        int ae = Halfedges.NextHalfedge(f);
        int be = Halfedges.NextHalfedge(ae);
        int ce = Halfedges.NextHalfedge(baseGrid.GetHalfedge(f));
        int de = Halfedges.NextHalfedge(ce);

        int[] baseEdges = new int[] {
            baseGrid.GetEdge(ae),
            baseGrid.GetEdge(be),
            baseGrid.GetEdge(ce),
            baseGrid.GetEdge(de)
        };

        var a = baseGrid.GetVertex(baseEdges[0]);
        var b = baseGrid.GetVertex(baseEdges[1]);
        var c = baseGrid.GetVertex(baseEdges[2]);
        var d = baseGrid.GetVertex(baseEdges[3]);

        Gizmos.color = DEBUG_COLORS[0];
        Gizmos.DrawLine(
            transform.TransformPoint(a),
            transform.TransformPoint(b));
        Gizmos.color = DEBUG_COLORS[1];
        Gizmos.DrawLine(
            transform.TransformPoint(b),
            transform.TransformPoint(c));
        Gizmos.color = DEBUG_COLORS[2];
        Gizmos.DrawLine(
            transform.TransformPoint(c),
            transform.TransformPoint(d));
        Gizmos.color = DEBUG_COLORS[3];
        Gizmos.DrawLine(
            transform.TransformPoint(d),
            transform.TransformPoint(a));
    }
}
