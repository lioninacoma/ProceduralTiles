using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(menuName = "World/Tile")]
public class Tile : ScriptableObject
{
    public static readonly int TILE_DIRS = 4;
    public static readonly int TILE_DIRS_HALF = TILE_DIRS / 2;

    public Transform Asset;

    /**
     * 0: north-west
     * 1: north-east
     * 2: south-east
     * 3: south-west
     */
    [Range(0, 1)] public int[] ConnectorFlags;

    [HideInInspector] public int TileIndex { get; set; }

    private int GetConnectionByDir(int dir, int shifted)
    {
        int d = ((dir - shifted) + TILE_DIRS) % TILE_DIRS;
        int connection = 0;
        switch (d) 
        {
            // south
            case 0:
                connection |= ConnectorFlags[(3 + shifted) % 4] << 0; // west
                connection |= ConnectorFlags[(2 + shifted) % 4] << 1; // east
                break;
            // west
            case 1:
                connection |= ConnectorFlags[(0 + shifted) % 4] << 0; // north
                connection |= ConnectorFlags[(3 + shifted) % 4] << 1; // south
                break;
            // north
            case 2:
                connection |= ConnectorFlags[(0 + shifted) % 4] << 0; // west
                connection |= ConnectorFlags[(1 + shifted) % 4] << 1; // east
                break;
            // east
            case 3:
                connection |= ConnectorFlags[(1 + shifted) % 4] << 0; // north
                connection |= ConnectorFlags[(2 + shifted) % 4] << 1; // south
                break; 
        }
        return connection;
    }

    public bool IsConnecting(Tile tile, int dirSelfToOther, int dirOtherToSelf)
    {
        int shift = (((TILE_DIRS_HALF + dirOtherToSelf) - dirSelfToOther) + TILE_DIRS) % TILE_DIRS;
        var f0 = GetConnectionByDir(dirSelfToOther, 0);
        var f1 = tile.GetConnectionByDir(dirOtherToSelf, shift);
        return f0 == f1;
    }

    public IEnumerable<Tile> ConnectingTiles(IEnumerable<Tile> tileSet, int dirSelfToOther, int dirOtherToSelf)
    {
        return tileSet.Where(t => IsConnecting(t, dirSelfToOther, dirOtherToSelf));
    }
}
