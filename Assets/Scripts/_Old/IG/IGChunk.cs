using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class IGChunk : MonoBehaviour
{
    private static readonly int CELL_XY_BUFFER_SIZE = 32000;
    private static readonly int HALFEDGES_BUFFER_SIZE = CELL_XY_BUFFER_SIZE * 6; // edge cell has 2 triangles with each 3 halfedges
    private static readonly int GRID_RELAX_ITERATIONS = 20;
    private static readonly float GRID_RELAX_SCALE = .22f;

    [Range(1f, 1000f)] public float GridRadius = 32f;
    [Range(2, 80)] public int GridCellDiv = 24;
    [Range(0f, 1f)] public float GridCellHeight = 1f;
    public int ChunkSeed = 123;

    private IGSurface Surface;
    private IrregularGrid Grid;

    private void Awake()
    {
        var surfaceObj = CreateChildObject("Surface");
        Surface = surfaceObj.AddComponent<IGSurface>();
        Grid = new IrregularGrid(HALFEDGES_BUFFER_SIZE);
        Grid.Build(GridRadius, GridCellDiv, GRID_RELAX_ITERATIONS, GRID_RELAX_SCALE, 0, ChunkSeed);
    }

    private void Start()
    {
        Surface.Build(Grid, GridCellHeight);
    }

    private GameObject CreateChildObject(string name)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(transform);
        return obj;
    }
}
