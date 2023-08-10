using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class Cell
{
    public int Index { get; set; }
    public int[] Points { get; set; }
    public int[] Neighbours { get; set; }
    public bool Occupied { get; set; }
    public string OccTileName { get; set; }
    public int OccTileIndex { get; set; }

    public int GetNeighbourIndex(Cell cell)
    {
        for (int i = 0; i < Neighbours.Length; i++)
        {
            if (Neighbours[i] == cell.Index)
                return i;
        }
        return -1;
    }

    public Cell(int index, int[] points, int[] neighbours)
    {
        Index = index;
        Points = points;
        Neighbours = neighbours;
        Occupied = false;
        OccTileName = "";
        OccTileIndex = -1;
    }
}
