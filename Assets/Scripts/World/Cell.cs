using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class Cell
{
    public int Index { get; set; }
    public int[] Points { get; set; }
    public int[] Neighbours { get; set; }
    public int[][] NeighboursOfPoints { get; set; }
    public Dictionary<int, int> IndicesOfPoints { get; set; }
    public bool Occupied { get; set; }
    public string OccTileName { get; set; }
    public int OccTileIndex { get; set; }

    public Cell(int index, int[] points, int[] neighbours, int[][] neighboursOfPoints, Dictionary<int, int> indicesOfPoints)
    {
        Index = index;
        Points = points;
        Neighbours = neighbours;
        NeighboursOfPoints = neighboursOfPoints;
        IndicesOfPoints = indicesOfPoints;
        Occupied = false;
        OccTileName = "";
        OccTileIndex = -1;
    }
}
