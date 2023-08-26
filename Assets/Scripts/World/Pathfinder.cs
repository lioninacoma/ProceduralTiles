using Priority_Queue;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Pathfinder
{
    private class TriangleNode : FastPriorityQueueNode
    {
        public int t;
        public TriangleNode(int t)
        {
            this.t = t;
        }
    }

    private Halfedges Halfedges;
    private int HalfedgesCount;
    private Dictionary<ulong, int> EdgeLookup;
    private List<Vector3> Vertices;

    public Pathfinder(int maxEdgeCount)
    {
        Halfedges = new Halfedges(maxEdgeCount);
        HalfedgesCount = 0;
        EdgeLookup = new Dictionary<ulong, int>();
        Vertices = new List<Vector3>();
    }

    public void Clear()
    {
        HalfedgesCount = 0;
        EdgeLookup.Clear();
        Vertices.Clear();
    }

    public int UpdateGrid(Mesh mesh)
    {
        return UpdateGrid(mesh.vertices, mesh.GetIndices(0));
    }

    public int UpdateGrid(IEnumerable<Vector3> vertices, IEnumerable<int> indices)
    {
        int vertexStart = Vertices.Count;
        Vertices.AddRange(vertices);

        var indexList = indices.Select(i => i + vertexStart).ToList();
        Halfedges.Update(indexList, ref HalfedgesCount, EdgeLookup);

        return vertexStart;
    }

    public List<int> FindPath(int start, int goal)
    {
        var path = new List<int>();
        AStar(start, goal, ref path);
        return path;
    }

    public Vector3 GetTriangleCenter(int t)
    {
        var points = Halfedges.GetTrianglePoints(t);
        var mid = Vector3.zero;
        foreach (int p in points)
            mid += Vertices[p];
        return mid / 3f;
    }

    private float GetCost(int ta, int tb, Vector3 a, Vector3 b)
    {
        return Heuristics(a, b);
    }

    private float Heuristics(Vector3 a, Vector3 b)
    {
        return Utils.ManhattanDistance(a, b);
    }

    private void AStar(int start, int goal, ref List<int> path)
    {
        if (start == goal) return;

        var frontier = new FastPriorityQueue<TriangleNode>(10000);
        var cameFrom = new Dictionary<int, TriangleNode>();
        var costSoFar = new Dictionary<int, float>();
        float newCost, currentCost, priority;
        bool pathFound = false;
        TriangleNode current;
        Vector3 currentPoint, nextPoint;
        Vector3 goalPoint = GetTriangleCenter(goal);

        frontier.Enqueue(new TriangleNode(start), 0);
        cameFrom[start] = null;
        costSoFar[start] = 0;

        while (frontier.Count > 0)
        {
            current = frontier.Dequeue();
            currentCost = costSoFar[current.t];
            currentPoint = GetTriangleCenter(current.t);

            if (current.t == goal)
            {
                pathFound = true;
                break;
            }

            foreach (int next in Halfedges.GetTriangleNeighbours(current.t))
            {
                nextPoint = GetTriangleCenter(next);
                newCost = currentCost + GetCost(current.t, next, currentPoint, nextPoint);

                if (!costSoFar.ContainsKey(next) || newCost < costSoFar[next])
                {
                    costSoFar[next] = newCost;
                    priority = newCost + Heuristics(goalPoint, nextPoint);
                    frontier.Enqueue(new TriangleNode(next), priority);
                    cameFrom[next] = current;
                }
            }
        }

        if (pathFound)
        {
            int ct = goal;
            int nt = cameFrom[ct].t;
            path.Add(goal);

            while (ct != start)
            {
                ct = nt;
                path.Add(ct);
                if (ct == start) break;
                nt = cameFrom[nt].t;
            }

            path.Reverse();
        }
    }

    public void DrawPath(List<int> path)
    {
        if (Halfedges != null)
        {
            List<int> points;

            foreach (var t in path)
            {
                points = Halfedges.GetTrianglePoints(t);
                Gizmos.DrawLine(Vertices[points[0]], Vertices[points[1]]);
                Gizmos.DrawLine(Vertices[points[1]], Vertices[points[2]]);
                Gizmos.DrawLine(Vertices[points[2]], Vertices[points[0]]);
            }
        }
    }

    public void DrawGrid()
    {
        if (Halfedges != null)
        {
            List<int> points;

            for (int t = 0; t < HalfedgesCount / 3; t++)
            {
                points = Halfedges.GetTrianglePoints(t);
                Gizmos.DrawLine(Vertices[points[0]], Vertices[points[1]]);
                Gizmos.DrawLine(Vertices[points[1]], Vertices[points[2]]);
                Gizmos.DrawLine(Vertices[points[2]], Vertices[points[0]]);
            }
        }
    }
}
