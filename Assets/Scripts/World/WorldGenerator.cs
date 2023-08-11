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
    public int seed = 123;

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
        UnityEngine.Random.InitState(seed);
        grid = new Grid();
        grid.Build(radius, div, relaxIterations, relaxScale, relaxType, seed);
    }

    // Start is called before the first frame update
    void Start()
    {
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

    private Tile[] GetConnectingTiles(Cell cell)
    {
        Cell neighbour;
        Tile neighbourTile;

        int point, conNeighbour, n;
        IEnumerable<Tile> connectingTiles = tiles.ToList();

        for (int i = 0; i < cell.Points.Length; i++)
        {
            point = cell.Points[i];

            for (int j = 0; j < cell.NeighboursOfPoints[i].Length; j++)
            {
                n = cell.NeighboursOfPoints[i][j];
                neighbour = grid.GetCell(cell.Neighbours[n]);

                if (neighbour.OccTileIndex >= 0)
                {
                    neighbourTile = tiles[neighbour.OccTileIndex];
                    conNeighbour = neighbour.IndicesOfPoints[point];
                    connectingTiles = neighbourTile.ConnectingTiles(connectingTiles, conNeighbour, i);
                }
            }
        }

        return connectingTiles.ToArray();
    }

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
                var allowedTiles = GetConnectingTiles(cell);

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
