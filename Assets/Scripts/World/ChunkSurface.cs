using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshCollider))]
[RequireComponent(typeof(MeshRenderer))]
public class ChunkSurface : MonoBehaviour
{
    private static readonly int VERTEX_SIZE = 3;
    private static readonly int MAX_VERTICES = 65536;
    private static readonly int MAX_INDICES = MAX_VERTICES * 6;
    private static readonly int VERTEX_BUFFER_SIZE = MAX_VERTICES * VERTEX_SIZE;
    private static readonly int INDEX_BUFFER_SIZE = MAX_INDICES;
    private static readonly float DEFAULT_DENSITY_VALUE = float.MaxValue;

    public int ChunkHeight = 18; // in grid cells

    private MeshFilter SurfaceMeshFilter;
    private MeshRenderer SurfaceMeshRenderer;
    private Mesh SurfaceMesh;
    private MeshCollider SurfaceMeshCollider;
    private IrregularGrid Grid;
    private int GridVertexCount;
    private float GridCellHeight;
    private float[] SurfaceVolume;

    private void Awake()
    {
        SurfaceMeshRenderer = GetComponent<MeshRenderer>();

        if (SurfaceMeshRenderer.sharedMaterial == null)
            SurfaceMeshRenderer.sharedMaterial = Resources.Load<Material>("Materials/MaterialSurface");
        
        SurfaceMeshFilter = GetComponent<MeshFilter>();

        if (SurfaceMeshFilter.sharedMesh == null)
        {
            SurfaceMesh = new Mesh();
            SurfaceMeshFilter.sharedMesh = SurfaceMesh;
        }
        else
        {
            SurfaceMesh = SurfaceMeshFilter.sharedMesh;
        }

        SurfaceMeshCollider = GetComponent<MeshCollider>();
    }

    public void Build(IrregularGrid grid, float gridCellHeight)
    {
        Grid = grid;
        GridCellHeight = gridCellHeight;
        GridVertexCount = Grid.GetVertexCount();

        int volumeBufferSize = GridVertexCount * (ChunkHeight + 1);
        SurfaceVolume = new float[volumeBufferSize];
        System.Array.Fill(SurfaceVolume, DEFAULT_DENSITY_VALUE);

        BuildVolume();
        UpdateMesh();
    }

    public void Flatten(Ray ray, float height, float amount = 1f)
    {
        amount = Mathf.Max(Mathf.Min(1f, amount), 0f);
        int f = RaycastCell(ray);
        Flatten(f, height, amount);
    }

    public void Flatten(int f, float height, float amount = 1f)
    {
        if (f >= 0)
        {
            for (int y = 0; y < ChunkHeight; y++)
                UpdateCellVolume(f, y, VolumeOperation.FLATTEN, height, amount);
        }
    }

    public int RaycastCell(Ray ray)
    {
        foreach (int f in Grid.GetFaceIndices())
        {
            if (RaycastCellIntersection(ray.origin, ray.direction, f))
            {
                return f;
            }
        }

        return -1;
    }

    private bool RaycastCellIntersection(float3 origin, float3 dir, int f)
    {
        int ae = Halfedges.NextHalfedge(f);
        int be = Halfedges.NextHalfedge(ae);
        int ce = Halfedges.NextHalfedge(Grid.GetHalfedge(f));
        int de = Halfedges.NextHalfedge(ce);

        int[] baseEdges = new int[] {
            Grid.GetEdge(ae),
            Grid.GetEdge(be),
            Grid.GetEdge(ce),
            Grid.GetEdge(de)
        };

        var a = Grid.GetVertex(baseEdges[0]);
        var b = Grid.GetVertex(baseEdges[1]);
        var c = Grid.GetVertex(baseEdges[2]);
        var d = Grid.GetVertex(baseEdges[3]);

        return Utils.RayTriangleIntersection(origin, dir, a, b, c, out _, out _, out _, out _)
            || Utils.RayTriangleIntersection(origin, dir, c, d, a, out _, out _, out _, out _);
    }

    public enum VolumeOperation
    {
        FLATTEN, ADD, SET
    }

    public void UpdateCellVolume(int f, int y, VolumeOperation op, params float[] updateParams)
    {
        float v0 = (updateParams.Length > 0) ? updateParams[0] : 0f;
        float v1 = (updateParams.Length > 1) ? updateParams[1] : 0f;

        int a = Halfedges.NextHalfedge(f);
        int b = Halfedges.NextHalfedge(a);
        int c = Halfedges.NextHalfedge(Grid.GetHalfedge(f));
        int d = Halfedges.NextHalfedge(c);

        int[] baseEdges = new int[] {
            Grid.GetEdge(a),
            Grid.GetEdge(b),
            Grid.GetEdge(c),
            Grid.GetEdge(d)
        };

        float3[] points = new float3[]
        {
            GetCellPoint(baseEdges[1], y),
            GetCellPoint(baseEdges[0], y),
            GetCellPoint(baseEdges[0], y + 1),
            GetCellPoint(baseEdges[1], y + 1),
            GetCellPoint(baseEdges[2], y),
            GetCellPoint(baseEdges[3], y),
            GetCellPoint(baseEdges[3], y + 1),
            GetCellPoint(baseEdges[2], y + 1)
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
            switch (op)
            {
                case VolumeOperation.FLATTEN:
                    {
                        float3 point = points[i];
                        float current = SurfaceVolume[volumeIndices[i]];
                        float target = point.y - v0;
                        SurfaceVolume[volumeIndices[i]] += (target - current) * v1;
                    }
                    break;
                case VolumeOperation.SET:
                    SurfaceVolume[volumeIndices[i]] = v0;
                    break;
                case VolumeOperation.ADD:
                default:
                    SurfaceVolume[volumeIndices[i]] += v0;
                    break;
            }
        }
    }

    private float3 GetCellPoint(int p, int y)
    {
        return Grid.GetVertex(p) + new float3(0, y, 0);
    }

    private int GetVolumeIndex(int p, int y)
    {
        return p + y * GridVertexCount;
    }

    private void BuildVolume()
    {
        //for (int p = 0; p < GridVertexCount; p++)
        //{
        //    float3 v = Grid.GetVertex(p);
        //    float d = Noise.FBM_4(v * 0.04f) * 10f + 5f;
        //    for (int y = 0; y < ChunkHeight + 1; y++)
        //    {
        //        int index = GetVolumeIndex(p, y);
        //        SurfaceVolume[index] = y - d;
        //    }
        //}

        for (int p = 0; p < GridVertexCount; p++)
            for (int y = 0; y < ChunkHeight + 1; y++)
            {
                int index = GetVolumeIndex(p, y);
                float3 v = Grid.GetVertex(p) + new float3(0, y, 0);
                SurfaceVolume[index] = Noise.FBM_4(v * 0.05f) * 100f;
            }
    }

    public void UpdateMesh()
    {
        float[] vertexBuffer = new float[VERTEX_BUFFER_SIZE];
        int[] indexBuffer = new int[INDEX_BUFFER_SIZE];
        int[] counts = new int[3];

        foreach (int f in Grid.GetFaceIndices())
        {
            for (int y = 0; y < ChunkHeight; y++)
            {
                BuildCell(f, y, counts, indexBuffer, vertexBuffer);
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

            SurfaceMesh.Clear();
            SurfaceMesh.SetVertices(vertices);
            SurfaceMesh.SetIndices(indices, MeshTopology.Triangles, 0);
            SurfaceMesh.RecalculateNormals();
            SurfaceMesh.RecalculateBounds();
            SurfaceMeshCollider.sharedMesh = SurfaceMesh;
        }
    }

    private unsafe float* GridCellLerp(float* vp, float yOffset, int[] baseEdges)
    {
        var a = Grid.GetVertex(baseEdges[0]);
        var b = Grid.GetVertex(baseEdges[1]);
        var c = Grid.GetVertex(baseEdges[2]);
        var d = Grid.GetVertex(baseEdges[3]);

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
        int c = Halfedges.NextHalfedge(Grid.GetHalfedge(f));
        int d = Halfedges.NextHalfedge(c);

        int[] baseEdges = new int[] {
            Grid.GetEdge(a),
            Grid.GetEdge(b),
            Grid.GetEdge(c),
            Grid.GetEdge(d)
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
            s = SurfaceVolume[volumeIndices[i]];
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
            float at = grid[e[0]];
            float bt = grid[e[1]];
            float dt = at - bt;

            if (Mathf.Abs(dt) > 1e-6)
                t = at / dt;

            // vertex position
            vp[0] = p[0] + t * MarchingCubes.edgeDirection[i, 0];
            vp[1] = p[1] + t * MarchingCubes.edgeDirection[i, 1];
            vp[2] = p[2] + t * MarchingCubes.edgeDirection[i, 2];

            vp = GridCellLerp(vp, y, baseEdges);

            vertexBuffer[counts[0]++] = vp[0];
            vertexBuffer[counts[0]++] = vp[1] * GridCellHeight;
            vertexBuffer[counts[0]++] = vp[2];

            counts[2]++;
        }

        for (i = 0; MarchingCubes.triTable[cubeIndex, i] != -1; i += 3)
        {
            indexBuffer[counts[1]++] = edges[MarchingCubes.triTable[cubeIndex, i]];
            indexBuffer[counts[1]++] = edges[MarchingCubes.triTable[cubeIndex, i + 1]];
            indexBuffer[counts[1]++] = edges[MarchingCubes.triTable[cubeIndex, i + 2]];
        }
    }
}
