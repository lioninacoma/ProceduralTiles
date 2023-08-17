using System.Collections;
using System.Collections.Generic;
using TMPro;
using TreeEditor;
using UnityEngine;

public class WorldGenerator : MonoBehaviour
{
    [Range(1f, 1000f)] public float radius = 3f;
    [Range(2, 80)] public int div = 3;
    [Range(1, 100)] public int height = 16;
    public int seed = 123;
    public Material surfaceMaterial; 
    public TMP_Text Text;

    private GameGrid grid;
    private GameObject surface;
    private int placeHeight;

    void Awake()
    {
        grid = new GameGrid(radius, height, div, seed);
        surface = CreateSurface();
        surface.name = "Surface";
        debugCell = -1;
        placeHeight = 0;
    }

    private GameObject CreateSurface()
    {
        var surface = new GameObject(name);
        surface.transform.SetParent(transform);

        var meshFilter = surface.AddComponent<MeshFilter>();
        meshFilter.mesh = new Mesh();

        var meshRenderer = surface.AddComponent<MeshRenderer>();
        meshRenderer.material = surfaceMaterial;

        return surface;
    }

    // Start is called before the first frame update
    void Start()
    {
        var mesh = surface.GetComponent<MeshFilter>().sharedMesh;
        grid.BuildMesh(mesh);
        Text.text = "Place height at: " + placeHeight;
    }

    private int debugCell;

    // Update is called once per frame
    void Update()
    {
        if (Input.GetAxis("Mouse ScrollWheel") > 0f)
        {
            placeHeight++;
            placeHeight = Mathf.Min(placeHeight, height);
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
                grid.SetCellVolume(cell, placeHeight, -1f);
                var mesh = surface.GetComponent<MeshFilter>().sharedMesh;
                mesh.Clear();
                grid.BuildMesh(mesh);
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
