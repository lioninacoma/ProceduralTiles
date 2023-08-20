using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "World/Tile")]
public class Tile : ScriptableObject
{
    public Transform Asset;

    [Range(0, 1)] public int[] ActiveCorners = new int[8];

    [HideInInspector] public int TileIndex { get; set; }

    private int GetCorner(int i, int yRot, int yMir)
    {
        int d = (i / 4) * 4;
        int m = ((i - yRot) + 4) % 4;
        int index = ((d + m) + (yMir * 4)) % 8;
        return ActiveCorners[index];
    }

    public int GetCubeIndex(int yRot, int yMir)
    {
        int[] cornerIndices = new int[]
        {
            1, 0, 4, 5,
            2, 3, 7, 6
        };

        int i, c;
        int cubeIndex = 0;

        for (i = 0; i < 8; i++)
        {
            c = GetCorner(cornerIndices[i], yRot, yMir);
            cubeIndex |= (c > 0) ? (1 << i) : 0;
        }

        return cubeIndex;
    }
}
