using Extensions;
using Priority_Queue;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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
        var closedSet = new HashSet<int>();
        Cell current = PickRandomCell(cells);

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

    private bool PlaceTile(Cell cell)
    {
        if (cell != null && cell.CellTile == null)
        {
            InitAllowedTiles(cell);
            var randomTile = PickRandomTile(cell);

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

    private void UpdatedNeighbourCells(Cell cell, FastPriorityQueue<Cell> queue = null, HashSet<int> closedSet = null)
    {
        Cell neighbour;

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

    private CellTile PickRandomTile(Cell cell)
    {
        if (cell.AllowedTiles.Count() == 0) return null;
        return cell.AllowedTiles.RandomElementUsing(rng);
    }

    private Cell PickRandomCell(IEnumerable<Cell> cells)
    {
        return cells.RandomElementUsing(rng);
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
        var tile0 = tiles[t0.Index];
        var tile1 = tiles[t1.Index];
        int con0 = tile0.GetConnection(c0) + t0.Level;
        int con1 = tile1.GetConnection(c1) + t1.Level;
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
            grid.DrawTriangles(transform);
        }

        if (debugCell != null)
        {
            grid.DrawCell(debugCell, transform);
        }
    }
}
