using Extensions;
using Priority_Queue;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class World2DGenerator : MonoBehaviour
{
    private static readonly bool DEBUG_LOG = false;
    private static readonly int CELL_BUFFER_SIZE = 32000;
    private static readonly int HALFEDGES_BUFFER_SIZE = CELL_BUFFER_SIZE * 6;

    [Range(1f, 1000f)] public float radius = 3f;
    [Range(2, 80)] public int div = 3;
    [Range(0, 100)] public int relaxIterations = 50;
    [Range(0, .46f)] public float relaxScale = .1f;
    [Range(0, 1)] public int relaxType = 0;
    [Range(1, 10)] public int tileLevels = 1;
    public int seed = 123;

    public Tile2D[] tiles;
    public Material tileMaterial;

    private Cell2DGrid grid;
    private Tile2DMesh[] tileMeshes;
    private System.Random rng;

    void OnDisable()
    {
        if (tileMeshes != null)
            for (int i = 0; i < tileMeshes.Length; i++)
                tileMeshes[i].Dispose();
    }

    private void InitGrid()
    {
        Random.InitState(seed);
        rng = new System.Random(seed);
        var irregularGrid = new IrregularGrid(HALFEDGES_BUFFER_SIZE);
        irregularGrid.Build(radius, div, relaxIterations, relaxScale, relaxType, seed);
        grid = new Cell2DGrid(irregularGrid, CELL_BUFFER_SIZE);
    }

    // Start is called before the first frame update
    void Start()
    {
        if (Application.isPlaying)
        {
            InitGrid();

            float chanceTotal = 0f;

            tileMeshes = new Tile2DMesh[tiles.Length];
            for (int i = 0; i < tileMeshes.Length; i++)
            {
                tiles[i].TileIndex = i;
                chanceTotal += tiles[i].SpawnChance;
                tileMeshes[i] = new Tile2DMesh();
                tileMeshes[i].InitFromTile(tiles[i]);
            }

            for (int i = 0; i < tileMeshes.Length; i++)
            {
                tiles[i].SpawnProbability = tiles[i].SpawnChance / (float)chanceTotal;
            }

            //GenerateTiles();
        }
    }

    private void GenerateTiles()
    {
        var cells = grid.GetCells();
        var queue = new FastPriorityQueue<Cell2D>(cells.Count());
        var closedSet = new HashSet<int>();
        Cell2D current = PickRandomCell(cells);

        closedSet.Add(current.Index);
        queue.Enqueue(current, 1f);

        while (queue.Count > 0)
        {
            current = queue.Dequeue();

            if (PlaceTile(current))
            {
                // Updates allowed neighbour tiles,
                // enqueues unvisited neighbours
                // and updates neighbour priorities.
                UpdatedNeighbourCells(current, queue, closedSet);
            }
        }
    }

    private bool PlaceTile(Cell2D cell)
    {
        if (cell != null && cell.CellTile == null)
        {
            InitAllowedTiles(cell);
            var randomTile = WeightedPickRandomTile(cell);

            if (randomTile != null)
            {
                SpawnTileOnCell(cell, randomTile);
                if (DEBUG_LOG) Debug.Log(tiles[cell.CellTile.Index].name + " spawned at tile level " + cell.CellTile.Level);
                return true;
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
        return false;
    }

    private void UpdatedNeighbourCells(Cell2D cell, FastPriorityQueue<Cell2D> queue = null, HashSet<int> closedSet = null)
    {
        Cell2D neighbour;

        foreach (var n in cell.Neighbours)
        {
            neighbour = grid.GetCell(n);

            if (neighbour.CellTile != null)
                continue;

            UpdateAllowedTiles(neighbour);

            if (queue != null && closedSet != null)
            {
                if (closedSet.Contains(neighbour.Index))
                {
                    queue.UpdatePriority(neighbour, CalcPriority(neighbour));
                }
                else
                {
                    queue.Enqueue(neighbour, CalcPriority(neighbour));
                    closedSet.Add(neighbour.Index);
                }
            }
        }
    }

    private Cell2DTile PickRandomTile(Cell2D cell)
    {
        if (cell.AllowedTiles.Count() == 0) return null;
        return cell.AllowedTiles.RandomElementUsing(rng);
    }

    private Cell2DTile WeightedPickRandomTile(Cell2D cell)
    {
        if (cell.AllowedTiles.Count() == 0) 
            return null;

        float r = Random.value;
        float cumulative = 0f;

        foreach (var t in cell.AllowedTiles)
        {
            cumulative += tiles[t.Index].SpawnProbability;
            if (r < cumulative)
            {
                return t;
            }
        }

        return cell.AllowedTiles.RandomElementUsing(rng);
    }

    private Cell2D PickRandomCell(IEnumerable<Cell2D> cells)
    {
        return cells.RandomElementUsing(rng);
    }

    private float CalcPriority(Cell2D cell)
    {
        float priority = 1f;

        if (cell.AllowedTiles != null)
        {
            priority = cell.AllowedTiles.Count() / (float)tiles.Length;
            return priority;
        }
           
        return priority;
    }

    private Cell2D debugCell;

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

    private void SpawnTileOnCell(Cell2D cell, Cell2DTile tile)
    {
        var tileMesh = tileMeshes[tile.Index];
        var tileObject = CreateTile(tileMesh.tileName);
        var tileInfo = tileObject.AddComponent<Tile2DInfo>();
        tileInfo.TileIndex = tile.Index;
        tileInfo.TileLevel = tile.Level;
        tileInfo.TileRotation = tile.Rotation;
        tileInfo.CellIndex = cell.Index;
        var meshFilter = tileObject.GetComponent<MeshFilter>();
        tileMesh.GetMesh(meshFilter, cell, grid.GetBaseGrid(), tile.Level, tile.Rotation * 90f);
        cell.CellTile = tile;
    }

    private void InitAllowedTiles(Cell2D cell)
    {
        if (cell.AllowedTiles == null)
        {
            cell.AllowedTiles = Enumerable.Range(0, 4) // y rotation indices
                .SelectMany(r => Enumerable.Range(0, tileLevels) // y offset
                .SelectMany(l => Enumerable.Range(0, tiles.Length) // tile index
                .Select(i => new Cell2DTile() { Index = i, Level = l, Rotation = r })));
        }
    }

    public bool IsConnecting(Cell2DTile t0, Cell2DTile t1, int c0, int c1)
    {
        var tile0 = tiles[t0.Index];
        var tile1 = tiles[t1.Index];
        int con0 = tile0.GetConnection(c0, t0.Rotation) + t0.Level;
        int con1 = tile1.GetConnection(c1, t1.Rotation) + t1.Level;
        return con0 == con1;
    }

    public IEnumerable<Cell2DTile> FilterTiles(IEnumerable<Cell2DTile> tileSet, Cell2DTile t0, int c0, int c1)
    {
        return tileSet.Where(t1 => IsConnecting(t0, t1, c0, c1));
    }

    private void UpdateAllowedTiles(Cell2D cell)
    {
        Cell2D neighbour;
        int point, conNeighbour, n;

        InitAllowedTiles(cell);

        for (int i = 0; i < cell.Points.Length; i++)
        {
            point = cell.Points[i];

            for (int j = 0; j < cell.NeighboursOfPoints[i].Length; j++)
            {
                n = cell.NeighboursOfPoints[i][j];
                int neighbourCellIndex = cell.Neighbours[n];
                neighbour = grid.GetCell(neighbourCellIndex);

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

            if (PlaceTile(cell))
            {
                UpdatedNeighbourCells(cell);
            }

            debugCell = cell;
        }
    }

    void OnDrawGizmos()
    {
        if (grid != null)
        {
            grid.GetBaseGrid().DrawTriangles(transform);
        }

        if (debugCell != null)
        {
            grid.DrawCell(debugCell, transform);
        }
    }
}
