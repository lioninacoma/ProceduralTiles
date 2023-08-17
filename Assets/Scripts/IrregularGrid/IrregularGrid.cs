using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Mathematics;

// Sourced from https://github.com/sketchpunklabs/irregular_grid/
// Based on irruglar grid https://sketchpunklabs.github.io/irregular_grid/
// by Oskar St�lberg 

public class IrregularGrid
{
    public class QuadEdge
    {
        public int Point { get; set; }
        public int Edge { get; set; }
        public int Halfedge { get; set; }
    }

    private Halfedges halfedges;
    private int halfedgesCount;
    private List<float3> vertices;
    private List<int> faceEdges;
    private int[] edgeFaceLookup;

    public IrregularGrid(int maxEdgeCount)
    {
        halfedges = new Halfedges(maxEdgeCount);
        edgeFaceLookup = new int[maxEdgeCount];
        vertices = new List<float3>();
        faceEdges = new List<int>();
        halfedgesCount = 0;

        for (int i = 0; i < maxEdgeCount; i++)
            edgeFaceLookup[i] = -1;
    }
    
    public void Build(float radius = 3, int div = 3, int iter = 50, float relaxScl = .1f, int relaxType = 0, int seed = 123)
    {
        var triangles = new List<int>();
        BuildPoints(radius, div, ref vertices);
        BuildTriangles(div, ref triangles);
        halfedges.Update(triangles, ref halfedgesCount);

        triangles.Clear();
        FaceSubdivide(ref triangles, seed);

        halfedges.ClearEdges(0, halfedgesCount);
        halfedgesCount = 0;
        halfedges.Update(triangles, ref halfedgesCount);

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

    public float3 GetVertex(int index)
    {
        return vertices[index];
    }

    public List<int> GetFaceIndices()
    {
        return faceEdges;
    }

    public int GetFaceCount()
    {
        return faceEdges.Count;
    }

    public int GetEdgeCount()
    {
        return halfedgesCount;
    }

    public int GetVertexCount()
    {
        return vertices.Count;
    }

    public int GetEdge(int e)
    {
        return halfedges.GetEdge(e);
    }

    public int GetHalfedge(int e)
    {
        return halfedges.GetHalfedge(e);
    }

    public int GetFaceIndexOfEdge(int edge)
    {
        return edgeFaceLookup[edge];
    }

    public int GetNeighbourEdges(int edge, ref int[] neighbourEdges)
    {
        return halfedges.GetEdgesAroundPoint(edge, ref neighbourEdges, neighbourEdges.Length);
    }

    private void GetQuadVertices(int f, out float3 a, out float3 b, out float3 c, out float3 d)
    {
        int t0 = Halfedges.TriangleOfEdge(f);
        int t1 = Halfedges.TriangleOfEdge(halfedges.GetHalfedge(f));

        int ai = halfedges.GetEdgeOfTriangle(t0, 0);
        int bi = halfedges.GetEdgeOfTriangle(t0, 1);
        int ci = halfedges.GetEdgeOfTriangle(t0, 2);
        int di = halfedges.GetEdgeOfTriangle(t1, 1);

        a = vertices[ai];
        b = vertices[bi];
        c = vertices[ci];
        d = vertices[di];
    }

    public void GetEdgesOfFaceIndex(int f, QuadEdge[] edges)
    {
        int ae = Halfedges.NextHalfedge(f);
        int be = Halfedges.NextHalfedge(ae);
        int ce = Halfedges.NextHalfedge(halfedges.GetHalfedge(f));
        int de = Halfedges.NextHalfedge(ce);

        int ah = halfedges.GetHalfedge(ae);
        int bh = halfedges.GetHalfedge(be);
        int ch = halfedges.GetHalfedge(ce);
        int dh = halfedges.GetHalfedge(de);

        int ai = halfedges.GetEdge(ae);
        int bi = halfedges.GetEdge(be);
        int ci = halfedges.GetEdge(ce);
        int di = halfedges.GetEdge(de);

        edges[0].Point = ai; edges[0].Edge = ae; edges[0].Halfedge = ah;
        edges[1].Point = bi; edges[1].Edge = be; edges[1].Halfedge = bh;
        edges[2].Point = ci; edges[2].Edge = ce; edges[2].Halfedge = ch;
        edges[3].Point = di; edges[3].Edge = de; edges[3].Halfedge = dh;
    }

    private void GetTriangleVertices(int t, out float3 a, out float3 b, out float3 c)
    {
        int ai = halfedges.GetEdgeOfTriangle(t, 0);
        int bi = halfedges.GetEdgeOfTriangle(t, 1);
        int ci = halfedges.GetEdgeOfTriangle(t, 2);
        a = vertices[ai];
        b = vertices[bi];
        c = vertices[ci];
    }

    private void FaceSubdivide(ref List<int> triangles, int seed = 123)
    {
        var rng = new System.Random(seed);

        int he, t1;
        bool quadFound;

        var midpoints = new int[halfedgesCount];
        var midpointExists = new bool[halfedgesCount];
        var triProcessed = new bool[halfedgesCount / 3];
        var triangleIndices = Enumerable.Range(0, halfedgesCount / 3);
        var edgeIndices = Enumerable.Range(0, 3);

        foreach (int t0 in triangleIndices.OrderBy(a => rng.Next()))
        {
            if (triProcessed[t0]) continue;

            quadFound = false;
            triProcessed[t0] = true;

            foreach (int i in edgeIndices.OrderBy(a => rng.Next()))
            {
                he = halfedges.GetHalfedgeOfTriangle(t0, i);
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

        ah = halfedges.GetHalfedgeOfTriangle(t, 0);
        bh = halfedges.GetHalfedgeOfTriangle(t, 1);
        ch = halfedges.GetHalfedgeOfTriangle(t, 2);

        ai = halfedges.GetEdge(ae);
        bi = halfedges.GetEdge(be);
        ci = halfedges.GetEdge(ce);

        a = vertices[ai];
        b = vertices[bi];
        c = vertices[ci];

        if (ah >= 0 && midpointExists[ah])
        {
            ab = midpoints[ah];
        }
        else
        {
            v = math.lerp(a, b, 0.5f);
            ab = vertices.Count;
            vertices.Add(v);
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
            bc = vertices.Count;
            vertices.Add(v);
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
            ca = vertices.Count;
            vertices.Add(v);
            midpointExists[ce] = true;
            midpoints[ce] = ca;
        }

        v = (a + b + c) / 3f;
        cp = vertices.Count;
        vertices.Add(v);

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
        ce = Halfedges.NextHalfedge(halfedges.GetHalfedge(s));
        de = Halfedges.NextHalfedge(ce);

        ah = halfedges.GetHalfedge(ae);
        bh = halfedges.GetHalfedge(be);
        ch = halfedges.GetHalfedge(ce);
        dh = halfedges.GetHalfedge(de);

        ai = halfedges.GetEdge(ae);
        bi = halfedges.GetEdge(be);
        ci = halfedges.GetEdge(ce);
        di = halfedges.GetEdge(de);

        a = vertices[ai];
        b = vertices[bi];
        c = vertices[ci];
        d = vertices[di];

        if (ah >= 0 && midpointExists[ah])
        {
            ab = midpoints[ah];
        }
        else
        {
            v = math.lerp(a, b, 0.5f);
            ab = vertices.Count;
            vertices.Add(v);
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
            bc = vertices.Count;
            vertices.Add(v);
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
            cd = vertices.Count;
            vertices.Add(v);
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
            da = vertices.Count;
            vertices.Add(v);
            midpointExists[de] = true;
            midpoints[de] = da;
        }

        v = (a + b + c + d) / 4f;
        cp = vertices.Count;
        vertices.Add(v);

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

        faceEdges.Add(faceEdge);
        for (int i = 0; i < 6; i++)
            edgeFaceLookup[startEdge + i] = faceEdge;
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
        var isInnerVert = new bool[vertices.Count];
        var reprEdge = new int[vertices.Count];
        for (i = 0; i < isInnerVert.Length; i++)
            isInnerVert[i] = true;

        for (t = 0; t < halfedgesCount / 3; t++)
        {
            for (i = 0; i < 3; i++)
            {
                e = Halfedges.EdgeOfTriangle(t, i);
                p = halfedges.GetEdge(e);
                he = halfedges.GetHalfedge(e);
                isInnerVert[p] &= he >= 0;
                reprEdge[p] = he;
            }
        }

        for (int loop = 0; loop < iter; loop++)
        {
            for (i = 0; i < vertices.Count; i++)
            {
                if (!isInnerVert[i]) continue;

                e = reprEdge[i];
                neighbourCount = GetNeighbourEdges(e, ref neighbourEdges);
                if (neighbourCount == 0) continue;

                centroid = float3.zero;
                weight = 0;
                v = vertices[i];

                for (j = 0; j < neighbourCount; j++)
                {
                    n = vertices[halfedges.GetEdge(neighbourEdges[j])];
                    w = math.distance(v, n);
                    weight += w;
                    pos = n * w;
                    centroid += pos;
                }

                centroid /= weight;
                vertices[i] = centroid;
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

        var forces = new float3[vertices.Count];
        var isInnerVert = new bool[vertices.Count];
        for (i = 0; i < isInnerVert.Length; i++)
            isInnerVert[i] = true;

        for (t = 0; t < halfedgesCount / 3; t++)
        {
            for (i = 0; i < 3; i++)
            {
                e = Halfedges.EdgeOfTriangle(t, i);
                p = halfedges.GetEdge(e);
                he = halfedges.GetHalfedge(e);
                isInnerVert[p] &= he >= 0;
            }
        }

        for (int loop = 0; loop < iter; loop++)
        {
            force = float3.zero; 
            for (i = 0; i < forces.Length; i++)
                forces[i] = float3.zero;

            foreach (int f in faceEdges)
            {
                GetEdgesOfFaceIndex(f, edges);
                centroid = float3.zero;

                for (i = 0; i < 4; i++)
                {
                    edge = edges[i];
                    centroid += vertices[edge.Point];
                }

                centroid /= 4f;

                for (i = 0; i < 4; i++)
                {
                    edge = edges[i];
                    if (isInnerVert[edge.Point])
                    {
                        force += (vertices[edge.Point] - centroid);
                        force = RotateY90(force);
                    }
                }

                force /= 4f;

                for (i = 0; i < 4; i++)
                {
                    edge = edges[i];
                    if (isInnerVert[edge.Point])
                    {
                        forces[edge.Point] += ((centroid + force) - vertices[edge.Point]);
                        force = RotateY90(force);
                    }
                }
            }

            for (i = 0; i < forces.Length; i++)
            {
                if (isInnerVert[i])
                {
                    vertices[i] += (forces[i] * relaxScl);
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
        //Gizmos.color = Color.gray;
        //for (int t = 0; t < halfedgesCount / 3; t++)
        //{
        //    DrawTriangle(t, transform);
        //}

        Gizmos.color = Color.black;
        foreach (int f in faceEdges)
        {
            DrawQuad(f, transform);
        }
    }
}
