#define PI 3.14159265359
#define mod(x, y) (x - y * floor(x / y))

// noise functions sourced from 
// - https://github.com/ashima/webgl-noise
// - https://gist.github.com/patriciogonzalezvivo/670c22f3966e662d2f83
float4 taylorInvSqrt(float4 r) { return 1.79284291400159 - 0.85373472095314 * r; }
float3 fade(float3 t) { return t * t * t * (t * (t * 6. - 15.) + 10.); }
float4 permute(float4 x) { return mod(((x * 34.) + 1.) * x, 289.); }

// Hash Functions
#define UI0 1597334673U
#define UI1 3812015801U
#define UI2 uint2(UI0, UI1)
#define UI3 uint3(UI0, UI1, 2798796415U)
#define UIF (1.0 / float(0xffffffffU))

float3 hash33_old(float3 p)
{
	uint3 q = uint3(int3(p)) * UI3;
	q = (q.x ^ q.y ^ q.z)*UI3;
	return -1. + 2. * float3(q) * UIF;
}

float3 hash33(float3 p3)
{
	p3 = frac(p3 * float3(.1031, .1030, .0973));
    p3 += dot(p3, p3.yxz+33.33);
    return -1. + 2. * frac((p3.xxy + p3.yxx)*p3.zyx);
}

float3 hash33(float3 p, float freq)
{
	p = mod(p, freq);
	return hash33(p);
}

#define hash(p) hash33(p).x

// Gradient 3D Noise
// by Inigo Quilez
// https://iquilezles.org/articles/gradientnoise
#ifdef GRADIENT
float gnoise(float3 x)
{
    // grid
    float3 p = floor(x);
    float3 w = frac(x);
    
    // quintic interpolant
    float3 u = w*w*w*(w*(w*6.0-15.0)+10.0);

    // gradients
    float3 ga = hash33( p+float3(0.0,0.0,0.0) );
    float3 gb = hash33( p+float3(1.0,0.0,0.0) );
    float3 gc = hash33( p+float3(0.0,1.0,0.0) );
    float3 gd = hash33( p+float3(1.0,1.0,0.0) );
    float3 ge = hash33( p+float3(0.0,0.0,1.0) );
    float3 gf = hash33( p+float3(1.0,0.0,1.0) );
    float3 gg = hash33( p+float3(0.0,1.0,1.0) );
    float3 gh = hash33( p+float3(1.0,1.0,1.0) );
    
    // projections
    float va = dot( ga, w-float3(0.0,0.0,0.0) );
    float vb = dot( gb, w-float3(1.0,0.0,0.0) );
    float vc = dot( gc, w-float3(0.0,1.0,0.0) );
    float vd = dot( gd, w-float3(1.0,1.0,0.0) );
    float ve = dot( ge, w-float3(0.0,0.0,1.0) );
    float vf = dot( gf, w-float3(1.0,0.0,1.0) );
    float vg = dot( gg, w-float3(0.0,1.0,1.0) );
    float vh = dot( gh, w-float3(1.0,1.0,1.0) );
	
    // interpolation
    return va + 
           u.x*(vb-va) + 
           u.y*(vc-va) + 
           u.z*(ve-va) + 
           u.x*u.y*(va-vb-vc+vd) + 
           u.y*u.z*(va-vc-ve+vg) + 
           u.z*u.x*(va-vb-ve+vf) + 
           u.x*u.y*u.z*(-va+vb+vc-vd+ve-vf-vg+vh);
}

float4 gnoised(float3 x)
{
	//grid
    float3 p = floor(x);
    float3 w = frac(x);
    
    // quintic interpolant
    float3 u = w*w*w*(w*(w*6.0-15.0)+10.0);
    float3 du = 30.0*w*w*(w*(w-2.0)+1.0);

    // gradients
    float3 ga = hash33( p+float3(0.0,0.0,0.0) );
    float3 gb = hash33( p+float3(1.0,0.0,0.0) );
    float3 gc = hash33( p+float3(0.0,1.0,0.0) );
    float3 gd = hash33( p+float3(1.0,1.0,0.0) );
    float3 ge = hash33( p+float3(0.0,0.0,1.0) );
    float3 gf = hash33( p+float3(1.0,0.0,1.0) );
    float3 gg = hash33( p+float3(0.0,1.0,1.0) );
    float3 gh = hash33( p+float3(1.0,1.0,1.0) );
    
    // projections
    float va = dot( ga, w-float3(0.0,0.0,0.0) );
    float vb = dot( gb, w-float3(1.0,0.0,0.0) );
    float vc = dot( gc, w-float3(0.0,1.0,0.0) );
    float vd = dot( gd, w-float3(1.0,1.0,0.0) );
    float ve = dot( ge, w-float3(0.0,0.0,1.0) );
    float vf = dot( gf, w-float3(1.0,0.0,1.0) );
    float vg = dot( gg, w-float3(0.0,1.0,1.0) );
    float vh = dot( gh, w-float3(1.0,1.0,1.0) );
	
    // interpolation
    float v = va + 
              u.x*(vb-va) + 
              u.y*(vc-va) + 
              u.z*(ve-va) + 
              u.x*u.y*(va-vb-vc+vd) + 
              u.y*u.z*(va-vc-ve+vg) + 
              u.z*u.x*(va-vb-ve+vf) + 
              u.x*u.y*u.z*(-va+vb+vc-vd+ve-vf-vg+vh);
              
    float3 d = ga + 
             u.x*(gb-ga) + 
             u.y*(gc-ga) + 
             u.z*(ge-ga) + 
             u.x*u.y*(ga-gb-gc+gd) + 
             u.y*u.z*(ga-gc-ge+gg) + 
             u.z*u.x*(ga-gb-ge+gf) + 
             u.x*u.y*u.z*(-ga+gb+gc-gd+ge-gf-gg+gh) +   
             
             du * (float3(vb-va,vc-va,ve-va) + 
                   u.yzx*float3(va-vb-vc+vd,va-vc-ve+vg,va-vb-ve+vf) + 
                   u.zxy*float3(va-vb-ve+vf,va-vb-vc+vd,va-vc-ve+vg) + 
                   u.yzx*u.zxy*(-va+vb+vc-vd+ve-vf-vg+vh));
                   
    return float4( v, d );     
}

float gnoiset(float3 x, float freq)
{
    // grid
    float3 p = floor(x);
    float3 w = frac(x);
    
    // quintic interpolant
    float3 u = w*w*w*(w*(w*6.-15.)+10.);

    // gradients
    float3 ga = hash33(p + float3(0., 0., 0.), freq);
    float3 gb = hash33(p + float3(1., 0., 0.), freq);
    float3 gc = hash33(p + float3(0., 1., 0.), freq);
    float3 gd = hash33(p + float3(1., 1., 0.), freq);
    float3 ge = hash33(p + float3(0., 0., 1.), freq);
    float3 gf = hash33(p + float3(1., 0., 1.), freq);
    float3 gg = hash33(p + float3(0., 1., 1.), freq);
    float3 gh = hash33(p + float3(1., 1., 1.), freq);
    
    // projections
    float va = dot(ga, w - float3(0., 0., 0.));
    float vb = dot(gb, w - float3(1., 0., 0.));
    float vc = dot(gc, w - float3(0., 1., 0.));
    float vd = dot(gd, w - float3(1., 1., 0.));
    float ve = dot(ge, w - float3(0., 0., 1.));
    float vf = dot(gf, w - float3(1., 0., 1.));
    float vg = dot(gg, w - float3(0., 1., 1.));
    float vh = dot(gh, w - float3(1., 1., 1.));
	
    // interpolation
     return va + 
           u.x*(vb-va) + 
           u.y*(vc-va) + 
           u.z*(ve-va) + 
           u.x*u.y*(va-vb-vc+vd) + 
           u.y*u.z*(va-vc-ve+vg) + 
           u.z*u.x*(va-vb-ve+vf) + 
           u.x*u.y*u.z*(-va+vb+vc-vd+ve-vf-vg+vh);
}
#endif // GRADIENT

// Classic Perlin 3D Noise 
// by Stefan Gustavson
#ifdef PERLIN
float pnoise(float3 P) 
{
	float3 Pi0 = floor(P); // Integer part for indexing
	float3 Pi1 = Pi0 + float3(1.0, 1.0, 1.0); // Integer part + 1
	Pi0 = mod(Pi0, 289.0);
	Pi1 = mod(Pi1, 289.0);
	float3 Pf0 = frac(P); // Fractional part for interpolation
	float3 Pf1 = Pf0 - float3(1.0, 1.0, 1.0); // Fractional part - 1.0
	float4 ix = float4(Pi0.x, Pi1.x, Pi0.x, Pi1.x);
	float4 iy = float4(Pi0.yy, Pi1.yy);
	float4 iz0 = Pi0.zzzz;
	float4 iz1 = Pi1.zzzz;

	float4 ixy = permute(permute(ix) + iy);
	float4 ixy0 = permute(ixy + iz0);
	float4 ixy1 = permute(ixy + iz1);

	float4 gx0 = ixy0 / 7.0;
	float4 gy0 = frac(floor(gx0) / 7.0) - 0.5;
	gx0 = frac(gx0);
	float4 gz0 = float4(0.5, 0.5, 0.5, 0.5) - abs(gx0) - abs(gy0);
	float4 sz0 = step(gz0, float4(0.0, 0.0, 0.0, 0.0));
	gx0 -= sz0 * (step(0.0, gx0) - 0.5);
	gy0 -= sz0 * (step(0.0, gy0) - 0.5);

	float4 gx1 = ixy1 / 7.0;
	float4 gy1 = frac(floor(gx1) / 7.0) - 0.5;
	gx1 = frac(gx1);
	float4 gz1 = float4(0.5, 0.5, 0.5, 0.5) - abs(gx1) - abs(gy1);
	float4 sz1 = step(gz1, float4(0.0, 0.0, 0.0, 0.0));
	gx1 -= sz1 * (step(0.0, gx1) - 0.5);
	gy1 -= sz1 * (step(0.0, gy1) - 0.5);

	float3 g000 = float3(gx0.x, gy0.x, gz0.x);
	float3 g100 = float3(gx0.y, gy0.y, gz0.y);
	float3 g010 = float3(gx0.z, gy0.z, gz0.z);
	float3 g110 = float3(gx0.w, gy0.w, gz0.w);
	float3 g001 = float3(gx1.x, gy1.x, gz1.x);
	float3 g101 = float3(gx1.y, gy1.y, gz1.y);
	float3 g011 = float3(gx1.z, gy1.z, gz1.z);
	float3 g111 = float3(gx1.w, gy1.w, gz1.w);

	float4 norm0 = taylorInvSqrt(float4(dot(g000, g000), dot(g010, g010), dot(g100, g100), dot(g110, g110)));
	g000 *= norm0.x;
	g010 *= norm0.y;
	g100 *= norm0.z;
	g110 *= norm0.w;
	float4 norm1 = taylorInvSqrt(float4(dot(g001, g001), dot(g011, g011), dot(g101, g101), dot(g111, g111)));
	g001 *= norm1.x;
	g011 *= norm1.y;
	g101 *= norm1.z;
	g111 *= norm1.w;

	float n000 = dot(g000, Pf0);
	float n100 = dot(g100, float3(Pf1.x, Pf0.yz));
	float n010 = dot(g010, float3(Pf0.x, Pf1.y, Pf0.z));
	float n110 = dot(g110, float3(Pf1.xy, Pf0.z));
	float n001 = dot(g001, float3(Pf0.xy, Pf1.z));
	float n101 = dot(g101, float3(Pf1.x, Pf0.y, Pf1.z));
	float n011 = dot(g011, float3(Pf0.x, Pf1.yz));
	float n111 = dot(g111, Pf1);

	float3 fade_xyz = fade(Pf0);
	float4 n_z = lerp(float4(n000, n100, n010, n110), float4(n001, n101, n011, n111), fade_xyz.z);
	float2 n_yz = lerp(n_z.xy, n_z.zw, fade_xyz.y);
	float n_xyz = lerp(n_yz.x, n_yz.y, fade_xyz.x);
	return 2.2 * n_xyz;
}
#endif // PERLIN

// Simplex 3D Noise 
// by Ian McEwan, Ashima Arts
#ifdef SIMPLEX
float snoise(float3 v)
{ 
    const float2  C = float2(1.0/6.0, 1.0/3.0) ;
    const float4  D = float4(0.0, 0.5, 1.0, 2.0);

    // First corner
    float3 i  = floor(v + dot(v, C.yyy) );
    float3 x0 =   v - i + dot(i, C.xxx) ;

    // Other corners
    float3 g = step(x0.yzx, x0.xyz);
    float3 l = 1.0 - g;
    float3 i1 = min( g.xyz, l.zxy );
    float3 i2 = max( g.xyz, l.zxy );

    //  x0 = x0 - 0. + 0.0 * C 
    float3 x1 = x0 - i1 + 1.0 * C.xxx;
    float3 x2 = x0 - i2 + 2.0 * C.xxx;
    float3 x3 = x0 - 1. + 3.0 * C.xxx;

    // Permutations
    i = mod(i, 289.0 ); 
    float4 p = permute( permute( permute( 
                i.z + float4(0.0, i1.z, i2.z, 1.0 ))
            + i.y + float4(0.0, i1.y, i2.y, 1.0 )) 
            + i.x + float4(0.0, i1.x, i2.x, 1.0 ));

    // Gradients
    // ( N*N points uniformly over a square, mapped onto an octahedron.)
    float n_ = 1.0/7.0; // N=7
    float3  ns = n_ * D.wyz - D.xzx;

    float4 j = p - 49.0 * floor(p * ns.z *ns.z);  //  mod(p,N*N)

    float4 x_ = floor(j * ns.z);
    float4 y_ = floor(j - 7.0 * x_ );    // mod(j,N)

    float4 x = x_ *ns.x + ns.yyyy;
    float4 y = y_ *ns.x + ns.yyyy;
    float4 h = 1.0 - abs(x) - abs(y);

    float4 b0 = float4( x.xy, y.xy );
    float4 b1 = float4( x.zw, y.zw );

    float4 s0 = floor(b0)*2.0 + 1.0;
    float4 s1 = floor(b1)*2.0 + 1.0;
    float4 sh = -step(h, float4(0.0, 0.0, 0.0, 0.0));

    float4 a0 = b0.xzyw + s0.xzyw*sh.xxyy ;
    float4 a1 = b1.xzyw + s1.xzyw*sh.zzww ;

    float3 p0 = float3(a0.xy,h.x);
    float3 p1 = float3(a0.zw,h.y);
    float3 p2 = float3(a1.xy,h.z);
    float3 p3 = float3(a1.zw,h.w);

    //Normalise gradients
    float4 norm = taylorInvSqrt(float4(dot(p0,p0), dot(p1,p1), dot(p2, p2), dot(p3,p3)));
    p0 *= norm.x;
    p1 *= norm.y;
    p2 *= norm.z;
    p3 *= norm.w;

    // Mix final noise value
    float4 m = max(0.6 - float4(dot(x0,x0), dot(x1,x1), dot(x2,x2), dot(x3,x3)), 0.0);
    m = m * m;
    return 42.0 * dot( m*m, float4( dot(p0,x0), dot(p1,x1), 
                                dot(p2,x2), dot(p3,x3) ) );
}
#endif // SIMPLEX

// Fractional Brownian Motion
// depends on custom basis function
// sourced from https://www.shadertoy.com/view/ldyXRw
#define DECL_FBM_FUNC(_name, _octaves, _basis) float _name(float3 pos, float lacunarity, float init_gain, float gain) { float3 p = pos; float H = init_gain; float t = 0.f; for (int i = 0; i < _octaves; i++) { t += _basis * H; p *= lacunarity; H *= gain; } return t; }
#define DECL_FBM_TEX_FUNC(_name, _octaves, _basis) float _name(float3 pos, Texture3D tex, float lacunarity, float init_gain, float gain) { float3 p = pos; Texture3D tx = tex; float H = init_gain; float t = 0.f; for (int i = 0; i < _octaves; i++) { t += _basis * H; p *= lacunarity; H *= gain; } return t; }

// Voronoi Noise
// by Ronja B?hringer
// https://www.ronja-tutorials.com/post/028-voronoi-noise/
#ifdef VORONOI

// random functions
// by Ronja B?hringer
// https://github.com/ronja-tutorials/ShaderTutorials/blob/master/Assets/028_Voronoi_Noise/Random.cginc

// to 2d functions
//get a scalar random value from a 3d value
float rand3dTo1d(float3 value, float3 dotDir = float3(12.9898, 78.233, 37.719)){
	//make value smaller to avoid artefacts
	float3 smallValue = cos(value);
	//get scalar value from 3d vector
	float random = dot(smallValue, dotDir);
	//make value more random by making it bigger and then taking the factional part
	random = frac(sin(random) * 143758.5453);
	return random;
}

// to 3d functions
float3 rand3dTo3d(float3 value){
	return float3(
		rand3dTo1d(value, float3(12.989, 78.233, 37.719)),
		rand3dTo1d(value, float3(39.346, 11.135, 83.155)),
		rand3dTo1d(value, float3(73.156, 52.235, 09.151))
	);
}

float3 voronoi(float3 value){
	float3 baseCell = floor(value);

	//first pass to find the closest cell
	float minDistToCell = 10;
	float3 toClosestCell;
	float3 closestCell;
	//[unroll]
	for(int x1=-1; x1<=1; x1++){
		//[unroll]
		for(int y1=-1; y1<=1; y1++){
			//[unroll]
			for(int z1=-1; z1<=1; z1++){
				float3 cell = baseCell + float3(x1, y1, z1);
				float3 cellPosition = cell + rand3dTo3d(cell);
				float3 toCell = cellPosition - value;
				float distToCell = length(toCell);
				if(distToCell < minDistToCell){
					minDistToCell = distToCell;
					closestCell = cell;
					toClosestCell = toCell;
				}
			}
		}
	}

	//second pass to find the distance to the closest edge
	float minEdgeDistance = 10;
	//[unroll]
	for(int x2=-1; x2<=1; x2++){
		//[unroll]
		for(int y2=-1; y2<=1; y2++){
			//[unroll]
			for(int z2=-1; z2<=1; z2++){
				float3 cell = baseCell + float3(x2, y2, z2);
				float3 cellPosition = cell + rand3dTo3d(cell);
				float3 toCell = cellPosition - value;

				float3 diffToClosestCell = abs(closestCell - cell);
				bool isClosestCell = diffToClosestCell.x + diffToClosestCell.y + diffToClosestCell.z < 0.1;
				if(!isClosestCell){
					float3 toCenter = (toClosestCell + toCell) * 0.5;
					float3 cellDifference = normalize(toCell - toClosestCell);
					float edgeDistance = dot(toCenter, cellDifference);
					minEdgeDistance = min(minEdgeDistance, edgeDistance);
				}
			}
		}
	}

	float random = rand3dTo1d(closestCell);
    return float3(minDistToCell, random, minEdgeDistance);
}
#endif // VORONOI

// Noise sampling functions from example code of GPU Gems 3 Chapter 1
// by Ryan Geiss 
// https://developer.nvidia.com/gpugems/gpugems3/part-i-geometry/chapter-1-generating-complex-procedural-terrains-using-gpu
#ifdef SAMPLE_NOISE

SamplerState sampler_trilinear_repeat;
SamplerState sampler_point_repeat;

#define NOISE_LATTICE_SIZE 16
#define INV_LATTICE_SIZE (1.0/(float)(NOISE_LATTICE_SIZE))

float4 NLQu(float3 uvw, Texture3D tex) {
	return tex.SampleLevel(sampler_trilinear_repeat, uvw, 0);
}
float4 NLQs(float3 uvw, Texture3D tex) {
	return NLQu(uvw, tex)*2-1;
}

float4 NMQu(float3 uvw, Texture3D tex) {
	// smooth the input coord
	float3 t = frac(uvw * NOISE_LATTICE_SIZE + 0.5);
	float3 t2 = (3 - 2*t)*t*t;
	float3 uvw2 = uvw + (t2-t)/(float)(NOISE_LATTICE_SIZE);
	// fetch
	return NLQu(uvw2, tex);
}

float4 NMQs(float3 uvw, Texture3D tex) {
	// smooth the input coord
	float3 t = frac(uvw * NOISE_LATTICE_SIZE + 0.5);
	float3 t2 = (3 - 2*t)*t*t;
	float3 uvw2 = uvw + (t2-t)/(float)(NOISE_LATTICE_SIZE);
	// fetch  
	return NLQs(uvw2, tex);
}

// SUPER MEGA HIGH QUALITY noise sampling (signed)
float NHQu(float3 uvw, Texture3D tex, float smooth = 1) 
{
	float3 uvw2 = floor(uvw * NOISE_LATTICE_SIZE) * INV_LATTICE_SIZE;
	float3 t    = (uvw - uvw2) * NOISE_LATTICE_SIZE;
	t = lerp(t, t*t*(3 - 2*t), smooth);
	
	float2 d = float2( INV_LATTICE_SIZE, 0 );

#if 0
	// the 8-lookup version... (SLOW)
	float4 f1 = float4( tex.SampleLevel(sampler_point_repeat, uvw2 + d.xxx, 0).x, 
						tex.SampleLevel(sampler_point_repeat, uvw2 + d.yxx, 0).x, 
						tex.SampleLevel(sampler_point_repeat, uvw2 + d.xyx, 0).x, 
						tex.SampleLevel(sampler_point_repeat, uvw2 + d.yyx, 0).x );
	float4 f2 = float4( tex.SampleLevel(sampler_point_repeat, uvw2 + d.xxy, 0).x, 
						tex.SampleLevel(sampler_point_repeat, uvw2 + d.yxy, 0).x, 
						tex.SampleLevel(sampler_point_repeat, uvw2 + d.xyy, 0).x, 
						tex.SampleLevel(sampler_point_repeat, uvw2 + d.yyy, 0).x );
	float4 f3 = lerp(f2, f1, t.zzzz);
	float2 f4 = lerp(f3.zw, f3.xy, t.yy);
	float  f5 = lerp(f4.y, f4.x, t.x);
#else
	// THE TWO-SAMPLE VERSION: much faster!
	// note: requires that three xy-neighbor texels' original .x values
	//       are packed into .xyz values of each texel.
	float4 f1 = tex.SampleLevel(sampler_point_repeat, uvw2 + d.yyx, 0); // <+xyz, +yz, +xz, +z>
	float4 f2 = tex.SampleLevel(sampler_point_repeat, uvw2		  , 0); // <+xy, +y, +x, +0>
	float4 f3 = lerp(f2, f1, t.zzzz);
	float2 f4 = lerp(f3.zw, f3.xy, t.yy);
	float  f5 = lerp(f4.y, f4.x, t.x);
#endif
	
	return f5;
}

float NHQs(float3 uvw, Texture3D tex, float smooth = 1) {
	return NHQu(uvw, tex, smooth)*2-1;
}
#endif // SAMPLE_NOISE