using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class Cell
{
    public int Index { get; set; }
    public int[] Points { get; set; }
    public int[] Neighbours { get; set; }

    public Cell(int index, int[] points, int[] neighbours)
    {
        Index = index;
        Points = points;
        Neighbours = neighbours;
    }
}
