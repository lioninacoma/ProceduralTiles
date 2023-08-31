using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class IsoNode
{
    public enum IsoNodeType
    {
        NONE,
        INTERNAL,
        LEAF
    }

    public int Index;
    public int Parent;
    public int[] Children;
    public int3 Min;
    public int Size;
    public IsoDrawInfo DrawInfo;
    public IsoNodeType NodeType;

    public IsoNode()
    {
        Index = -1;
        Min = 0;
        Size = 0;
        Parent = -1;
        Children = new int[8] { -1, -1, -1, -1, -1, -1, -1, -1, };
        DrawInfo = null;
        NodeType = IsoNodeType.NONE;
    }
}
