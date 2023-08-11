using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(menuName = "World/Tile")]
public class Tile : ScriptableObject
{
    public static readonly int CONNECTOR_COUNT = 4;

    public Transform Asset;

    /**
     * 0: north-west
     * 1: north-east
     * 2: south-east
     * 3: south-west
     */
    [Range(0, 1)] public int[] ConnectorFlags;

    [HideInInspector] public int TileIndex { get; set; }

    private int GetConnection(int con)
    {
        return ConnectorFlags[(2 + con) % CONNECTOR_COUNT];
    }

    public bool IsConnecting(Tile tile, int conSelf, int conOther)
    {
        int c0 = GetConnection(conSelf);
        int c1 = tile.GetConnection(conOther);
        return c0 == c1;
    }

    public IEnumerable<Tile> ConnectingTiles(IEnumerable<Tile> tileSet, int conSelf, int conOther)
    {
        return tileSet.Where(t => IsConnecting(t, conSelf, conOther));
    }
}
