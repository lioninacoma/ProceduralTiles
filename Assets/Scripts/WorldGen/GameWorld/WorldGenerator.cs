using System.Collections;
using System.Collections.Generic;
using TMPro;
using TreeEditor;
using UnityEngine;

public class WorldGenerator : MonoBehaviour
{
    [Range(1f, 1000f)] public float radius = 3f;
    [Range(2, 80)] public int div = 3;
    [Range(1, 100)] public int cellCountY = 16;
    [Range(0.1f, 4f)] public float cellHeightY = .5f;
    public int seed = 123;
    public Material surfaceMaterial;
    public Material buildingsMaterial;
    public Tile[] tiles;
    public TMP_Text Text;

    private GameGrid grid;
    private GameObject surface, buildings;
    private TileMesh[] tileMeshes;
    private Dictionary<int, TilePermutation> tilePermutations;
    private int placeHeight;

    void Awake()
    {
        grid = new GameGrid(radius, cellCountY, cellHeightY, div, seed);
        surface = CreateObject("Surface", surfaceMaterial);
        buildings = CreateObject("Buildings", buildingsMaterial);
        debugCell = -1;
        placeHeight = 0;
    }
    void OnDisable()
    {
        if (tileMeshes != null)
            for (int i = 0; i < tileMeshes.Length; i++)
                tileMeshes[i].Dispose();
    }

    private GameObject CreateObject(string name, Material material)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(transform);

        var meshFilter = obj.AddComponent<MeshFilter>();
        meshFilter.mesh = new Mesh();

        var meshRenderer = obj.AddComponent<MeshRenderer>();
        meshRenderer.material = material;

        return obj;
    }

    void Start()
    {
        tileMeshes = new TileMesh[tiles.Length]; 
        tilePermutations = new Dictionary<int, TilePermutation>();

        for (int i = 0; i < tileMeshes.Length; i++)
        {
            tiles[i].TileIndex = i;
            tileMeshes[i] = new TileMesh();
            tileMeshes[i].InitFromTile(tiles[i]);

            for (int y = 0; y < 4; y++)
            {
                for (int m = 0; m < 2; m++)
                {
                    int cubeIndex = tiles[i].GetCubeIndex(y, m);

                    if (!tilePermutations.ContainsKey(cubeIndex))
                    {
                        tilePermutations[cubeIndex] = new TilePermutation()
                        {
                            TileIndex = i,
                            YRotation = y * 90f,
                            YMirror = (m == 0) ? 1f : -1f
                        };
                    }
                }
            }
        }

        var mesh = surface.GetComponent<MeshFilter>().sharedMesh;
        grid.BuildMesh(mesh, 0);
        Text.text = "Place height at: " + placeHeight;
    }

    private int debugCell;

    void Update()
    {
        if (Input.GetAxis("Mouse ScrollWheel") > 0f)
        {
            placeHeight++;
            placeHeight = Mathf.Min(placeHeight, cellCountY);
            Text.text = "Place height at: " + placeHeight;
        }
        else if (Input.GetAxis("Mouse ScrollWheel") < 0f)
        {
            placeHeight--;
            placeHeight = Mathf.Max(placeHeight, 0);
            Text.text = "Place height at: " + placeHeight;
        }
        
        if (Input.GetMouseButtonUp(0))
        {
            var camera = Camera.main;
            var mouseRay = camera.ScreenPointToRay(Input.mousePosition);
            var cell = grid.RaycastCell(mouseRay, transform);

            if (cell >= 0)
            {
                grid.SetCellVolume(cell, placeHeight, -1f, 1);

                {
                    var mesh = surface.GetComponent<MeshFilter>().sharedMesh;
                    mesh.Clear();
                    grid.BuildMesh(mesh, 0);
                }

                {
                    var mesh = buildings.GetComponent<MeshFilter>().sharedMesh;
                    mesh.Clear();
                    grid.BuildObjectMesh(mesh, 1, tileMeshes, tilePermutations);
                }

                debugCell = cell;
            }
        }
    }

    void OnDrawGizmos()
    {
        if (grid != null)
        {
            grid.GetBaseGrid().DrawTriangles(transform);

            if (debugCell >= 0)
            {
                grid.DrawCell(debugCell, transform);
            }
        }
    }
}
