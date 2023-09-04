#ifndef __MESH_STRUCTS__
#define __MESH_STRUCTS__

struct ActiveCell
{
	uint mask;
	uint cellId;
    uint chunkId;
};

struct Counts
{
	uint vertexCount;
	uint indexCount;
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