using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

public class Cell2DGrid
{
    private Cell2D[] cells;
    private int cellCount;
    private IrregularGrid baseGrid;

    public Cell2DGrid(IrregularGrid baseGrid, int maxCellCount)
    {
        this.baseGrid = baseGrid;
        cells = new Cell2D[maxCellCount];
        cellCount = 0;

        BuildCells();
    }

    public IrregularGrid GetBaseGrid() { return baseGrid; }

    // TODO: ersetzen mit Quadtree search
    public Cell2D RaycastCell(Ray ray, Transform transform)
    {
        Cell2D curr, cell = null;

        for (int i = 0; i < cellCount; i++)
        {
            curr = cells[i];

            if (curr != null && RayCellIntersection(ray.origin, ray.direction, transform, curr))
            {
                cell = curr;
                break;
            }
        }

        return cell;
    }

    public Cell2D GetCell(int index)
    {
        return cells[index];
    }

    public IEnumerable<Cell2D> GetCells() { return cells.Take(cellCount); }

    private bool RayCellIntersection(float3 origin, float3 dir, Transform transform, Cell2D cell)
    {
        float3 a = transform.TransformPoint(baseGrid.GetVertex(cell.Points[0]));
        float3 b = transform.TransformPoint(baseGrid.GetVertex(cell.Points[1]));
        float3 c = transform.TransformPoint(baseGrid.GetVertex(cell.Points[2]));
        float3 d = transform.TransformPoint(baseGrid.GetVertex(cell.Points[3]));
        return Utils.RayTriangleIntersection(origin, dir, a, b, c, out _, out _, out _, out _)
            || Utils.RayTriangleIntersection(origin, dir, c, d, a, out _, out _, out _, out _);
    }

    private static int FaceToCellIndex(int f)
    {
        return (f - 2) / 6;
    }

    private void BuildCells()
    {
        int i, n, e, h;
        IrregularGrid.QuadEdge edge;
        Cell2D cell;
        int[] points;
        var neighbourEdges = new int[16];
        int neighbourCount;
        List<int> neighbours;
        HashSet<int>[] neighboursOfPoints;
        Dictionary<int, int> indicesOfPoints;

        var neighbourIndices = new Dictionary<int, int>();

        var edges = new IrregularGrid.QuadEdge[4];
        for (i = 0; i < 4; i++) edges[i] = new IrregularGrid.QuadEdge();

        foreach (var f in baseGrid.GetFaceIndices())
        {
            baseGrid.GetEdgesOfFaceIndex(f, edges);

            points = new int[4];
            neighboursOfPoints = new HashSet<int>[4] { new(), new(), new(), new() };
            indicesOfPoints = new Dictionary<int, int>();
            neighbours = new List<int>();
            neighbourIndices.Clear();

            for (i = 0; i < 4; i++)
            {
                edge = edges[i];
                points[i] = edge.Point;
                indicesOfPoints[edge.Point] = i;

                if (edge.Edge >= 0)
                {
                    neighbourCount = baseGrid.GetNeighbourEdges(Halfedges.PrevHalfedge(edge.Edge), ref neighbourEdges);

                    for (e = 0; e < neighbourCount; e++)
                    {
                        h = neighbourEdges[e];
                        n = baseGrid.GetFaceIndexOfEdge(h);

                        if (n >= 0 && n != f)
                        {
                            if (!neighbourIndices.ContainsKey(n))
                            {
                                neighbourIndices[n] = neighbours.Count;
                                neighboursOfPoints[i].Add(neighbours.Count);
                                neighbours.Add(FaceToCellIndex(n));
                            }
                            else
                            {
                                neighboursOfPoints[i].Add(neighbourIndices[n]);
                            }
                        }
                    }
                }
            }

            cell = new Cell2D(FaceToCellIndex(f), points, neighbours.ToArray(), neighboursOfPoints.Select(n => n.ToArray()).ToArray(), indicesOfPoints);
            cells[cell.Index] = cell;
            cellCount++;
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

    public void DrawCell(Cell2D cell, Transform transform)
    {
        var a = baseGrid.GetVertex(cell.Points[0]);
        var b = baseGrid.GetVertex(cell.Points[1]);
        var c = baseGrid.GetVertex(cell.Points[2]);
        var d = baseGrid.GetVertex(cell.Points[3]);
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

    public void DrawCellNeighbours(Cell2D cell, Transform transform)
    {
        for (int i = 0; i < 4; i++)
        {
            if (cell.Neighbours[i] < 0) continue;
            Gizmos.color = DEBUG_COLORS[i + 1];
            var n = cells[cell.Neighbours[i]];
            DrawCell(n, transform);
        }
    }
}
