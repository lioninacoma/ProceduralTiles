using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[CreateAssetMenu(menuName = "World/Tile")]
public class Tile : ScriptableObject
{
    public static readonly int CONNECTOR_COUNT = 4;

    public Transform Asset;

    /* 0: north-west
     * 1: north-east
     * 2: south-east
     * 3: south-west
     */
    [Range(0, 1)] public int[] ConnectorFlags;

    [Range(1, 10)] public float SpawnChance;

    [HideInInspector] public float SpawnProbability;
    [HideInInspector] public int TileIndex { get; set; }

    public int GetConnection(int con, int rot)
    {
        // Shifted by 2. Connector indices (determined by point index)
        // start at south-eastern connector. Connector flags start
        // at north-western cube corner.
        return ConnectorFlags[(((2 + con) - rot) + CONNECTOR_COUNT) % CONNECTOR_COUNT];
    }
}
