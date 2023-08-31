using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class Iso
{
    public static readonly int MATERIAL_AIR = 0;
    public static readonly int MATERIAL_SOLID = 1;

    // ----------------------------------------------------------------------------

    public static readonly int3[] CHILD_MIN_OFFSETS =
    {
        new int3(0, 0, 0),
        new int3(1, 0, 0),
        new int3(0, 1, 0),
        new int3(1, 1, 0),
        new int3(0, 0, 1),
        new int3(1, 0, 1),
        new int3(0, 1, 1),
        new int3(1, 1, 1),
    };

    public static readonly int3[] EDGE_OFFSETS =
    {
	    // coordinates to centers of edges
	    new int3(1, 2, 0), new int3(1, 0, 2),
        new int3(2, 1, 0), new int3(0, 1, 2),
        new int3(2, 0, 1), new int3(0, 2, 1),
        new int3(1, 0, 0), new int3(0, 1, 0), new int3(0, 0, 1),
        new int3(1, 2, 2), new int3(2, 2, 1), new int3(2, 1, 2),
	    // coordinates to centers of faces
	    new int3(0, 1, 1), new int3(2, 1, 1),
        new int3(1, 0, 1), new int3(1, 2, 1),
        new int3(1, 1, 0), new int3(1, 1, 2),
    };

    // ----------------------------------------------------------------------------
    // data from the original DC impl, drives the contouring process

    public static readonly int[][] EDGE_V_MAP =
    {
        new int[] { 0, 4 }, new int[] { 1, 5 }, new int[] { 2, 6 }, new int[] { 3, 7 },	// x-axis 
	    new int[] { 0, 2 }, new int[] { 1, 3 }, new int[] { 4, 6 }, new int[] { 5, 7 },	// y-axis
	    new int[] { 0, 1 }, new int[] { 2, 3 }, new int[] { 4, 5 }, new int[] { 6, 7 }	// z-axis
    };

    public static readonly int[] EDGE_MASK = { 5, 3, 6 };

    public static readonly int[][] FACE_MAP = {
        new int[] { 4, 8, 5,  9 }, new int[] { 6, 10, 7, 11 }, new int[] { 0, 8, 1, 10 },
        new int[] { 2, 9, 3, 11 }, new int[] { 0,  4, 2,  6 }, new int[] { 1, 5, 3,  7 }
    };

    public static readonly int[][] CELL_PROC_FACE_MASK = {
        new int[] { 0, 4, 0 }, new int[] { 1, 5, 0 }, new int[] { 2, 6, 0 }, new int[] { 3, 7, 0 },
        new int[] { 0, 2, 1 }, new int[] { 4, 6, 1 }, new int[] { 1, 3, 1 }, new int[] { 5, 7, 1 },
        new int[] { 0, 1, 2 }, new int[] { 2, 3, 2 }, new int[] { 4, 5, 2 }, new int[] { 6, 7, 2 }
    };

    public static readonly int[][] CELL_PROC_EDGE_MASK = {
        new int[] { 0, 1, 2, 3, 0 }, new int[] { 4, 5, 6, 7, 0 }, new int[] { 0, 4, 1, 5, 1 },
        new int[] { 2, 6, 3, 7, 1 }, new int[] { 0, 2, 4, 6, 2 }, new int[] { 1, 3, 5, 7, 2 }
    };

    public static readonly int[][][] FACE_PROC_FACE_MASK = {
        new int[][] { new int[] { 4, 0, 0 }, new int[] { 5, 1, 0 }, new int[] { 6, 2, 0 }, new int[] { 7, 3, 0 } },
        new int[][] { new int[] { 2, 0, 1 }, new int[] { 6, 4, 1 }, new int[] { 3, 1, 1 }, new int[] { 7, 5, 1 } },
        new int[][] { new int[] { 1, 0, 2 }, new int[] { 3, 2, 2 }, new int[] { 5, 4, 2 }, new int[] { 7, 6, 2 } }
    };

    public static readonly int[][][] FACE_PROC_EDGE_MASK = {
        new int[][] { new int[] { 1, 4, 0, 5, 1, 1 }, new int[] { 1, 6, 2, 7, 3, 1 }, new int[] { 0, 4, 6, 0, 2, 2 }, new int[] { 0, 5, 7, 1, 3, 2 } },
        new int[][] { new int[] { 0, 2, 3, 0, 1, 0 }, new int[] { 0, 6, 7, 4, 5, 0 }, new int[] { 1, 2, 0, 6, 4, 2 }, new int[] { 1, 3, 1, 7, 5, 2 } },
        new int[][] { new int[] { 1, 1, 0, 3, 2, 0 }, new int[] { 1, 5, 4, 7, 6, 0 }, new int[] { 0, 1, 5, 0, 4, 1 }, new int[] { 0, 3, 7, 2, 6, 1 } }
    };

    public static readonly int[][][] EDGE_PROC_EDGE_MASK = {
        new int[][] { new int[] { 3, 2, 1, 0, 0 }, new int[] { 7, 6, 5, 4, 0 } },
        new int[][] { new int[] { 5, 1, 4, 0, 1 }, new int[] { 7, 3, 6, 2, 1 } },
        new int[][] { new int[] { 6, 4, 2, 0, 2 }, new int[] { 7, 5, 3, 1, 2 } },
    };

    public static readonly int[][] PROCESS_EDGE_MASK = {
        new int[] { 3, 2, 1, 0 }, new int[] { 7, 5, 6, 4 }, new int[] { 11, 10, 9, 8 }
    };
}
