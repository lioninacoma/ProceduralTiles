using Priority_Queue;
using System.Collections.Generic;

public class Cell2D : FastPriorityQueueNode
{
    public int Index { get; set; }
    public int[] Points { get; set; }
    public int[] Neighbours { get; set; }
    public int[][] NeighboursOfPoints { get; set; }
    public Dictionary<int, int> IndicesOfPoints { get; set; }
    public Cell2DTile CellTile { get; set; }
    public IEnumerable<Cell2DTile> AllowedTiles { get; set; }

    public Cell2D(int index, int[] points, int[] neighbours, int[][] neighboursOfPoints, Dictionary<int, int> indicesOfPoints)
    {
        Index = index;
        Points = points;
        Neighbours = neighbours;
        NeighboursOfPoints = neighboursOfPoints;
        IndicesOfPoints = indicesOfPoints;
        CellTile = null;
        AllowedTiles = null;
    }
}
