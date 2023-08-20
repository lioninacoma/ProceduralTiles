using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;

public class GameGrid
{
    private static readonly int VERTEX_SIZE = 3;
    private static readonly int MAX_VERTICES = 480000;
    private static readonly int MAX_INDICES = 640000;
    private static readonly int VERTEX_BUFFER_SIZE = MAX_VERTICES * VERTEX_SIZE;
    private static readonly int INDEX_BUFFER_SIZE = MAX_INDICES;
    private static readonly int CELL_XY_BUFFER_SIZE = 32000;
    private static readonly int HALFEDGES_BUFFER_SIZE = CELL_XY_BUFFER_SIZE * 6; // edge cell has 2 triangles with each 3 halfedges
    private static readonly bool LERP_POSITION = true;
    private static readonly bool SMOOTH_SHADING = LERP_POSITION;
    private static readonly float DEFAULT_DENSITY_VALUE = float.MaxValue;

    private IrregularGrid baseGrid;
    private int volumeBufferSize, vertexCountXZ;
    private int cellCountY;
    private float cellHeightY;
    private VolumeData[] volume;

    public class VolumeData
    {
        public float Density;
        public short Material;

        public VolumeData()
        {
            Density = DEFAULT_DENSITY_VALUE;
            Material = 0;
        }
    }

    public class CellData
    {
        public int CubeIndex;
        public int EdgeMask;
    }

    public GameGrid(float radius, int cellCountY, float cellHeightY, int div, int seed)
    {
        baseGrid = new IrregularGrid(HALFEDGES_BUFFER_SIZE);
        baseGrid.Build(radius, div, 50, .2f, 0, seed);

        vertexCountXZ = baseGrid.GetVertexCount();
        this.cellCountY = cellCountY;
        this.cellHeightY = cellHeightY;
        volumeBufferSize = vertexCountXZ * (this.cellCountY + 1);

        volume = new VolumeData[volumeBufferSize];
        //Array.Fill(volume, float.MaxValue); 
        for (int i = 0; i < volumeBufferSize; i++)
            volume[i] = new VolumeData();

        //for (int p = 0; p < vertexCountXZ; p++)
        //    for (int y = 0; y < cellCountY + 1; y++)
        //    {
        //        int index = GetVolumeIndex(p, y);
        //        float3 v = baseGrid.GetVertex(p) + new float3(0, y, 0);
        //        volume[index] = Mathf.Min(
        //            SdSphere(v - new float3(0, height * .5f, 0), height * .5f),
        //            SdPlane(v, height * .5f));
        //    }

        //for (int p = 0; p < vertexCountXZ; p++)
        //    for (int y = 0; y < cellCountY + 1; y++)
        //    {
        //        int index = GetVolumeIndex(p, y);
        //        float3 v = baseGrid.GetVertex(p) + new float3(0, y, 0);
        //        volume[index] = fbm_4(v * 0.05f) * 100f;
        //    }

        for (int p = 0; p < vertexCountXZ; p++)
        {
            float3 v = baseGrid.GetVertex(p);
            float d = GetDensity(v * 0.05f) * 10f + 5f;
            
            for (int y = 0; y < this.cellCountY + 1; y++)
            {
                int index = GetVolumeIndex(p, y);
                volume[index].Density = y - d;
            }
        }
    }

    private static float hash1(float n)
    {
        return math.frac(n * 17.0f * math.frac(n * 0.3183099f));
    }

    private static float noise(float3 x)
    {
        float3 p = math.floor(x);
        float3 w = math.frac(x);
        float3 u = w * w * w * (w * (w * 6.0f - 15.0f) + 10.0f);

        float n = p.x + 317.0f * p.y + 157.0f * p.z;

        float a = hash1(n + 0.0f);
        float b = hash1(n + 1.0f);
        float c = hash1(n + 317.0f);
        float d = hash1(n + 318.0f);
        float e = hash1(n + 157.0f);
        float f = hash1(n + 158.0f);
        float g = hash1(n + 474.0f);
        float h = hash1(n + 475.0f);

        float k0 = a;
        float k1 = b - a;
        float k2 = c - a;
        float k3 = e - a;
        float k4 = a - b - c + d;
        float k5 = a - c - e + g;
        float k6 = a - b - e + f;
        float k7 = -a + b + c - d + e - f - g + h;

        return -1.0f + 2.0f * (k0 + k1 * u.x + k2 * u.y + k3 * u.z + k4 * u.x * u.y + k5 * u.y * u.z + k6 * u.z * u.x + k7 * u.x * u.y * u.z);
    }

    private static float3x3 m3 = new float3x3(
        0.0f, 0.8f, 0.6f,
        -0.8f, 0.36f, -0.48f,
        -0.6f, -0.48f, 0.64f);

    private static float GetDensity(float3 x)
    {
        float f = 2.0f;
        float s = 0.5f;
        float a = 0.0f;
        float b = 0.5f;
        for (int i = 0; i < 4; i++)
        {
            float n = noise(x);
            a += b * n;
            b *= s;
            x = f * math.mul(m3, x);
        }
        return a;
    }

    float3 GetNormal(float3 p, float eps)
    {
        float2 h = new float2(eps, 0);
        return math.normalize(new float3(
            GetDensity(p + h.xyy) - GetDensity(p - h.xyy),
            GetDensity(p + h.yxy) - GetDensity(p - h.yxy),
            GetDensity(p + h.yyx) - GetDensity(p - h.yyx)));
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

    public void SetCellVolume(int f, int y, float v, short material)
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

        for (int i = 0; i < 8; ++i)
        {
            var data = volume[volumeIndices[i]];

            /*if (data.Material != material)
            {
                if (data.Density > 0)
                {
                    data.Density = v;
                    data.Material = material;
                    volume[volumeIndices[i]] = data;
                }
            }
            else
            {
                data.Density = v;
                volume[volumeIndices[i]] = data;
            }*/
            data.Density = v;
            data.Material = material;
            volume[volumeIndices[i]] = data;
        }
    }

    public IrregularGrid GetBaseGrid()
    {
        return baseGrid;
    }

    private static int FaceToCellIndex(int f)
    {
        return (f - 2) / 6;
    }

    private int GetVolumeIndex(int p, int y)
    {
        return p + y * vertexCountXZ;
    }

    public void BuildObjectMesh(Mesh mesh, short material, TileMesh[] tileMeshes, Dictionary<int, TilePermutation> tilePermutations)
    {
        float[] vertexBuffer = new float[VERTEX_BUFFER_SIZE];
        int[] indexBuffer = new int[INDEX_BUFFER_SIZE];
        int[] counts = new int[3];

        foreach (int f in baseGrid.GetFaceIndices())
        {
            for (int y = 0; y < cellCountY; y++)
            {
                BuildObjectCell(f, y, counts, indexBuffer, vertexBuffer, material, tileMeshes, tilePermutations);
            }
        }

        if (counts[2] > 0)
        {
            var indices = new int[counts[1]];
            var vertices = new Vector3[counts[2]];

            for (int i = 0; i < counts[2]; i++)
            {
                vertices[i] = new Vector3(
                    vertexBuffer[i * VERTEX_SIZE + 0],
                    vertexBuffer[i * VERTEX_SIZE + 1],
                    vertexBuffer[i * VERTEX_SIZE + 2]);
            }

            for (int i = 0; i < counts[1]; i++)
            {
                indices[i] = indexBuffer[i];
            }

            mesh.SetVertices(vertices);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }
    }

    private unsafe void BuildObjectCell(int f, int y, int[] counts, int[] indexBuffer, float[] vertexBuffer, short material, 
        TileMesh[] tileMeshes, Dictionary<int, TilePermutation> tilePermutations)
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
        int activeCornersMat = 0;
        int activeCornersOther = 0;

        for (i = 0; i < 8; ++i)
        {
            var data = volume[volumeIndices[i]];
            activeCornersMat += (data.Material == material && data.Density < 0) ? 1 : 0;
            activeCornersOther += (data.Material != material && data.Density < 0) ? 1 : 0;
            s = (data.Material == material) ? data.Density : DEFAULT_DENSITY_VALUE;
            grid[i] = s;
            cubeIndex |= (s < 0) ? (1 << i) : 0;
        }

        if (activeCornersMat >= 5 || (activeCornersMat >= 2 && activeCornersMat + activeCornersOther >= 8))
        {
            if (tilePermutations.ContainsKey(cubeIndex))
            {
                var perm = tilePermutations[cubeIndex];
                var tileMesh = tileMeshes[perm.TileIndex];
                tileMesh.GetMesh(counts, indexBuffer, vertexBuffer, baseGrid, baseEdges, y, cellHeightY, perm.YRotation, perm.YMirror);
            }
        }
    }

    public void BuildMesh(Mesh mesh, short material)
    {
        float[] vertexBuffer = new float[VERTEX_BUFFER_SIZE];
        int[] indexBuffer = new int[INDEX_BUFFER_SIZE];
        int[] counts = new int[3];

        foreach (int f in baseGrid.GetFaceIndices())
        {
            for (int y = 0; y < cellCountY; y++)
            {
                BuildCell(f, y, counts, indexBuffer, vertexBuffer, material, LERP_POSITION);
            }
        }
        
        if (counts[2] > 0)
        {
            var indices = new int[counts[1]];
            var vertices = new Vector3[counts[2]];
            
            for (int i = 0; i < counts[2]; i++)
            {
                vertices[i] = new Vector3(
                    vertexBuffer[i * VERTEX_SIZE + 0], 
                    vertexBuffer[i * VERTEX_SIZE + 1], 
                    vertexBuffer[i * VERTEX_SIZE + 2]);
            }

            if (!SMOOTH_SHADING)
            {
                vertices = new Vector3[indices.Length];

                for (int i = 0; i < counts[1]; i++)
                {
                    vertices[i] = new Vector3(
                        vertexBuffer[indexBuffer[i] * VERTEX_SIZE + 0],
                        vertexBuffer[indexBuffer[i] * VERTEX_SIZE + 1],
                        vertexBuffer[indexBuffer[i] * VERTEX_SIZE + 2]);
                }

                for (int i = 0; i < counts[1]; i++)
                {
                    indices[i] = i;
                }
            }
            else
            {
                for (int i = 0; i < counts[1]; i++)
                {
                    indices[i] = indexBuffer[i];
                }
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

    private unsafe void BuildCell(int f, int y, int[] counts, int[] indexBuffer, float[] vertexBuffer, short material, bool lerpPosition = true)
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
            var data = volume[volumeIndices[i]];
            s = (data.Material == material) ? data.Density : DEFAULT_DENSITY_VALUE;
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

            float t = 0.5f;

            if (lerpPosition)
            {
                float at = grid[e[0]];
                float bt = grid[e[1]];
                float dt = at - bt;

                if (Mathf.Abs(dt) > 1e-6)
                    t = at / dt;
            }

            // vertex position
            vp[0] = p[0] + t * MarchingCubes.edgeDirection[i, 0];
            vp[1] = p[1] + t * MarchingCubes.edgeDirection[i, 1];
            vp[2] = p[2] + t * MarchingCubes.edgeDirection[i, 2];

            vp = GridCellLerp(vp, y, baseEdges);

            vertexBuffer[counts[0]++] = vp[0];
            vertexBuffer[counts[0]++] = vp[1] * cellHeightY;
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
