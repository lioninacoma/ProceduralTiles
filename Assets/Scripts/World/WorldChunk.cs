using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WorldChunk : MonoBehaviour
{
    private static readonly int CELL_XY_BUFFER_SIZE = 32000;
    private static readonly int HALFEDGES_BUFFER_SIZE = CELL_XY_BUFFER_SIZE * 6; // edge cell has 2 triangles with each 3 halfedges
    private static readonly int GRID_RELAX_ITERATIONS = 20;
    private static readonly float GRID_RELAX_SCALE = .22f;

    [Range(1f, 1000f)] public float GridRadius = 32f;
    [Range(2, 80)] public int GridCellDiv = 24;
    [Range(0f, 1f)] public float GridCellHeight = 1f;
    public int ChunkSeed = 123;

    private float FlattenHeight;
    private ChunkSurface Surface;
    private IrregularGrid Grid;
    private Pathfinder Pathfinder;

    private void Awake()
    {
        var surfaceObj = CreateChildObject("Surface");
        Surface = surfaceObj.AddComponent<ChunkSurface>();
        Grid = new IrregularGrid(HALFEDGES_BUFFER_SIZE);
        Grid.Build(GridRadius, GridCellDiv, GRID_RELAX_ITERATIONS, GRID_RELAX_SCALE, 0, ChunkSeed);
        FlattenHeight = 0f;
        Pathfinder = new Pathfinder(100000);
    }

    private void Start()
    {
        Surface.Build(Grid, GridCellHeight);
        Pathfinder.UpdateGrid(Surface.GetComponent<MeshFilter>().sharedMesh);
        StartIndex = -1;
        GoalIndex = StartIndex;
    }

    void Update()
    {
        if (Input.GetAxis("Mouse ScrollWheel") > 0f)
        {
            FlattenHeight +=.5f;
            FlattenHeight = Mathf.Min(FlattenHeight, 20f);
            Debug.Log(FlattenHeight);
        }
        else if (Input.GetAxis("Mouse ScrollWheel") < 0f)
        {
            FlattenHeight -= .5f;
            FlattenHeight = Mathf.Max(FlattenHeight, 0f);
            Debug.Log(FlattenHeight);
        }

        if (Input.GetMouseButtonUp(0))
        {
            var camera = Camera.main;
            var mouseRay = camera.ScreenPointToRay(Input.mousePosition);

            var stopwatchTotal = new System.Diagnostics.Stopwatch();
            stopwatchTotal.Start();

            {
                var stopwatchMesh = new System.Diagnostics.Stopwatch();
                stopwatchMesh.Start();

                Surface.Flatten(mouseRay, FlattenHeight);
                Surface.UpdateMesh();

                stopwatchMesh.Stop();
                var elapsedTimeMesh = stopwatchMesh.ElapsedMilliseconds;
                Debug.Log("Update mesh time: " + elapsedTimeMesh + "ms");
            }

            {
                var stopwatchGrid = new System.Diagnostics.Stopwatch();
                stopwatchGrid.Start();

                Pathfinder.Clear();
                Pathfinder.UpdateGrid(Surface.GetComponent<MeshFilter>().sharedMesh);

                stopwatchGrid.Stop();
                var elapsedTimeGrid = stopwatchGrid.ElapsedMilliseconds;
                Debug.Log("Update grid time: " + elapsedTimeGrid + "ms");
            }

            stopwatchTotal.Stop();
            var elapsedTimeTotal = stopwatchTotal.ElapsedMilliseconds;
            Debug.Log("Total time: " + elapsedTimeTotal + "ms");
        }

        if (Input.GetMouseButtonUp(1))
        {
            var camera = Camera.main;
            var mouseRay = camera.ScreenPointToRay(Input.mousePosition);
            var collider = Surface.GetComponent<MeshCollider>();

            if (collider.Raycast(mouseRay, out RaycastHit hitInfo, 10000))
            {
                if (StartIndex < 0) StartIndex = hitInfo.triangleIndex;
                else if (GoalIndex < 0) GoalIndex = hitInfo.triangleIndex;

                if (StartIndex >= 0 && GoalIndex >= 0)
                {
                    DebugPath = Pathfinder.FindPath(StartIndex, GoalIndex);
                    StartIndex = GoalIndex;
                    GoalIndex = -1;
                }
            }
        }
    }

    private int StartIndex, GoalIndex;
    private List<int> DebugPath;

    private void OnDrawGizmos()
    {
        if (Grid != null)
        {
            Gizmos.color = Color.black;
            Grid.DrawTriangles(transform);
        }

        if (Pathfinder != null)
        {
            //Gizmos.color = Color.gray;
            //Pathfinder.DrawGrid();

            if (DebugPath != null && DebugPath.Count > 0)
            {
                Gizmos.color = Color.red;
                Pathfinder.DrawPath(DebugPath);
            }
        }
    }

    private GameObject CreateChildObject(string name)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(transform);
        return obj;
    }
}
