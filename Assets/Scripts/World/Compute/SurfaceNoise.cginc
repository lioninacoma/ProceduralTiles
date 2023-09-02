#ifndef __SURFACE_NOISE__
#define __SURFACE_NOISE__

#define PERLIN
#include "Noise.cginc"

#define noise(p) pnoise(p)
DECL_FBM_FUNC(FBMSurface, 4, noise(p))

#define M 0.000012

float SurfaceSDF(float3 position)
{
	float3 x = position;
	
	float3 x0 = x * 0.5 + 0.5;
	//float3 x1 = x + float3(31, 71, 111);
	//float3 x2 = x + float3(11, 131, 51);
	//float3 x3 = x + float3(1, 71, 171);
	//float3 x4 = x + float3(131, 1, 31);
	
	float d = FBMSurface(x0 * 0.1, 2.0, 0.5, 0.5) * 100.0;
	return d;
}

#define SurfaceNoise(p) SurfaceSDF(p)

#endif