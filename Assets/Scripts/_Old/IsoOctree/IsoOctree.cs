using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

using static Iso;
using static IsoMeshStructs;

public class IsoOctree
{
    private IsoNode[] Nodes;
    private int NodeCount;
    private VolumeData[] SignedDistanceField;
    private float[] SurfaceHeightField;
    private int BufferSize;
    private int DataSize;

    private int3 RootMin;
    private int RootSize;
    private int CellSize;

    public IsoOctree(int3 rootMin, int rootSize, int cellSize)
    {
        RootMin = rootMin;
        RootSize = rootSize;
        CellSize = cellSize;

        DataSize = (RootSize / CellSize) + 3;
        BufferSize = DataSize * DataSize * DataSize;

        Nodes = new IsoNode[BufferSize];
        NodeCount = 0;
        SignedDistanceField = new VolumeData[BufferSize];
        SurfaceHeightField = new float[DataSize * DataSize];
    }

    public void BuildVolume()
    {
        int x, y, z;

        int3 maxIt = RootSize / CellSize;

        for (x = -1; x < maxIt.x + 2; x++)
            for (z = -1; z < maxIt.z + 2; z++)
            {
                var idxPos = new int3(x, 0, z);
                float3 pos = idxPos * CellSize + RootMin;
                SurfaceHeightField[Utils.I2(x + 1, z + 1, DataSize)] = SurfaceHeight(pos);
            }

        for (x = 0; x < maxIt.x + 1; x++)
            for (y = 0; y < maxIt.y + 1; y++)
                for (z = 0; z < maxIt.z + 1; z++)
                {
                    var idxPos = new int3(x, y, z);
                    float d = SurfaceDistance(idxPos);
                    float3 n = SurfaceNormal(idxPos);
                    SetVolumeData(idxPos, d, n);
                }
    }

    private void SetVolumeData(int3 p, float density, float3 normal)
    {
        int index = Utils.I3(p.x + 1, p.y + 1, p.z + 1, DataSize, DataSize);
        if (index < 0 || index >= BufferSize) return;
        SignedDistanceField[index] = new VolumeData(density, normal);
    }
    
    private VolumeData GetVolumeData(int3 p)
    {
        int index = Utils.I3(p.x + 1, p.y + 1, p.z + 1, DataSize, DataSize);
        if (index < 0 || index >= BufferSize) return default;
        return SignedDistanceField[index];
    }

    private static float SurfaceHeight(float3 p)
    {
        p.y = 0;
        return Noise.FBM_4(p * 0.04f) * 10f + 5f;
    }

    private float SurfaceDistance(int3 p)
    {
        int index = Utils.I2(p.x + 1, p.z + 1, DataSize);
        if (index < 0 || index >= DataSize * DataSize) return 0f;
        float d = SurfaceHeightField[index];
        return CellSize * p.y - d;
    }

    private float3 SurfaceNormal(int3 p)
    {
        int2 h = new int2(1, 0);
        return math.normalize(new float3(
            SurfaceDistance(p + h.xyy) - SurfaceDistance(p - h.xyy),
            SurfaceDistance(p + h.yxy) - SurfaceDistance(p - h.yxy),
            SurfaceDistance(p + h.yyx) - SurfaceDistance(p - h.yyx)));
    }

    public int AddNode(IsoNode node)
    {
        int index = NodeCount++;
        Nodes[index] = node;
        node.Index = index;
        return index;
    }

    public IsoNode GetChild(IsoNode node, int childIndex) 
    {
        if (node.Children[childIndex] < 0) return null;
        return Nodes[node.Children[childIndex]];
    }

    public void ContourProcessEdge(IsoNode[] node, int dir, ref Counts counts, NativeArray<int> indexBuffer)
    {
        int minSize = 1000000; // arbitrary big number
        int minIndex = 0;
        int[] indices = { -1, -1, -1, -1 };
        bool flip = false;
        bool[] signChange = { false, false, false, false };

        for (int i = 0; i < 4; i++)
        {
            int edge = PROCESS_EDGE_MASK[dir][i];
            int c1 = EDGE_V_MAP[edge][0];
            int c2 = EDGE_V_MAP[edge][1];

            int mask = node[i].DrawInfo.Corners;
            int m1 = (mask >> c1) & 1;
            int m2 = (mask >> c2) & 1;

            if (node[i].Size < minSize)
            {
                minSize = node[i].Size;
                minIndex = i;
                flip = m1 != MATERIAL_AIR;
            }

            indices[i] = node[i].DrawInfo.Index;

            signChange[i] =
                (m1 == MATERIAL_AIR && m2 != MATERIAL_AIR) ||
                (m1 != MATERIAL_AIR && m2 == MATERIAL_AIR);
        }

        if (signChange[minIndex])
        {
            if (flip)
            {
                indexBuffer[counts.IndexCount++] = indices[0];
                indexBuffer[counts.IndexCount++] = indices[1];
                indexBuffer[counts.IndexCount++] = indices[3];

                indexBuffer[counts.IndexCount++] = indices[0];
                indexBuffer[counts.IndexCount++] = indices[3];
                indexBuffer[counts.IndexCount++] = indices[2];
            }
            else
            {
                indexBuffer[counts.IndexCount++] = indices[0];
                indexBuffer[counts.IndexCount++] = indices[3];
                indexBuffer[counts.IndexCount++] = indices[1];

                indexBuffer[counts.IndexCount++] = indices[0];
                indexBuffer[counts.IndexCount++] = indices[2];
                indexBuffer[counts.IndexCount++] = indices[3];
            }
        }
    }

    public void ContourEdgeProc(IsoNode[] node, int dir, ref Counts counts, NativeArray<int> indexBuffer)
    {
        if (node[0] == null || node[1] == null || node[2] == null || node[3] == null)
        {
            return;
        }

        if (node[0].NodeType != IsoNode.IsoNodeType.INTERNAL &&
            node[1].NodeType != IsoNode.IsoNodeType.INTERNAL &&
            node[2].NodeType != IsoNode.IsoNodeType.INTERNAL &&
            node[3].NodeType != IsoNode.IsoNodeType.INTERNAL)
        {
            ContourProcessEdge(node, dir, ref counts, indexBuffer);
        }
        else
        {
            for (int i = 0; i < 2; i++)
            {
                var edgeNodes = new IsoNode[4];
                int[] c =
                {
                    EDGE_PROC_EDGE_MASK[dir][i][0],
                    EDGE_PROC_EDGE_MASK[dir][i][1],
                    EDGE_PROC_EDGE_MASK[dir][i][2],
                    EDGE_PROC_EDGE_MASK[dir][i][3],
                };

                for (int j = 0; j < 4; j++)
                {
                    if (node[j].NodeType == IsoNode.IsoNodeType.LEAF)
                    {
                        edgeNodes[j] = node[j];
                    }
                    else
                    {
                        edgeNodes[j] = GetChild(node[j], c[j]);
                    }
                }

                ContourEdgeProc(edgeNodes, EDGE_PROC_EDGE_MASK[dir][i][4], ref counts, indexBuffer);
            }
        }
    }

    public void ContourFaceProc(IsoNode[] node, int dir, ref Counts counts, NativeArray<int> indexBuffer)
    {
        if (node[0] == null || node[1] == null)
        {
            return;
        }

        if (node[0].NodeType == IsoNode.IsoNodeType.INTERNAL ||
            node[1].NodeType == IsoNode.IsoNodeType.INTERNAL)
        {
            for (int i = 0; i < 4; i++)
            {
                var faceNodes = new IsoNode[2];
                int[] c =
                {
                    FACE_PROC_FACE_MASK[dir][i][0],
                    FACE_PROC_FACE_MASK[dir][i][1],
                };

                for (int j = 0; j < 2; j++)
                {
                    if (node[j].NodeType != IsoNode.IsoNodeType.INTERNAL)
                    {
                        faceNodes[j] = node[j];
                    }
                    else
                    {
                        faceNodes[j] = GetChild(node[j], c[j]);
                    }
                }

                ContourFaceProc(faceNodes, FACE_PROC_FACE_MASK[dir][i][2], ref counts, indexBuffer);
            }

            int[][] orders =
            {
                new int[] { 0, 0, 1, 1 },
                new int[] { 0, 1, 0, 1 },
		    };

            for (int i = 0; i < 4; i++)
            {
                var edgeNodes = new IsoNode[4];
                int[] c =
                {
                    FACE_PROC_EDGE_MASK[dir][i][1],
                    FACE_PROC_EDGE_MASK[dir][i][2],
                    FACE_PROC_EDGE_MASK[dir][i][3],
                    FACE_PROC_EDGE_MASK[dir][i][4],
                };

                int[] order = orders[FACE_PROC_EDGE_MASK[dir][i][0]];
                for (int j = 0; j < 4; j++)
                {
                    if (node[order[j]].NodeType == IsoNode.IsoNodeType.LEAF)
                    {
                        edgeNodes[j] = node[order[j]];
                    }
                    else
                    {
                        edgeNodes[j] = GetChild(node[order[j]], c[j]);
                    }
                }

                ContourEdgeProc(edgeNodes, FACE_PROC_EDGE_MASK[dir][i][5], ref counts, indexBuffer);
            }
        }
    }

    public void ContourCellProc(IsoNode node, ref Counts counts, NativeArray<int> indexBuffer)
    {
        if (node == null)
        {
            return;
        }

        if (node.NodeType == IsoNode.IsoNodeType.INTERNAL)
        {
            for (int i = 0; i < 8; i++)
            {
                ContourCellProc(GetChild(node, i), ref counts, indexBuffer);
            }

            for (int i = 0; i < 12; i++)
            {
                var faceNodes = new IsoNode[2];
                int[] c = { CELL_PROC_FACE_MASK[i][0], CELL_PROC_FACE_MASK[i][1] };

                faceNodes[0] = GetChild(node, c[0]);
                faceNodes[1] = GetChild(node, c[1]);

                ContourFaceProc(faceNodes, CELL_PROC_FACE_MASK[i][2], ref counts, indexBuffer);
            }

            for (int i = 0; i < 6; i++)
            {
                var edgeNodes = new IsoNode[4];
                int[] c =
                {
                    CELL_PROC_EDGE_MASK[i][0],
                    CELL_PROC_EDGE_MASK[i][1],
                    CELL_PROC_EDGE_MASK[i][2],
                    CELL_PROC_EDGE_MASK[i][3],
                };

                for (int j = 0; j < 4; j++)
                {
                    edgeNodes[j] = GetChild(node, c[j]);
                }

                ContourEdgeProc(edgeNodes, CELL_PROC_EDGE_MASK[i][4], ref counts, indexBuffer);
            }
        }
    }

    public void GenerateVertexIndices(IsoNode node, ref Counts counts, NativeArray<Vertex> vertexBuffer)
    {
        if (node == null)
        {
            return;
        }

        int i;

        if (node.NodeType != IsoNode.IsoNodeType.LEAF)
        {
            for (i = 0; i < 8; i++)
            {
                GenerateVertexIndices(GetChild(node, i), ref counts, vertexBuffer);
            }
        }

        if (node.NodeType != IsoNode.IsoNodeType.INTERNAL)
        {
            if (node.DrawInfo == null)
            {
                Debug.LogError("Error! Could not add vertex!");
                return;
            }

            node.DrawInfo.Index = counts.VertexCount;
            var v = new Vertex(node.DrawInfo.Position, node.DrawInfo.Normal);
            vertexBuffer[counts.VertexCount++] = v;
        }
    }

    public void GenerateMeshFromOctree(IsoNode node, ref Counts counts, NativeArray<int> indexBuffer, NativeArray<Vertex> vertexBuffer)
    {
        if (node == null || node.Size == 0)
        {
            return;
        }

        GenerateVertexIndices(node, ref counts, vertexBuffer);
        ContourCellProc(node, ref counts, indexBuffer);
    }

    // ----------------------------------------------------------------------------

    private static ulong HashOctreeMin(int3 min)
    {
        return System.Convert.ToUInt64(min.x) | (System.Convert.ToUInt64(min.y) << 20) | (System.Convert.ToUInt64(min.z) << 40);
    }

    public IEnumerable<IsoNode> ConstructParents(IEnumerable<IsoNode> nodes, int parentSize)
    {
        var parentsHashmap = new Dictionary<ulong, IsoNode>();

        foreach (var node in nodes)
        {
            // because the octree is regular we can calculate the parent min
            var localPos = node.Min - RootMin;
            var parentPos = node.Min - (localPos % parentSize);

            ulong parentIndex = HashOctreeMin(parentPos - RootMin);

            if (!parentsHashmap.TryGetValue(parentIndex, out IsoNode parentNode))
            {
                parentNode = new IsoNode();
                parentNode.Min = parentPos;
                parentNode.Size = parentSize;
                parentNode.NodeType = IsoNode.IsoNodeType.INTERNAL;
                AddNode(parentNode);

                parentsHashmap[parentIndex] = parentNode;
            }

            for (int i = 0; i < 8; i++)
            {
                var childPos = parentPos + ((parentSize / 2) * CHILD_MIN_OFFSETS[i]);
                if (childPos.Equals(node.Min))
                {
                    parentNode.Children[i] = node.Index;
                    node.Parent = parentNode.Index;
                    break;
                }
            }
        }

        return parentsHashmap.Values;
    }

    public IsoNode ConstructUpwards(IEnumerable<IsoNode> nodes)
    {
	    if (nodes.Count() == 0)
	    {
		    return null;
	    }

        nodes = nodes.OrderBy(a => a.Size);

        // the input nodes may be different sizes if a seam octree is being constructed
        // in that case we need to process the input nodes in stages along with the newly
        // constructed parent nodes until the all the nodes have the same size
        while (nodes.First().Size != nodes.Last().Size)
        {
            // find the end of this run
            int size = nodes.First().Size;
            var newNodes = nodes.TakeWhile(n => n.Size == size);
            int count = newNodes.Count();

            // construct the new parent nodes for this run
            newNodes = ConstructParents(newNodes, size * 2);

            // set up for the next iteration: the parents produced plus any remaining input nodes
            newNodes.Concat(nodes.Skip(count));
            (nodes, newNodes) = (newNodes, nodes);
        }

        int parentSize = nodes.First().Size * 2;
        while (parentSize <= RootSize)
        {
            nodes = ConstructParents(nodes, parentSize);
            parentSize *= 2;
        }
        IsoNode root = nodes.First();
        return root;
    }

    public IEnumerable<IsoNode> FindActiveVoxels() {
	    var leafs = new List<IsoNode>();

        int i, j, k, x, y, z;
        var grid = new VolumeData[8];
        int3 posMin = RootMin / CellSize;
        int3 maxIt = RootSize / CellSize;

	    for (x = 0; x < maxIt.x; x++)
		    for (y = 0; y < maxIt.y; y++)
			    for (z = 0; z < maxIt.z; z++)
			    {
				    var idxPos = new int3 (x, y, z);

                    int corners = 0;
				    for (i = 0; i < 8; i++) {
					    int3 cornerPos = idxPos + CHILD_MIN_OFFSETS[i];
                        var vd = GetVolumeData(cornerPos);
                        float d = vd.Density;
                        int material = d < 0f ? MATERIAL_SOLID : MATERIAL_AIR;
                        corners |= (material << i);
					    grid[i] = vd;
				    }

                    if (corners == 0 || corners == 0xff)
                    {
                        continue;
                    }

                    // otherwise the voxel contains the surface, so find the edge intersections
                    float3 normal = float3.zero;
                    float3 position = float3.zero;
                    float3 pos = idxPos + posMin;
                    int3 min = idxPos * CellSize + RootMin;
                    int edgeCount = 0;

                    for (i = 0; i < 12; i++)
                    {
                        int c1 = EDGE_V_MAP[i][0];
                        int c2 = EDGE_V_MAP[i][1];

                        int m1 = (corners >> c1) & 1;
                        int m2 = (corners >> c2) & 1;

                        if ((m1 == MATERIAL_AIR && m2 == MATERIAL_AIR) ||
                            (m1 == MATERIAL_SOLID && m2 == MATERIAL_SOLID))
                        {
                            // no zero crossing on this edge
                            continue;
                        }

                        var p0 = grid[c1];
                        var p1 = grid[c2];
                        float g0 = p0.Density;
                        float g1 = p1.Density;
                        float t = g0 - g1;

                        if (Mathf.Abs(t) > 1e-6)
                            t = g0 / t;
                        else continue;

                        for (k = 1, j = 0; j < 3; ++j, k <<= 1)
                        {
                            int a = c1 & k;
                            int b = c2 & k;
                            if (a != b)
                                position[j] += a > 0 ? 1f - t : t;
                            else
                                position[j] += a > 0 ? 1f : 0;
                        }

                        edgeCount++;

                        var n = math.normalize(math.lerp(p0.Normal, p1.Normal, t));
                        normal += n;
                    }

                    if (edgeCount == 0) continue;

                    float s = 1f / edgeCount;
                    position = (pos + s * position) * CellSize;
                    normal = math.normalize(s * normal);

                    var drawInfo = new IsoDrawInfo();
                    drawInfo.Position = position;
                    drawInfo.Normal = normal;
                    drawInfo.Corners = corners;

                    var node = new IsoNode();
                    node.Min = min;
                    node.Size = CellSize;
                    node.NodeType = IsoNode.IsoNodeType.LEAF;
                    node.DrawInfo = drawInfo;
                    AddNode(node);

                    leafs.Add(node);
                }
        return leafs;
    }

    public int SimplifyOctree(IsoNode node, float threshold)
    {
        if (node == null)
        {
            return -1;
        }

        if (node.NodeType != IsoNode.IsoNodeType.INTERNAL)
        {
            return node.Index;
        }

        int[] signs = { -1, -1, -1, -1, -1, -1, -1, -1 };
        int midsign = -1;
        bool isCollapsible = true;
        int edgeCount = 0;
        var position = float3.zero;

        var ata = new Qef.Mat3(0f);
        var atb = new Qef.Vec3(0f);
        var pointaccum = new Qef.Vec4(0f);

        for (int i = 0; i < 8; i++)
        {
            node.Children[i] = SimplifyOctree(GetChild(node, i), threshold);

            if (node.Children[i] >= 0)
            {
                var child = GetChild(node, i);

                if (child.NodeType == IsoNode.IsoNodeType.INTERNAL)
                {
                    isCollapsible = false;
                }
                else
                {
                    var p = child.DrawInfo.Position;
                    var n = child.DrawInfo.Normal;

                    var pQ = new Qef.Vec3(p.x, p.y, p.z);
                    var nQ = new Qef.Vec3(n.x, n.y, n.z);
                    Qef.Qef.Add(nQ, pQ, ref ata, ref atb, ref pointaccum);

                    position += p;

                    midsign = (child.DrawInfo.Corners >> (7 - i)) & 1;
                    signs[i] = (child.DrawInfo.Corners >> i) & 1;

                    edgeCount++;
                }
            }
        }

        if (!isCollapsible)
        {
            // at least one child is an internal node, can't collapse
            return node.Index;
        }

        float s = 1f / edgeCount;
        position *= s;

        float error = Qef.Qef.Solve(ata, atb, pointaccum, out _);

        if (error > threshold)
        {
            // this collapse breaches the threshold
            return node.Index;
        }

        var drawInfo = new IsoDrawInfo();

        for (int i = 0; i < 8; i++)
        {
            if (signs[i] == -1)
            {
                // Undetermined, use centre sign instead
                drawInfo.Corners |= (midsign << i);
            }
            else
            {
                drawInfo.Corners |= (signs[i] << i);
            }
        }

        drawInfo.Normal = float3.zero;

        for (int i = 0; i < 8; i++)
        {
            if (node.Children[i] >= 0)
            {
                var child = GetChild(node, i);
                if (child.NodeType == IsoNode.IsoNodeType.LEAF)
                {
                    drawInfo.Normal += child.DrawInfo.Normal;
                }
            }
        }

        drawInfo.Normal = math.normalize(drawInfo.Normal);
        drawInfo.Position = position;

        for (int i = 0; i < 8; i++)
        {
            DestroyOctree(GetChild(node, i));
            node.Children[i] = -1;
        }

        node.NodeType = IsoNode.IsoNodeType.LEAF;
        node.DrawInfo = drawInfo;

        return node.Index;
    }

    public void DestroyOctree(IsoNode node)
    {
        if (node == null) return;

        for (int i = 0; i < 8; i++)
        {
            DestroyOctree(GetChild(node, i));
        }

        if (node.DrawInfo != null)
        {
            node.DrawInfo = null;
        }

        Nodes[node.Index] = null;
    }

}
