using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEditor.VersionControl;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteInEditMode]
public class WorldGenerator : MonoBehaviour
{
    [Range(1f, 1000f)] public float radius = 3f;
    [Range(2, 80)] public int div = 3;
    [Range(0, 100)] public int relaxIterations = 50;
    [Range(0, .46f)] public float relaxScale = .1f;
    [Range(0, 1)] public int relaxType = 0;

    public Tile[] tiles;
    public Material tileMaterial;

    private Grid grid;
    private TileMesh[] tileMeshes;

    void OnEnable()
    {
        InitGrid();
    }

    void OnDisable()
    {
        if (tileMeshes != null)
            for (int i = 0; i < tileMeshes.Length; i++)
                tileMeshes[i].Dispose();
    }

    void OnValidate()
    {
        InitGrid();
    }

    private void InitGrid()
    {
        grid = new Grid();
        grid.Build(radius, div, relaxIterations, relaxScale, relaxType);
    }

    // Start is called before the first frame update
    void Start()
    {
        UnityEngine.Random.InitState(123);
        tileMeshes = new TileMesh[tiles.Length];
        for (int i = 0; i < tileMeshes.Length; i++)
        {
            tiles[i].TileIndex = i;
            tileMeshes[i] = new TileMesh();
            tileMeshes[i].InitFromTile(tiles[i]);
        }
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

    private void SpawnTileOnCell(TileMesh tileMesh, Cell cell)
    {
        var tile = CreateTile(tileMesh.tileName);
        var meshFilter = tile.GetComponent<MeshFilter>();
        tileMesh.GetMesh(meshFilter, cell, grid);
    }

    private Tile[] GetConnectingTiles(Cell cell, List<Cell> neighbours)
    {
        Cell neighbour;
        Tile neighbourTile;
        int cn, nc;
        IEnumerable<Tile> connectingTiles = tiles.ToList();

        for (int dir = 0; dir < neighbours.Count; dir++)
        {
            neighbour = neighbours[dir];
            cn = cell.GetNeighbourIndex(neighbour);
            nc = neighbour.GetNeighbourIndex(cell);
            neighbourTile = tiles[neighbour.OccTileIndex];
            connectingTiles = neighbourTile.ConnectingTiles(connectingTiles, nc, cn);
        }

        return connectingTiles.ToArray();
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetMouseButtonUp(0))
        {
            var camera = Camera.main;
            var mouseRay = camera.ScreenPointToRay(Input.mousePosition);
            var cell = grid.RaycastCell(mouseRay, transform);
            debugCell = cell;

            if (cell != null && !cell.Occupied)
            {
                var allowedTiles = tiles;
                var neighbourCells = new List<Cell>();
                GetOccupiedNeighbourTiles(cell, ref neighbourCells);

                if (neighbourCells.Count > 0)
                {
                    allowedTiles = GetConnectingTiles(cell, neighbourCells);
                }

                if (allowedTiles.Length > 0)
                {
                    var randomTile = allowedTiles[UnityEngine.Random.Range(0, allowedTiles.Length)];
                    SpawnTileOnCell(tileMeshes[randomTile.TileIndex], cell);
                    cell.Occupied = true;
                    cell.OccTileName = randomTile.name;
                    cell.OccTileIndex = randomTile.TileIndex;
                }
                else
                {
                    Debug.Log("No connecting tile found!");
                }
            }
        }
    }

    private void GetOccupiedNeighbourTiles(Cell cell, ref List<Cell> neighbourCells)
    {
        int amountNeighbours = 0;
        Cell neighbour;

        for (int i = 0; i < cell.Neighbours.Length; i++)
        {            
            if (cell.Neighbours[i] >= 0)
            {
                neighbour = grid.GetCell(cell.Neighbours[i]);

                if (neighbour.OccTileIndex >= 0)
                {
                    neighbourCells.Add(neighbour);
                    amountNeighbours++;
                }
            }
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
            //Gizmos.color = Color.green;
            grid.DrawCell(debugCell, transform);
            //grid.DrawCellNeighbours(debugCell, transform);
        }
    }
}
