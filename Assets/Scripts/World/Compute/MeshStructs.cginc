#ifndef __MESH_STRUCTS__
#define __MESH_STRUCTS__

struct Vertex
{
	float3 position;
};

struct ActiveCell
{
	uint mask;
	uint cellId;
};

struct Counts
{
	uint vertexCount;
	uint indexCount;
};

#endif