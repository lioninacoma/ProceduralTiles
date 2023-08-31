using Priority_Queue;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshCollider))]
[RequireComponent(typeof(MeshRenderer))]
public unsafe class IGSurface : MonoBehaviour
{
    private static readonly VertexAttributeDescriptor[] VERTEX_ATTRIBUTES = new[] {
        new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3)
    };

    private static readonly IndexFormat INDEX_FORMAT = IndexFormat.UInt32;
    private static readonly int VERTEX_SIZE = 3;
    private static readonly int MAX_VERTICES = 65536;
    private static readonly int MAX_INDICES = MAX_VERTICES * 6;
    private static readonly int VERTEX_BUFFER_SIZE = MAX_VERTICES * VERTEX_SIZE;
    private static readonly int INDEX_BUFFER_SIZE = MAX_INDICES;
    private static readonly float DEFAULT_DENSITY_VALUE = float.MaxValue;

    public int ChunkHeight = 18; // in grid cells
    public int ChunkResolution = 0;

    private MeshFilter SurfaceMeshFilter;
    private MeshRenderer SurfaceMeshRenderer;
    private Mesh SurfaceMesh;
    private MeshCollider SurfaceMeshCollider;
    private IrregularGrid Grid;
    private int GridVertexCount;
    private int GridSubCellCount;
    private int GridSubCellSize;
    private int GridSubCellDataCount;
    private int GridSubCellDataSize;
    private float GridCellHeight;
    private float[] SurfaceVolume;
    NativeArray<float> TempVerticesArray;
    NativeArray<int> TempIndicesArray;
    private float* TempVertices;
    private int* TempIndices;

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

        TempVerticesArray = new NativeArray<float>(VERTEX_BUFFER_SIZE, Allocator.Persistent);
        TempIndicesArray = new NativeArray<int>(INDEX_BUFFER_SIZE, Allocator.Persistent);
        TempVertices = (float*)NativeArrayUnsafeUtility.GetUnsafePtr(TempVerticesArray);
        TempIndices = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(TempIndicesArray);
    }

    private void OnDisable()
    {
        TempVerticesArray.Dispose();
        TempIndicesArray.Dispose();
    }

    public void Build(IrregularGrid grid, float gridCellHeight)
    {
        Grid = grid;
        GridCellHeight = gridCellHeight;
        GridVertexCount = Grid.GetVertexCount();

        GridSubCellSize = (int)Mathf.Pow(2, ChunkResolution);
        GridSubCellCount = GridSubCellSize * GridSubCellSize * GridSubCellSize;

        GridSubCellDataSize = GridSubCellSize + 1;
        GridSubCellDataCount = GridSubCellDataSize * GridSubCellDataSize * GridSubCellDataSize;

        int volumeBufferSize = GridVertexCount * (ChunkHeight + 1) * GridSubCellDataCount;

        SurfaceVolume = new float[volumeBufferSize];
        System.Array.Fill(SurfaceVolume, DEFAULT_DENSITY_VALUE);

        BuildVolume();
        UpdateMesh();
    }

    //private float3 GetCellPoint(int f, int y, float xd, float yd, float zd)
    //{
    //    //Grid.GetQuadVertices(f, out float3 a, out float3 b, out float3 c, out float3 d);
    //    //float3 q = math.lerp(b, a, xd);
    //    //float3 r = math.lerp(c, d, xd);
    //    //float3 v = math.lerp(r, q, zd);
    //    float3 v = Grid.GetInterpolatedPosition(f, xd, zd);
    //    return v + new float3(0, y + yd, 0);
    //}

    //private float3 GetCellPoint(int f, int y, int xs, int ys, int zs)
    //{
    //    float xd = xs / (float)GridSubCellSize;
    //    float yd = ys / (float)GridSubCellSize;
    //    float zd = zs / (float)GridSubCellSize;
    //    return GetCellPoint(f, y, xd, yd, zd);
    //}

    //private int GetVolumeIndex(int f, int y, int xs, int ys, int zs)
    //{
    //    int a = Halfedges.NextHalfedge(f);
    //    int b = Halfedges.NextHalfedge(a);
    //    int p = Grid.GetEdge(b);
    //    int s = Utils.I3(xs, ys, zs, GridSubCellDataSize, GridSubCellDataSize);
    //    return (p + y * GridVertexCount) * GridSubCellDataCount + s;
    //}

    //private void SetVolume(int f, int y, int xs, int ys, int zs, float d)
    //{
    //    int index = GetVolumeIndex(f, y, xs, ys, zs);
    //    SurfaceVolume[index] = d;
    //}

    //private float GetVolume(int f, int y, int xs, int ys, int zs)
    //{
    //    int index = GetVolumeIndex(f, y, xs, ys, zs);
    //    return SurfaceVolume[index];
    //}

    //private float SurfaceSDF(int f, int y, int xs, int ys, int zs)
    //{
    //    var p = GetCellPoint(f, y, xs, ys, zs);
    //    float3 p0 = new float3(p.x, 0, p.z);
    //    float d = Noise.FBM_4(p0 * 0.04f) * 10f + 5f;
    //    return p.y - d;
    //}

    //private int GetVolumeIndex(int p, int y)
    //{
    //    return p + y * GridVertexCount;
    //}

    private int GetVolumeIndex(int p, int y)
    {
        return (p + y * GridVertexCount) * GridSubCellDataCount;
    }

    private void BuildVolume()
    {
        for (int p = 0; p < GridVertexCount; p++)
        {
            float3 v = Grid.GetVertex(p);
            float d = Noise.FBM_4(v * 0.04f) * 10f + 5f;
            for (int y = 0; y < ChunkHeight + 1; y++)
            {
                int index = GetVolumeIndex(p, y);
                SurfaceVolume[index] = y - d;
            }
        }

        //foreach (int f in Grid.GetFaceIndices())
        //{
        //    for (int y = 0; y < ChunkHeight + 1; y++)
        //        for (int zs = 0; zs < GridSubCellDataSize; zs++)
        //            for (int ys = 0; ys < GridSubCellDataSize; ys++)
        //                for (int xs = 0; xs < GridSubCellDataSize; xs++)
        //                {
        //                    float d = SurfaceSDF(f, y, xs, ys, zs);
        //                    SetVolume(f, y, xs, ys, zs, d);
        //                }
        //}

        //foreach (int f in Grid.GetFaceIndices())
        //{
        //    for (int y = 0; y < ChunkHeight + 1; y++)
        //    {
        //        float d = SurfaceSDF(f, y, 0, 0, 0);
        //        SetVolume(f, y, 0, 0, 0, d);
        //    }
        //}
    }

    public unsafe void UpdateMesh()
    {
        var counts = stackalloc int[3];
        var edgeIndices = new Dictionary<ulong, int>();

        //foreach (int f in Grid.GetFaceIndices())
        //{
        //    for (int y = 0; y < ChunkHeight; y++)
        //        for (int zs = 0; zs < GridSubCellSize; zs++)
        //            for (int ys = 0; ys < GridSubCellSize; ys++)
        //                for (int xs = 0; xs < GridSubCellSize; xs++)
        //                {
        //                    BuildCell(f, y, xs, ys, zs, counts, TempIndices, TempVertices, edgeIndices);
        //                }
        //}

        foreach (int f in Grid.GetFaceIndices())
        {
            for (int y = 0; y < ChunkHeight; y++)
            {
                BuildCell(f, y, 0, 0, 0, counts, TempIndices, TempVertices, edgeIndices);
            }
        }

        if (counts[2] > 0)
        {
            var dataArray = Mesh.AllocateWritableMeshData(1);
            var data = dataArray[0];

            data.SetVertexBufferParams(counts[2], VERTEX_ATTRIBUTES);
            data.SetIndexBufferParams(counts[1], INDEX_FORMAT);

            var vertices = data.GetVertexData<Vector3>();
            var indices = data.GetIndexData<int>();
            var verticesPtr = (Vector3*)NativeArrayUnsafeUtility.GetUnsafePtr(vertices);
            var indicesPtr = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(indices);

            for (int i = 0; i < counts[2]; i++)
            {
                verticesPtr[i] = new Vector3(
                    TempVertices[i * VERTEX_SIZE + 0],
                    TempVertices[i * VERTEX_SIZE + 1],
                    TempVertices[i * VERTEX_SIZE + 2]);
            }

            for (int i = 0; i < counts[1]; i++)
            {
                indicesPtr[i] = TempIndices[i];
            }

            SurfaceMesh.Clear();

            data.subMeshCount = 1;
            data.SetSubMesh(0, new SubMeshDescriptor(0, counts[1]));

            // apply mesh data
            var flags =
                  MeshUpdateFlags.DontRecalculateBounds
                | MeshUpdateFlags.DontValidateIndices
                | MeshUpdateFlags.DontNotifyMeshUsers
                | MeshUpdateFlags.DontResetBoneBounds;
            Mesh.ApplyAndDisposeWritableMeshData(dataArray, SurfaceMesh, flags);

            SurfaceMesh.RecalculateNormals();
            SurfaceMesh.RecalculateBounds();
            SurfaceMeshCollider.sharedMesh = SurfaceMesh;
        }
    }

    private unsafe float* GridCellLerp(float* vp, int f, int y)
    {
        float3 v = Grid.GetInterpolatedPosition(f, vp[0], vp[2]);
        vp[0] = v.x; vp[1] = vp[1] + y; vp[2] = v.z;
        return vp;
    }

    private unsafe void BuildCell(int f, int y, int xs, int ys, int zs, int* counts, int* indexBuffer, float* vertexBuffer, Dictionary<ulong, int> edgeIndices)
    {
        int i, si;
        float s;

        var e = stackalloc int[2];
        //var v = stackalloc int[3];
        var p = stackalloc float[3];
        var vp = stackalloc float[3];
        var vn = stackalloc float[3];
        var grid = stackalloc float[8];
        var vi = stackalloc int[8];
        var edges = stackalloc int[12];
        //ulong edgeHash0, edgeHash1;

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
            //v[0] = (int)MarchingCubes.cubeVerts[i, 0] + xs;
            //v[1] = (int)MarchingCubes.cubeVerts[i, 1] + ys;
            //v[2] = (int)MarchingCubes.cubeVerts[i, 2] + zs;
            //vi[i] = GetVolumeIndex(f, y, v[0], v[1], v[2]);

            vi[i] = volumeIndices[i];
            s = SurfaceVolume[vi[i]];
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

            e[0] = MarchingCubes.edgeIndex[i, 0];
            e[1] = MarchingCubes.edgeIndex[i, 1];

            //int p0 = vi[e[0]];
            //int p1 = vi[e[1]];

            //edgeHash0 = Halfedges.GetEdgeHash(p0, p1);
            //edgeHash1 = Halfedges.GetEdgeHash(p1, p0);

            //if (
            //    edgeIndices.TryGetValue(edgeHash1, out edges[i]) ||
            //    edgeIndices.TryGetValue(edgeHash0, out edges[i]))
            //{
            //    continue;
            //}

            //edgeIndices[edgeHash0] = counts[2];

            edges[i] = counts[2];

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

            vp = GridCellLerp(vp, f, y);

            vertexBuffer[counts[0]++] = vp[0];
            vertexBuffer[counts[0]++] = vp[1] * GridCellHeight;
            vertexBuffer[counts[0]++] = vp[2];

            counts[2]++;
        }

        for (i = 0; MarchingCubes.triTable[cubeIndex, i] != -1; i += 3)
        {
            si = counts[1];
            indexBuffer[si + 0] = edges[MarchingCubes.triTable[cubeIndex, i]];
            indexBuffer[si + 1] = edges[MarchingCubes.triTable[cubeIndex, i + 1]];
            indexBuffer[si + 2] = edges[MarchingCubes.triTable[cubeIndex, i + 2]];
            counts[1] += 3;
        }
    }
}
