using Priority_Queue;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshCollider))]
[RequireComponent(typeof(MeshRenderer))]
public unsafe class ChunkSurface : MonoBehaviour
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

    private MeshFilter SurfaceMeshFilter;
    private MeshRenderer SurfaceMeshRenderer;
    private Mesh SurfaceMesh;
    private MeshCollider SurfaceMeshCollider;
    private IrregularGrid Grid;
    private int GridVertexCount;
    private float GridCellHeight;
    private float[] SurfaceVolume;
    private bool[] SurfaceShadeFlat;
    private int[] SurfaceIndexToGridFace;
    private int[] SurfaceIndexToCellY;
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
        SurfaceIndexToGridFace = new int[INDEX_BUFFER_SIZE];
        SurfaceIndexToCellY = new int[INDEX_BUFFER_SIZE];

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

        int volumeBufferSize = GridVertexCount * (ChunkHeight + 1);
        SurfaceVolume = new float[volumeBufferSize];
        SurfaceShadeFlat = new bool[volumeBufferSize];
        System.Array.Fill(SurfaceVolume, DEFAULT_DENSITY_VALUE);

        BuildVolume();
        UpdateMesh();
    }

    public void FlattenAlongPath(CellNode start, CellNode goal)
    {
        foreach (var n in GetPath(start, goal))
        {
            Flatten(n.f, n.y, 1f);
        }
    }

    public void Flatten(Ray ray, float height, float amount = 1f)
    {
        int f = RaycastCell(ray);
        Flatten(f, height, amount);
    }

    public void Flatten(int f, float height, float amount = 1f)
    {
        amount = Mathf.Max(Mathf.Min(1f, amount), 0f);

        if (f >= 0)
        {
            int y;
            for (y = 0; y < ChunkHeight; y++)
                UpdateCellVolume(f, y, VolumeOperation.FLATTEN, height, amount);
        }
    }

    public class CellNode : FastPriorityQueueNode
    {
        public int f;
        public int y;
        public CellNode(int f, int y)
        {
            this.f = f;
            this.y = y;
        }

        public static bool operator ==(CellNode lhs, CellNode rhs)
        {
            if (ReferenceEquals(lhs, rhs))
                return true;
            if (ReferenceEquals(lhs, null))
                return false;
            if (ReferenceEquals(rhs, null))
                return false;
            return lhs.Equals(rhs);
        }

        public static bool operator !=(CellNode lhs, CellNode rhs) => !(lhs == rhs);

        public bool Equals(CellNode obj)
        {
            if (ReferenceEquals(obj, null))
                return false;
            if (ReferenceEquals(this, obj))
                return true;
            if (!GetType().Equals(obj.GetType()))
                return false;
            return f == obj.f && y == obj.y;
        }

        public override bool Equals(object obj) => Equals(obj as CellNode);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashcode = 1430287;
                hashcode = hashcode * 7302013 ^ f;
                hashcode = hashcode * 7302013 ^ y;
                return hashcode;
            }
        }
    }

    private float3 GetCellPosition(CellNode n)
    {
        int a = Halfedges.NextHalfedge(n.f);
        return GetCellPoint(Grid.GetEdge(a), n.y);
    }

    private bool IsSurfaceCell(int f, int y)
    {
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

        int i;
        int cubeIndex = 0;
        float s;

        for (i = 0; i < 8; ++i)
        {
            s = SurfaceVolume[volumeIndices[i]];
            cubeIndex |= (s > 0) ? (1 << i) : 0;
        }

        int edgeMask = MarchingCubes.edgeTable[cubeIndex];
        return !(edgeMask == 0 || edgeMask == 0xFF);
    }

    private List<CellNode> GetNeighbours(CellNode node)
    {
        var neighbours = new List<CellNode>();

        foreach (var n in Grid.GetDirectNeighbours(node.f))
        {
            if (node.y + 1 < ChunkHeight)
            {
                if (IsSurfaceCell(n, node.y + 1))
                {
                    neighbours.Add(new CellNode(n, node.y + 1));
                }
            }

            if (node.y - 1 >= 0)
            {
                if (IsSurfaceCell(n, node.y - 1))
                {
                    neighbours.Add(new CellNode(n, node.y - 1));
                }
            }

            if (IsSurfaceCell(n, node.y))
            {
                neighbours.Add(new CellNode(n, node.y));
            }
        }

        return neighbours;
    }

    private float GetCost(CellNode na, CellNode nb, Vector3 a, Vector3 b)
    {
        return Heuristics(a, b) + Mathf.Abs(a.y - b.y);
    }

    private float Heuristics(Vector3 a, Vector3 b)
    {
        return Utils.ManhattanDistance(a, b);
    }

    public CellNode GetCellNodeAt(Vector3 p)
    {
        var ray = new Ray(p, Vector3.down);
        int f = RaycastCell(ray);
        if (f >= 0)
        {
            for (int y = 0; y < ChunkHeight; y++)
            {
                if (IsSurfaceCell(f, y))
                {
                    return new CellNode(f, y);
                }
            }
        }
        return null;
    }

    public List<CellNode> GetPath(CellNode start, CellNode goal)
    {
        var path = new List<CellNode>();
        if (start == goal) return path;

        var frontier = new FastPriorityQueue<CellNode>(10000);
        var cameFrom = new Dictionary<CellNode, CellNode>();
        var costSoFar = new Dictionary<CellNode, float>();

        float newCost, currentCost, priority, costNext;
        bool pathFound = false;
        CellNode current;
        float3 currentPoint, nextPoint;
        float3 goalPoint = GetCellPosition(goal);

        frontier.Enqueue(start, 0);
        cameFrom[start] = null;
        costSoFar[start] = 0;

        while (frontier.Count > 0)
        {
            current = frontier.Dequeue();
            currentCost = costSoFar[current];
            currentPoint = GetCellPosition(current);

            if (current == goal)
            {
                pathFound = true;
                break;
            }

            foreach (var next in GetNeighbours(current))
            {
                nextPoint = GetCellPosition(next);
                newCost = currentCost + GetCost(current, next, currentPoint, nextPoint);

                if (!costSoFar.TryGetValue(next, out costNext) || newCost < costNext)
                {
                    costSoFar[next] = newCost;
                    priority = newCost + Heuristics(goalPoint, nextPoint);
                    frontier.Enqueue(next, priority);
                    cameFrom[next] = current;
                }
            }
        }

        if (pathFound)
        {
            var ct = goal;
            var nt = cameFrom[ct];
            path.Add(goal);

            while (ct != start)
            {
                ct = nt;
                path.Add(ct);
                if (ct == start) break;
                nt = cameFrom[nt];
            }

            path.Reverse();
        }

        return path;
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
                        //SurfaceShadeFlat[volumeIndices[i]] = true;
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

    public void GetCellCoordinates(int t, out int f, out int y)
    {
        f = SurfaceIndexToGridFace[t * 3];
        y = SurfaceIndexToCellY[t * 3];
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

        //for (int p = 0; p < GridVertexCount; p++)
        //    for (int y = 0; y < ChunkHeight + 1; y++)
        //    {
        //        int index = GetVolumeIndex(p, y);
        //        float3 v = Grid.GetVertex(p) + new float3(0, y, 0);
        //        SurfaceVolume[index] = Noise.FBM_4(v * 0.05f) * 100f;
        //    }
    }

    public unsafe void UpdateMesh()
    {
        var counts = stackalloc int[3];
        var edgeIndices = new Dictionary<ulong, int>();

        foreach (int f in Grid.GetFaceIndices())
        {
            for (int y = 0; y < ChunkHeight; y++)
            {
                BuildCell(f, y, counts, TempIndices, TempVertices, edgeIndices);
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

    private unsafe void BuildCell(int f, int y, int* counts, int* indexBuffer, float* vertexBuffer, Dictionary<ulong, int> edgeIndices)
    {
        int i, si;
        float s;

        var e = stackalloc int[2];
        var v = stackalloc int[3];
        var p = stackalloc float[3];
        var vp = stackalloc float[3];
        var vn = stackalloc float[3];
        var grid = stackalloc float[8];
        var vi = stackalloc int[8];
        var sf = stackalloc bool[8];
        var edges = stackalloc int[12];
        ulong edgeHash0, edgeHash1;

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
            vi[i] = volumeIndices[i];
            sf[i] = SurfaceShadeFlat[volumeIndices[i]];
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

            e[0] = MarchingCubes.edgeIndex[i, 0];
            e[1] = MarchingCubes.edgeIndex[i, 1];

            int p0 = vi[e[0]];
            int p1 = vi[e[1]];

            bool sf0 = sf[e[0]];
            bool sf1 = sf[e[1]];

            edgeHash0 = Halfedges.GetEdgeHash(p0, p1);
            edgeHash1 = Halfedges.GetEdgeHash(p1, p0);

            if (!sf0 && !sf1 && (
                edgeIndices.TryGetValue(edgeHash1, out edges[i]) ||
                edgeIndices.TryGetValue(edgeHash0, out edges[i])))
            {
                continue;
            }

            edgeIndices[edgeHash0] = counts[2];

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

            vp = GridCellLerp(vp, y, baseEdges);

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
            SurfaceIndexToGridFace[si + 0] = f;
            SurfaceIndexToGridFace[si + 1] = f;
            SurfaceIndexToGridFace[si + 2] = f;
            SurfaceIndexToCellY[si + 0] = y;
            SurfaceIndexToCellY[si + 1] = y;
            SurfaceIndexToCellY[si + 2] = y;
            counts[1] += 3;
        }
    }

    public void DrawCellNode(CellNode node)
    {
        int i;
        int f = node.f;
        int y = node.y;

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

        for (i = 0; i < 12; ++i)
        {
            int e0 = MarchingCubes.edgeIndex[i, 0];
            int e1 = MarchingCubes.edgeIndex[i, 1];

            float3 p0 = points[e0];
            float3 p1 = points[e1];

            p0.y *= GridCellHeight;
            p1.y *= GridCellHeight;

            Gizmos.DrawLine(p0, p1);
        }
    }
}
