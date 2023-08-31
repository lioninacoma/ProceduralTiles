using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class IsoDrawInfo
{
    public int Index;
    public int Corners;
    public float3 Position;
    public float3 Normal;

    public IsoDrawInfo()
    {
        Index = -1;
        Corners = 0;
        Position = float3.zero;
        Normal = float3.zero;
    }
}
