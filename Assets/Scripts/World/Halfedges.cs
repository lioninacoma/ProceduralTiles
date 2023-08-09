using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Halfedges
{
    public int capacity;

    private int[] triangles;
    private int[] halfedges;

    public Halfedges(int capacity)
    {
        this.capacity = capacity;
        triangles = new int[capacity];
        halfedges = new int[capacity];
        ClearEdges(0, capacity);
    }

    public static int NextHalfedge(int e) { return (e % 3 == 2) ? e - 2 : e + 1; }
    public static int PrevHalfedge(int e) { return (e % 3 == 0) ? e + 2 : e - 1; }
    public static int EdgeOfTriangle(int t, int i) { return 3 * t + i; }
    public static int[] EdgesOfTriangle(int t) { return new int[] { EdgeOfTriangle(t, 0), EdgeOfTriangle(t, 1), EdgeOfTriangle(t, 2) }; }
    public static int TriangleOfEdge(int e) { return (int)Math.Floor(e / 3d); }
    public static ulong GetEdgeHash(int p, int q)
    {
        unchecked // Allow arithmetic overflow, numbers will just "wrap around"
        {
            ulong hashcode = 1430287UL;
            hashcode = hashcode * 7302013UL ^ Convert.ToUInt64(p);
            hashcode = hashcode * 7302013UL ^ Convert.ToUInt64(q);
            return hashcode;
        }
    }

    public int[] GetTriangles()
    {
        return triangles;
    }

    public int[] GetHalfedges()
    {
        return halfedges;
    }

    public int GetEdgeOfTriangle(int t, int i)
    {
        return GetEdge(EdgeOfTriangle(t, i));
    }

    public int GetHalfedgeOfTriangle(int t, int i)
    {
        return GetHalfedge(EdgeOfTriangle(t, i));
    }

    public int GetEdgesAroundPoint(int start, ref int[] result, int maxCount = 16)
    {
        int incoming = start;
        int outgoing;
        int i = 0;
        do
        {
            result[i++] = incoming;
            outgoing = NextHalfedge(incoming);
            incoming = GetHalfedge(outgoing);
        } while (incoming != -1 && incoming != start && i <= maxCount);
        if (i > maxCount) return 0;
        return i;
    }

    public int GetEdge(int e)
    {
        int edge = triangles[e];
        return edge;
    }

    public int GetHalfedge(int e)
    {
        int edge = halfedges[e];
        return edge;
    }

    public void ConnectHalfedges(int a, int b)
    {
        halfedges[a] = b;
        halfedges[b] = a;
    }

    public int AddTriangle(int p0, int p1, int p2, ref int offset)
    {
        int t = offset / 3;
        NewEdge(p0, ref offset);
        NewEdge(p1, ref offset);
        NewEdge(p2, ref offset);
        return t;
    }

    private int NewEdge(int e, ref int offset)
    {
        int i = offset;
        triangles[i] = e;
        halfedges[i] = -1;
        offset++;
        return i;
    }

    public void ClearEdges(int offset, int count)
    {
        for (int i = offset; i < offset + count && i < capacity; i++)
        {
            triangles[i] = -1;
            halfedges[i] = -1;
        }
    }

    public void AddTriangles(List<int> indices, ref int offset, int start = 0, int count = -1)
    {
        int t;
        int p0, p1, p2;

        if (count < 0)
            count = indices.Count;

        for (t = start / 3; t < (start + count) / 3; t++)
        {
            p0 = indices[EdgeOfTriangle(t, 0)];
            p1 = indices[EdgeOfTriangle(t, 1)];
            p2 = indices[EdgeOfTriangle(t, 2)];

            // filter unvalid triangles
            if (p0 == p1 || p0 == p2 || p1 == p2)
                continue;

            AddTriangle(p0, p1, p2, ref offset);
        }
    }

    public void Update(List<int> indices, ref int offset, Dictionary<ulong, int> edgeLookup = null, int start = 0, int count = -1)
    {
        int t, ip, iq, tp, tq;
        int ep, eq;

        ulong hedge;

        if (edgeLookup == null)
            edgeLookup = new Dictionary<ulong, int>();

        if (count < 0)
            count = indices.Count;

        AddTriangles(indices, ref offset, start, count);

        for (t = start / 3; t < (start + count) / 3; t++)
        {
            for (ip = 0; ip < 3; ip++)
            {
                iq = (ip + 1) % 3;
                tp = EdgeOfTriangle(t, ip);
                tq = EdgeOfTriangle(t, iq);
                ep = GetEdge(tp);
                eq = GetEdge(tq);

                hedge = GetEdgeHash(eq, ep); // halfedge hash p <- q
                if (edgeLookup.ContainsKey(hedge))
                {
                    ConnectHalfedges(edgeLookup[hedge], tp);
                }

                hedge = GetEdgeHash(ep, eq); // halfedge hash p -> q
                edgeLookup[hedge] = tp;
            }
        }
    }
}