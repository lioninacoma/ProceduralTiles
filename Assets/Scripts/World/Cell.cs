using Priority_Queue;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;

public class Cell : FastPriorityQueueNode
{
    public int Index { get; set; }
    public int[] Points { get; set; }
    public int[] Neighbours { get; set; }
    public int[][] NeighboursOfPoints { get; set; }
    public Dictionary<int, int> IndicesOfPoints { get; set; }
    public CellTile CellTile { get; set; }
    public IEnumerable<CellTile> AllowedTiles { get; set; }

    public Cell(int index, int[] points, int[] neighbours, int[][] neighboursOfPoints, Dictionary<int, int> indicesOfPoints)
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
