#ifndef __MESH_STRUCTS__
#define __MESH_STRUCTS__

struct Vertex
{
	float3 position;
	uint cellId;
};

struct Args
{
    uint indexCountPerInstance;
    uint instanceCount;
    uint startIndexLocation;
    uint baseVertexLocation;
    uint startInstanceLocation;
};
#endif