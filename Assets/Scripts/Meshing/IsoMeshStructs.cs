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
    public struct ActiveCell
    {
        public int Mask;
        public int CellId;
        public int ChunkId;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    public struct Vertex
    {
        public float3 Position;
        public float3 Normal;

        public Vertex(float3 position, float3 normal)
        {
            Position = position;
            Normal = normal;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 0)]
    public struct Args
    {
        public uint indexCountPerInstance;
        public uint instanceCount;
        public uint startIndexLocation;
        public uint baseVertexLocation;
        public uint startInstanceLocation;
    }

}
