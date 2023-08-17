using System.Collections;
using System.Collections.Generic;
using TreeEditor;
using UnityEngine;

public class WorldGenerator : MonoBehaviour
{
    [Range(1f, 1000f)] public float radius = 3f;
    [Range(2, 80)] public int div = 3;
    public int seed = 123;
    public Material surfaceMaterial;

    private GameGrid grid;

    void Awake()
    {
        grid = new GameGrid(radius, div, seed);
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
        var surfaceObj = CreateSurface();
        surfaceObj.name = "Surface";
        var mesh = surfaceObj.GetComponent<MeshFilter>().sharedMesh;
        grid.BuildMesh(mesh);
    }

    // Update is called once per frame
    void Update()
    {

    }

    void OnDrawGizmos()
    {
        if (grid != null)
        {
            grid.GetBaseGrid().DrawTriangles(transform);
        }
    }
}
