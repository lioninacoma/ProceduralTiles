using Extensions;
using Priority_Queue;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEditor.VersionControl;
using UnityEngine;
using UnityEngine.Rendering;

public class WorldGenerator : MonoBehaviour
{
    private static readonly bool DEBUG_LOG = false;

    [Range(1f, 1000f)] public float radius = 3f;
    [Range(2, 80)] public int div = 3;
    [Range(0, 100)] public int relaxIterations = 50;
    [Range(0, .46f)] public float relaxScale = .1f;
    [Range(0, 1)] public int relaxType = 0;
    [Range(1, 10)] public int tileLevels = 1;
    public int seed = 123;

    public Tile[] tiles;
    public Material tileMaterial;

    private Grid grid;
    private TileMesh[] tileMeshes;
    private System.Random rng;

    void OnDisable()
    {
        if (tileMeshes != null)
            for (int i = 0; i < tileMeshes.Length; i++)
                tileMeshes[i].Dispose();
    }

    private void InitGrid()
    {
        UnityEngine.Random.InitState(seed);
        rng = new System.Random(seed);
        grid = new Grid();
        grid.Build(radius, div, relaxIterations, relaxScale, relaxType, seed);
    }

    // Start is called before the first frame update
    void Start()
    {
        if (Application.isPlaying)
        {
            InitGrid();

            tileMeshes = new TileMesh[tiles.Length];
            for (int i = 0; i < tileMeshes.Length; i++)
            {
                tiles[i].TileIndex = i;
                tileMeshes[i] = new TileMesh();
                tileMeshes[i].InitFromTile(tiles[i]);
            }

            GenerateTiles();
        }
    }

    private void GenerateTiles()
    {
        var cells = grid.GetCells();
        var queue = new FastPriorityQueue<Cell>(cells.Count());
        
        foreach (var cell in cells)
        {
            queue.Enqueue(cell, 1f);
        }

        Cell current;

        while (queue.Count > 0)
        {
            current = queue.Dequeue();
            PlaceTile(current, queue);
        }
    }

    private void PlaceTile(Cell cell, FastPriorityQueue<Cell> queue = null)
    {
        if (cell != null && cell.CellTile == null)
        {
            InitAllowedTiles(cell);
            var randomTile = PickRandomTile(cell);

            if (randomTile != null)
            {
                SpawnTileOnCell(cell, randomTile);
                UpdatedNeighbourCells(cell, queue);
                if (DEBUG_LOG) Debug.Log(tiles[cell.CellTile.Index].name + " spawned at tile level " + cell.CellTile.Level);
            }
            else
            {
                Debug.Log("No connecting tile found for " + cell.Index);
            }
        }
        else
        {
            Debug.Log(cell == null ? "Cell is empty!" : "Cell tile is already set!");
        }
    }

    private void UpdatedNeighbourCells(Cell cell, FastPriorityQueue<Cell> queue = null)
    {
        Cell neighbour;

        foreach (var n in cell.Neighbours)
        {
            neighbour = grid.GetCell(n);

            if (neighbour.CellTile != null) 
                continue;

            UpdateAllowedTiles(neighbour);

            if (queue != null)
                queue.UpdatePriority(neighbour, CalcPriority(neighbour));
        }
    }

    private CellTile PickRandomTile(Cell cell)
    {
        if (cell.AllowedTiles.Count() == 0) return null;
        return cell.AllowedTiles.RandomElementUsing(rng);
    }

    private float CalcPriority(Cell cell)
    {
        float priority = 1f;

        if (cell.AllowedTiles != null)
        {
            priority = cell.AllowedTiles.Count() / (float)tiles.Length;
            return priority;
        }
           
        return priority;
    }

    private Cell debugCell;

    private GameObject CreateTile(string name)
    {
        var tile = new GameObject(name);
        tile.transform.SetParent(transform);

        var meshFilter = tile.AddComponent<MeshFilter>();
        meshFilter.mesh = new Mesh();

        var meshRenderer = tile.AddComponent<MeshRenderer>();
        meshRenderer.material = tileMaterial;

        return tile;
    }

    private void SpawnTileOnCell(Cell cell, CellTile tile)
    {
        var tileMesh = tileMeshes[tile.Index];
        var tileObject = CreateTile(tileMesh.tileName);
        var tileInfo = tileObject.AddComponent<TileInfo>();
        tileInfo.TileIndex = tile.Index;
        tileInfo.TileLevel = tile.Level;
        tileInfo.TileRotation = tile.Rotation;
        tileInfo.CellIndex = cell.Index;
        var meshFilter = tileObject.GetComponent<MeshFilter>();
        tileMesh.GetMesh(meshFilter, cell, grid, tile.Level);
        cell.CellTile = tile;
    }

    private void InitAllowedTiles(Cell cell)
    {
        if (cell.AllowedTiles == null)
        {
            cell.AllowedTiles = Enumerable.Range(0, tileLevels)
                .SelectMany(l => Enumerable.Range(0, tiles.Length)
                .Select(i => new CellTile() { Index = i, Level = l, Rotation = 0 }));
        }
    }

    public bool IsConnecting(CellTile t0, CellTile t1, int c0, int c1)
    {
        int con0 = tiles[t0.Index].GetConnection(c0) + t0.Level;
        int con1 = tiles[t1.Index].GetConnection(c1) + t1.Level;
        return con0 == con1;
    }

    public IEnumerable<CellTile> FilterTiles(IEnumerable<CellTile> tileSet, CellTile t0, int c0, int c1)
    {
        return tileSet.Where(t1 => IsConnecting(t0, t1, c0, c1));
    }

    private void UpdateAllowedTiles(Cell cell)
    {
        Cell neighbour;
        int point, conNeighbour, n;

        InitAllowedTiles(cell);

        for (int i = 0; i < cell.Points.Length; i++)
        {
            point = cell.Points[i];

            for (int j = 0; j < cell.NeighboursOfPoints[i].Length; j++)
            {
                n = cell.NeighboursOfPoints[i][j];
                neighbour = grid.GetCell(cell.Neighbours[n]);

                if (neighbour.CellTile != null)
                {
                    /* one connector per point 
                     * for each tile
                     * p2-------p3
                     * | c2   c3 |
                     * |         |
                     * | c1   c0 |
                     * p1-------p0
                     */
                    conNeighbour = neighbour.IndicesOfPoints[point];
                    // Check if tiles connect by comparing connector flags.
                    // Filters non-connecting and returns allowed tiles.
                    cell.AllowedTiles = FilterTiles(cell.AllowedTiles, neighbour.CellTile, conNeighbour, i);
                }
            }
        }
    }

    void Update()
    {
        if (Input.GetMouseButtonUp(0))
        {
            var camera = Camera.main;
            var mouseRay = camera.ScreenPointToRay(Input.mousePosition);
            var cell = grid.RaycastCell(mouseRay, transform);
            debugCell = cell;
            PlaceTile(cell);
        }
    }

    void OnDrawGizmos()
    {
        if (grid != null)
        {
            grid.DrawTriangles(transform);
        }

        if (debugCell != null)
        {
            grid.DrawCell(debugCell, transform);
        }
    }
}
