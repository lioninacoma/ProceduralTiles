using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Mathematics;

// Sourced from https://github.com/sketchpunklabs/irregular_grid/
// Based on irruglar grid https://sketchpunklabs.github.io/irregular_grid/
// by Oskar Stålberg 

public class IrregularGrid
{
    public class QuadEdge
    {
        public int Point { get; set; }
        public int Edge { get; set; }
        public int Halfedge { get; set; }
    }

    private Halfedges Halfedges;
    private int HalfedgesCount;
    private List<float3> Vertices;
    private List<int> FaceEdges;
    private int[] EdgeFaceLookup;

    public IrregularGrid(int maxEdgeCount)
    {
        Halfedges = new Halfedges(maxEdgeCount);
        EdgeFaceLookup = new int[maxEdgeCount];
        Vertices = new List<float3>();
        FaceEdges = new List<int>();
        HalfedgesCount = 0;

        for (int i = 0; i < maxEdgeCount; i++)
            EdgeFaceLookup[i] = -1;
    }
    
    public void Build(float radius = 3, int div = 3, int iter = 50, float relaxScl = .1f, int relaxType = 0, int seed = 123)
    {
        var triangles = new List<int>();
        BuildPoints(radius, div, ref Vertices);
        BuildTriangles(div, ref triangles);
        Halfedges.Update(triangles, ref HalfedgesCount);

        triangles.Clear();
        FaceSubdivide(ref triangles, seed);

        Halfedges.ClearEdges(0, HalfedgesCount);
        HalfedgesCount = 0;
        Halfedges.Update(triangles, ref HalfedgesCount);

        switch (relaxType)
        {
            case 0:
                RelaxForces(iter, relaxScl);
                break;
            case 1:
            default:
                RelaxWeighted(iter);
                break;
        }
    }

    public float3 GetInterpolatedPosition(int f, float xd, float yd)
    {
        GetQuadVertices(f, out float3 a, out float3 b, out float3 c, out float3 d);
        float3 q = math.lerp(c, d, xd);
        float3 r = math.lerp(b, a, xd);
        return math.lerp(r, q, yd);
    }

    public float3 GetVertex(int index)
    {
        return Vertices[index];
    }

    public List<int> GetFaceIndices()
    {
        return FaceEdges;
    }

    public int GetFaceCount()
    {
        return FaceEdges.Count;
    }

    public int GetEdgeCount()
    {
        return HalfedgesCount;
    }

    public int GetVertexCount()
    {
        return Vertices.Count;
    }

    public int GetEdge(int e)
    {
        return Halfedges.GetEdge(e);
    }

    public int GetHalfedge(int e)
    {
        return Halfedges.GetHalfedge(e);
    }

    public int GetFaceIndexOfEdge(int edge)
    {
        return EdgeFaceLookup[edge];
    }

    public int GetNeighbourEdges(int edge, ref int[] neighbourEdges)
    {
        return Halfedges.GetEdgesAroundPoint(edge, ref neighbourEdges, neighbourEdges.Length);
    }

    public List<int> GetDirectNeighbours(int f)
    {
        int i, n;
        QuadEdge edge;

        var neighbourFaces = new List<int>();
        var foundNeighbours = new HashSet<int>();
        var edges = new QuadEdge[4] { new(), new(), new(), new() };

        GetEdgesOfFaceIndex(f, edges);

        for (i = 0; i < 4; i++)
        {
            edge = edges[i];

            if (edge.Halfedge >= 0)
            {
                n = GetFaceIndexOfEdge(edge.Halfedge);

                if (n >= 0 && n != f && !foundNeighbours.Contains(n))
                {
                    foundNeighbours.Add(n);
                    neighbourFaces.Add(n);
                }
            }
        }

        return neighbourFaces;
    }

    public List<int> GetNeighbourFaces(int f)
    {
        int i, e, h, n, neighbourCount;
        QuadEdge edge;

        var neighbourFaces = new List<int>();
        var foundNeighbours = new HashSet<int>();
        var neighbourEdges = new int[16];
        var edges = new QuadEdge[4] { new(), new(), new(), new() };

        GetEdgesOfFaceIndex(f, edges);

        for (i = 0; i < 4; i++)
        {
            edge = edges[i];

            if (edge.Edge >= 0)
            {
                neighbourCount = GetNeighbourEdges(Halfedges.PrevHalfedge(edge.Edge), ref neighbourEdges);

                for (e = 0; e < neighbourCount; e++)
                {
                    h = neighbourEdges[e];
                    n = GetFaceIndexOfEdge(h);

                    if (n >= 0 && n != f && !foundNeighbours.Contains(n))
                    {
                        foundNeighbours.Add(n);
                        neighbourFaces.Add(n);
                    }
                }
            }
        }

        return neighbourFaces;
    }

    public void GetQuadVertices(int f, out float3 a, out float3 b, out float3 c, out float3 d)
    {
        int ae = Halfedges.NextHalfedge(f);
        int be = Halfedges.NextHalfedge(ae);
        int ce = Halfedges.NextHalfedge(Halfedges.GetHalfedge(f));
        int de = Halfedges.NextHalfedge(ce);

        int ai = Halfedges.GetEdge(ae);
        int bi = Halfedges.GetEdge(be);
        int ci = Halfedges.GetEdge(ce);
        int di = Halfedges.GetEdge(de);

        a = Vertices[ai];
        b = Vertices[bi];
        c = Vertices[ci];
        d = Vertices[di];
    }

    public void GetEdgesOfFaceIndex(int f, QuadEdge[] edges)
    {
        int ae = Halfedges.NextHalfedge(f);
        int be = Halfedges.NextHalfedge(ae);
        int ce = Halfedges.NextHalfedge(Halfedges.GetHalfedge(f));
        int de = Halfedges.NextHalfedge(ce);

        int ah = Halfedges.GetHalfedge(ae);
        int bh = Halfedges.GetHalfedge(be);
        int ch = Halfedges.GetHalfedge(ce);
        int dh = Halfedges.GetHalfedge(de);

        int ai = Halfedges.GetEdge(ae);
        int bi = Halfedges.GetEdge(be);
        int ci = Halfedges.GetEdge(ce);
        int di = Halfedges.GetEdge(de);

        edges[0].Point = ai; edges[0].Edge = ae; edges[0].Halfedge = ah;
        edges[1].Point = bi; edges[1].Edge = be; edges[1].Halfedge = bh;
        edges[2].Point = ci; edges[2].Edge = ce; edges[2].Halfedge = ch;
        edges[3].Point = di; edges[3].Edge = de; edges[3].Halfedge = dh;
    }

    private void GetTriangleVertices(int t, out float3 a, out float3 b, out float3 c)
    {
        int ai = Halfedges.GetEdgeOfTriangle(t, 0);
        int bi = Halfedges.GetEdgeOfTriangle(t, 1);
        int ci = Halfedges.GetEdgeOfTriangle(t, 2);
        a = Vertices[ai];
        b = Vertices[bi];
        c = Vertices[ci];
    }

    private void FaceSubdivide(ref List<int> triangles, int seed = 123)
    {
        var rng = new System.Random(seed);

        int he, t1;
        bool quadFound;

        var midpoints = new int[HalfedgesCount];
        var midpointExists = new bool[HalfedgesCount];
        var triProcessed = new bool[HalfedgesCount / 3];
        var triangleIndices = Enumerable.Range(0, HalfedgesCount / 3);
        var edgeIndices = Enumerable.Range(0, 3);

        foreach (int t0 in triangleIndices.OrderBy(a => rng.Next()))
        {
            if (triProcessed[t0]) continue;

            quadFound = false;
            triProcessed[t0] = true;

            foreach (int i in edgeIndices.OrderBy(a => rng.Next()))
            {
                he = Halfedges.GetHalfedgeOfTriangle(t0, i);
                if (he >= 0)
                {
                    t1 = Halfedges.TriangleOfEdge(he);
                    if (!triProcessed[t1])
                    {
                        FaceSubdivideQuad(he, midpoints, midpointExists, ref triangles);
                        triProcessed[t1] = true;
                        quadFound = true;
                        break;
                    }
                }
            }

            if (quadFound) continue;
            FaceSubdivideTriangle(t0, midpoints, midpointExists, ref triangles);
        }
    }

    private void FaceSubdivideTriangle(int t, int[] midpoints, bool[] midpointExists, ref List<int> triangles)
    {
        float3 a, b, c, v;
        int ai, bi, ci, ae, be, ce, ah, bh, ch, ab, bc, ca, cp;

        ae = Halfedges.EdgeOfTriangle(t, 0);
        be = Halfedges.EdgeOfTriangle(t, 1);
        ce = Halfedges.EdgeOfTriangle(t, 2);

        ah = Halfedges.GetHalfedgeOfTriangle(t, 0);
        bh = Halfedges.GetHalfedgeOfTriangle(t, 1);
        ch = Halfedges.GetHalfedgeOfTriangle(t, 2);

        ai = Halfedges.GetEdge(ae);
        bi = Halfedges.GetEdge(be);
        ci = Halfedges.GetEdge(ce);

        a = Vertices[ai];
        b = Vertices[bi];
        c = Vertices[ci];

        if (ah >= 0 && midpointExists[ah])
        {
            ab = midpoints[ah];
        }
        else
        {
            v = math.lerp(a, b, 0.5f);
            ab = Vertices.Count;
            Vertices.Add(v);
            midpointExists[ae] = true;
            midpoints[ae] = ab;
        }

        if (bh >= 0 && midpointExists[bh])
        {
            bc = midpoints[bh];
        }
        else
        {
            v = math.lerp(b, c, 0.5f);
            bc = Vertices.Count;
            Vertices.Add(v);
            midpointExists[be] = true;
            midpoints[be] = bc;
        }

        if (ch >= 0 && midpointExists[ch])
        {
            ca = midpoints[ch];
        }
        else
        {
            v = math.lerp(c, a, 0.5f);
            ca = Vertices.Count;
            Vertices.Add(v);
            midpointExists[ce] = true;
            midpoints[ce] = ca;
        }

        v = (a + b + c) / 3f;
        cp = Vertices.Count;
        Vertices.Add(v);

        AddQuadFace(cp, ca, ai, ab, ref triangles);
        AddQuadFace(cp, ab, bi, bc, ref triangles);
        AddQuadFace(cp, bc, ci, ca, ref triangles);
    }

    private void FaceSubdivideQuad(int s, int[] midpoints, bool[] midpointExists, ref List<int> triangles)
    {
        float3 a, b, c, d, v;
        int ai, bi, ci, di, ae, be, ce, de, ah, bh, ch, dh, ab, bc, cd, da, cp;

        ae = Halfedges.NextHalfedge(s);
        be = Halfedges.NextHalfedge(ae);
        ce = Halfedges.NextHalfedge(Halfedges.GetHalfedge(s));
        de = Halfedges.NextHalfedge(ce);

        ah = Halfedges.GetHalfedge(ae);
        bh = Halfedges.GetHalfedge(be);
        ch = Halfedges.GetHalfedge(ce);
        dh = Halfedges.GetHalfedge(de);

        ai = Halfedges.GetEdge(ae);
        bi = Halfedges.GetEdge(be);
        ci = Halfedges.GetEdge(ce);
        di = Halfedges.GetEdge(de);

        a = Vertices[ai];
        b = Vertices[bi];
        c = Vertices[ci];
        d = Vertices[di];

        if (ah >= 0 && midpointExists[ah])
        {
            ab = midpoints[ah];
        }
        else
        {
            v = math.lerp(a, b, 0.5f);
            ab = Vertices.Count;
            Vertices.Add(v);
            midpointExists[ae] = true;
            midpoints[ae] = ab;
        }

        if (bh >= 0 && midpointExists[bh])
        {
            bc = midpoints[bh];
        }
        else
        {
            v = math.lerp(b, c, 0.5f);
            bc = Vertices.Count;
            Vertices.Add(v);
            midpointExists[be] = true;
            midpoints[be] = bc;
        }

        if (ch >= 0 && midpointExists[ch])
        {
            cd = midpoints[ch];
        }
        else
        {
            v = math.lerp(c, d, 0.5f);
            cd = Vertices.Count;
            Vertices.Add(v);
            midpointExists[ce] = true;
            midpoints[ce] = cd;
        }

        if (dh >= 0 && midpointExists[dh])
        {
            da = midpoints[dh];
        }
        else
        {
            v = math.lerp(d, a, 0.5f);
            da = Vertices.Count;
            Vertices.Add(v);
            midpointExists[de] = true;
            midpoints[de] = da;
        }

        v = (a + b + c + d) / 4f;
        cp = Vertices.Count;
        Vertices.Add(v);

        AddQuadFace(ai, ab, cp, da, ref triangles);
        AddQuadFace(ab, bi, bc, cp, ref triangles);
        AddQuadFace(bc, ci, cd, cp, ref triangles);
        AddQuadFace(cd, di, da, cp, ref triangles);
    }

    private void AddQuadFace(int a, int b, int c, int d, ref List<int> triangles)
    {
        int startEdge = triangles.Count;
        int faceEdge = startEdge + 2;

        triangles.Add(a); triangles.Add(b); triangles.Add(c);
        triangles.Add(c); triangles.Add(d); triangles.Add(a);

        FaceEdges.Add(faceEdge);
        for (int i = 0; i < 6; i++)
            EdgeFaceLookup[startEdge + i] = faceEdge;
    }

    private float3 RotateY90(float3 v)
    {
        float x = v[0], y = v[1], z = v[2];
        v[0] = -z;
        v[1] = y;
        v[2] = x;
        return v;
    }

    private void RelaxWeighted(int iter)
    {
        int i, j, t, e, he, p, neighbourCount;
        float weight, w;
        float3 v, n, centroid, pos;

        var neighbourEdges = new int[16];
        var isInnerVert = new bool[Vertices.Count];
        var reprEdge = new int[Vertices.Count];
        for (i = 0; i < isInnerVert.Length; i++)
            isInnerVert[i] = true;

        for (t = 0; t < HalfedgesCount / 3; t++)
        {
            for (i = 0; i < 3; i++)
            {
                e = Halfedges.EdgeOfTriangle(t, i);
                p = Halfedges.GetEdge(e);
                he = Halfedges.GetHalfedge(e);
                isInnerVert[p] &= he >= 0;
                reprEdge[p] = he;
            }
        }

        for (int loop = 0; loop < iter; loop++)
        {
            for (i = 0; i < Vertices.Count; i++)
            {
                if (!isInnerVert[i]) continue;

                e = reprEdge[i];
                neighbourCount = GetNeighbourEdges(e, ref neighbourEdges);
                if (neighbourCount == 0) continue;

                centroid = float3.zero;
                weight = 0;
                v = Vertices[i];

                for (j = 0; j < neighbourCount; j++)
                {
                    n = Vertices[Halfedges.GetEdge(neighbourEdges[j])];
                    w = math.distance(v, n);
                    weight += w;
                    pos = n * w;
                    centroid += pos;
                }

                centroid /= weight;
                Vertices[i] = centroid;
            }
        }
    }

    private void RelaxForces(int iter = 50, float relaxScl = 0.1f)
    {
        int i, t, e, he, p;
        QuadEdge edge;
        float3 force, centroid;

        var edges = new QuadEdge[4];
        for (i = 0; i < 4; i++) edges[i] = new QuadEdge();

        var forces = new float3[Vertices.Count];
        var isInnerVert = new bool[Vertices.Count];
        for (i = 0; i < isInnerVert.Length; i++)
            isInnerVert[i] = true;

        for (t = 0; t < HalfedgesCount / 3; t++)
        {
            for (i = 0; i < 3; i++)
            {
                e = Halfedges.EdgeOfTriangle(t, i);
                p = Halfedges.GetEdge(e);
                he = Halfedges.GetHalfedge(e);
                isInnerVert[p] &= he >= 0;
            }
        }

        for (int loop = 0; loop < iter; loop++)
        {
            force = float3.zero; 
            for (i = 0; i < forces.Length; i++)
                forces[i] = float3.zero;

            foreach (int f in FaceEdges)
            {
                GetEdgesOfFaceIndex(f, edges);
                centroid = float3.zero;

                for (i = 0; i < 4; i++)
                {
                    edge = edges[i];
                    centroid += Vertices[edge.Point];
                }

                centroid /= 4f;

                for (i = 0; i < 4; i++)
                {
                    edge = edges[i];
                    if (isInnerVert[edge.Point])
                    {
                        force += (Vertices[edge.Point] - centroid);
                        force = RotateY90(force);
                    }
                }

                force /= 4f;

                for (i = 0; i < 4; i++)
                {
                    edge = edges[i];
                    if (isInnerVert[edge.Point])
                    {
                        forces[edge.Point] += ((centroid + force) - Vertices[edge.Point]);
                        force = RotateY90(force);
                    }
                }
            }

            for (i = 0; i < forces.Length; i++)
            {
                if (isInnerVert[i])
                {
                    Vertices[i] += (forces[i] * relaxScl);
                }
            }
        }
    }

    private void BuildPoints(float radius, int div, ref List<float3> points)
    {
        float rad = -30f * Mathf.PI / 180f;
        var pCorner = new float3(radius * Mathf.Cos(rad), 0,
            radius * Mathf.Sin(rad));
        var pTop = new float3(0, 0, -radius);

        int min = div - 1;     // Min points to make per col
        int max = div * 2 - 1; // Max points to make per col
        int iAbs, pntCnt;
        int sgn;
        float3 aPnt, bPnt, xPnt;

        // Loop creates a pattern like -2,-1,0,1,2
        for (int i = -min; i <= min; i++)
        {
            iAbs = System.Math.Abs(i);
            pntCnt = max + -iAbs - 1;
            sgn = System.Math.Sign(i);
            aPnt = math.lerp(pCorner, pTop,  1f - (iAbs / (float)min));
            aPnt[0] *= (sgn == 0) ? 1 : sgn;
            bPnt = new float3(aPnt.x, aPnt.y, aPnt.z);
            bPnt[2] = -bPnt[2];

            points.Add(aPnt);
            for (int j = 1; j < pntCnt; j++)
            {
                xPnt = math.lerp(aPnt, bPnt, j / (float)pntCnt);
                points.Add(xPnt);
            }
            points.Add(bPnt );
        }
    }

    private void BuildTriangles(int div, ref List<int> triangles)
    {
        int min = div - 1;     // Min points to make per col
        int max = div * 2 - 1; // Max points to make per col

        int aAbs, bAbs;
        int aCnt, bCnt, minCnt;
        int aIdx = 0;
        int bIdx;
        int j;

        int a, b, c, d;

        // Loop creates a pattern like -2,-1,0,1,2
        for (int i = -min; i < min; i++)
        {
            aAbs = System.Math.Abs(i);
            bAbs = System.Math.Abs(i + 1);
            aCnt = max + -aAbs;       // How many point sin the column
            bCnt = max + -bAbs;
            bIdx = aIdx + aCnt;       // Starting index for second column
            minCnt = System.Math.Min(aCnt, bCnt) - 1;

            // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
            // Create each column as quads
            for (j = 0; j < minCnt; j++)
            {
                a = aIdx + j;
                b = a + 1;
                d = bIdx + j;
                c = d + 1;

                if (i < 0)
                {
                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(c);

                    triangles.Add(c);
                    triangles.Add(d);
                    triangles.Add(a);
                }
                else
                {
                    triangles.Add(a);
                    triangles.Add(b);
                    triangles.Add(d);

                    triangles.Add(b);
                    triangles.Add(c);
                    triangles.Add(d);
                }
            }

            // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
            // Every column has an ending triangle
            if (i < 0)
            {
                a = aIdx + aCnt - 1;
                b = a + bCnt;
                c = b - 1;
            }
            else
            {
                b = aIdx + aCnt - 1;
                a = b - 1;
                c = b + bCnt;
            }

            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);

            // ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
            aIdx += aCnt; // Set starting index for next iteration
        }
    }

    private void DrawTriangle(int t, Transform transform)
    {
        GetTriangleVertices(t, out float3 a, out float3 b, out float3 c);
        Gizmos.DrawLine(
            transform.TransformPoint(a),
            transform.TransformPoint(b));
        Gizmos.DrawLine(
            transform.TransformPoint(b),
            transform.TransformPoint(c));
        Gizmos.DrawLine(
            transform.TransformPoint(c),
            transform.TransformPoint(a));
    }

    private void DrawQuad(int f, Transform transform)
    {
        GetQuadVertices(f, out float3 a, out float3 b, out float3 c, out float3 d);
        Gizmos.DrawLine(
            transform.TransformPoint(a),
            transform.TransformPoint(b));
        Gizmos.DrawLine(
            transform.TransformPoint(b),
            transform.TransformPoint(c));
        Gizmos.DrawLine(
            transform.TransformPoint(c),
            transform.TransformPoint(d));
        Gizmos.DrawLine(
            transform.TransformPoint(d),
            transform.TransformPoint(a));
    }

    public void DrawTriangles(Transform transform)
    {

        foreach (int f in FaceEdges)
        {
            DrawQuad(f, transform);
        }
    }
}
