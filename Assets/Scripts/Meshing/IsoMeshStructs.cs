using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

public class IsoMeshStructs
{
    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    public struct VolumeData
    {
        public float Density;
        public float3 Normal;

        public VolumeData(float density, float3 normal)
        {
            Density = density;
            Normal = normal;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    public struct Counts
    {
        public int VertexCount;
        public int IndexCount;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    public struct Vertex
    {
        public float3 Position;
        public float3 Normal;
        public float2 GridInfo;

        public Vertex(float3 position, float3 normal, float2 gridInfo)
        {
            Position = position;
            Normal = normal;
            GridInfo = gridInfo;
        }
    }
}
