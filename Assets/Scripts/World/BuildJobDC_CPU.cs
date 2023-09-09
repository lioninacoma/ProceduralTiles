//#define USE_DUAL_CONTOURING
//#define USE_DC_GRADIENT_DESCENT

using System.Collections;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Burst;

namespace ChunkBuilder
{
    [BurstCompile(FloatPrecision.High, FloatMode.Default)]
    public struct BuildJobDC_CPU : IJob
    {
        private static readonly float SDF_BIAS = 0.00001f;
        private static readonly int GRADIENT_DESCENT_ITERATIONS = 6;

        [WriteOnly] public NativeArray<float3> VertexBuffer;
        [WriteOnly] public NativeArray<int> IndexBuffer;

        public NativeArray<int> IndexCache;
        public NativeArray<int> MeshCounts;

        [ReadOnly] public int BufferSize;
        [ReadOnly] public int DataSize;

        [ReadOnly] public int3 ChunkMin;
        [ReadOnly] public int ChunkSize;
        [ReadOnly] public int CellSize;
        [ReadOnly] public int ChunkIndex;

        public void Execute()
        {
            Triangulate();
        }

        private static float SdSurface(float3 x)
        {
            return x.y - (Noise.FBM_4(new float3(x.x, 0, x.z) * 0.004f) * 120.0f + 20.0f);
        }

        private static float SdRepLimModel(float3 p, float s, float3 lima, float3 limb)
        {
            float d = 1e20f;
            float3 id = math.round(p / s);
            float3 o = math.sign(p - s * id);
            for (int k = 0; k < 2; k++)
                for (int j = 0; j < 2; j++)
                    for (int i = 0; i < 2; i++)
                    {
                        float3 rid = id + new float3(i, j, k) * o;
                        rid = math.clamp(rid, lima, limb);
                        float3 r = p - s * rid;
                        d = math.min(d, SdModel(r));
                    }
            return d;
        }

        private static float SdRepLimModel(float3 p, float s, float2 lima, float2 limb)
        {
            float d = 1e20f;
            float3 id = math.round(p / s);
            float3 o = math.sign(p - s * id);
            var lima3 = new float3(lima, 0);
            var limb3 = new float3(limb, 0);
            for (int j = 0; j < 2; j++)
                for (int i = 0; i < 2; i++)
                {
                    float3 rid = id + new float3(i, j, 0) * o;
                    rid = math.clamp(rid, lima3.xzy, limb3.xzy);
                    float3 r = p - s * rid;
                    d = math.min(d, SdModel(r));
                }
            return d;
        }

        private static float SdRepLimModel(float3 p, float s, float3 lima, float3 limb, bool symmetric = false)
        {
            if (!symmetric)
            {
                return SdRepLimModel(p, s, lima, limb); 
            }
            else
            {
                return SdModel(Csg.OpRepLim(p, s, lima, limb));
            }
        }

        private static float SdModel1(float3 p)
        {
            float d = 4f;
            float3 ra = new float3(d * 2f, d, d * 2f);
            float3 dy = new float3(0, d, 0);
            float3 rb = d;
            float a = Csg.SdBox(p + dy, ra + .01f);
            float b = Csg.SdBox(p - dy, rb + .01f);
            return Csg.OpUnion(a, b);
        }

        private static readonly float MODEL_RADIUS = 8f;
        private static readonly float MODEL_RADIUS_BIAS = .01f;

        private static float SdModel(float3 p)
        {
            float r = MODEL_RADIUS;
            return Csg.SdBox(p, r + MODEL_RADIUS_BIAS);
        }

        private static float Map1(float3 x)
        {
            float d;

            // terrain sdf
            float terrain = SdSurface(x);

            // model sdf
            float s = MODEL_RADIUS * 2;
            float3 i2 = math.round(new float3(x.x, 0, x.z) / s);
            float y = (math.ceil(SdSurface(i2 * s) / s) * s);
            float3 p = new float3(x.x, x.y + y, x.z) - s;
            float3 lima = 0f;
            float3 limb = new float3(1000f, 0f, 1000f);
            float model = SdRepLimModel(p, s, lima, limb, true);

            // combine sdfs
            d = Csg.OpUnion(model, terrain);
            return d + SDF_BIAS;
        }

        private static float Map(float3 x)
        {
            float d;
            float terrain = SdSurface(x);
            d = terrain;
            return d + SDF_BIAS;
        }

        private float3 CalcNormal(float3 x)
        {
            const float eps = 0.001f;
            float2 h = new float2(eps, 0);
            return math.normalize(new float3(Map(x + h.xyy) - Map(x - h.xyy),
                                             Map(x + h.yxy) - Map(x - h.yxy),
                                             Map(x + h.yyx) - Map(x - h.yyx)));
        }
        
        private float3 Raycast(float3 p0, float3 p1, float g0)
        {
            const int linearSearchSteps = 4;
            const int binarySearchSteps = 4;
            const float targetDist = .001f;
            float step = 1f / linearSearchSteps;

            float3 p = float3.zero;
            float3 ro = (g0 < 0) ? p1 : p0;
            float3 rt = (g0 < 0) ? p0 : p1;
            float3 rd = rt - ro;
            float d;
            float t = step;

            // linear search
            for (int i = 0; i < linearSearchSteps; i++)
            {
                p = ro + rd * t;
                d = Map(p);

                if (d < 0)
                {
                    break;
                }

                t += step;
            }

            // binary search
            for (int i = 0; i < binarySearchSteps; i++)
            {
                p = ro + rd * t;
                d = Map(p);

                if (math.abs(d) < targetDist)
                {
                    return p;
                }

                step *= 0.5f;
                t += step * ((d > 0) ? 1 : 0);
                t -= step * ((d < 0) ? 1 : 0);
            }

            return p;
        }

        private void TriangulateCell(int3 cellPos, bool placeCentroid, NativeArray<float> grid, NativeArray<float> ATA)
        {
            int i, j, m, iu, iv, du, dv;
            int mask, edgeMask, edgeCount, bufNo;
            int v0, v1, v2, v3;

            float d, s, gcScale, gcDot;

            float3 position, normal, masspoint, p, n, p0, p1, vi;
            int3 cellMin, cellMax;
            int2 e = int2.zero;

            var R = int3.zero;
            R[0] = 1;
            R[1] = DataSize + 1;
            R[2] = R[1] * R[1];

            bufNo = cellPos[2];
            m = 1 + R[1] * (1 + bufNo * R[1]);
            m += (cellPos[0] + cellPos[1] * (DataSize - 1) + 2 * cellPos[1]);

            mask = 0;

            for (i = 0; i < 8; i++)
            {
                vi = SurfaceNets.CUBE_VERTS[i] + cellPos;
                d = Map(vi * CellSize + ChunkMin);
                grid[i] = d;
                mask |= (d > 0) ? (1 << i) : 0;
            }

            if (mask == 0 || mask == 0xff)
                return;

            edgeMask = SurfaceNets.EDGE_TABLE[mask];
            edgeCount = 0;
            position = float3.zero;

#if USE_DUAL_CONTOURING
            QefSolver.ClearMatTri(ref ATA);
            var ATb = float4.zero;
            var pointaccum = float4.zero;
            normal = float3.zero;
#endif

            for (i = 0; i < 12 && edgeCount < 6 && !placeCentroid; ++i)
            {
                if ((edgeMask & (1 << i)) == 0)
                    continue;

                e[0] = SurfaceNets.CUBE_EDGES[i << 1];
                e[1] = SurfaceNets.CUBE_EDGES[(i << 1) + 1];

                p0 = SurfaceNets.CUBE_VERTS[e[0]] + cellPos;
                p1 = SurfaceNets.CUBE_VERTS[e[1]] + cellPos;

                p0 = p0 * CellSize + ChunkMin;
                p1 = p1 * CellSize + ChunkMin;

                p = Raycast(p0, p1, grid[e[0]]);

#if USE_DUAL_CONTOURING
                n = CalcNormal(p);
                QefSolver.Add(n, p, ref ATA, ref ATb, ref pointaccum);
                normal += n;
#endif

                position += p;
                edgeCount++;
            }

            if (edgeCount == 0 && !placeCentroid) 
                return;

            cellMin = cellPos * CellSize + ChunkMin;
            cellMax = cellMin + CellSize;

            if (placeCentroid)
            {
                position = (cellMin + cellMax) / 2;
            }
            else
            {
                s = 1f / edgeCount;
                position *= s;
                masspoint = position;

#if USE_DUAL_CONTOURING
                normal = math.normalize(normal * s);
                QefSolver.Solve(ATA, ATb, pointaccum, out position);
                gcDot = math.max(0, math.dot(normal, math.normalize(position - masspoint)));
                gcScale = .1f * gcDot;
#if USE_DC_GRADIENT_DESCENT
                // gradient descent with inflated position
                position += normal * gcScale;
                for (j = 0; j < GRADIENT_DESCENT_ITERATIONS; j++)
                {
                    d = Map(position);
                    if (math.abs(d) < 0.001f) break;
                    position -= CalcNormal(position) * d;
                }
#endif
#endif
            }

            IndexCache[m] = MeshCounts[0];
            VertexBuffer[MeshCounts[0]++] = position - ChunkMin;

            for (i = 0; i < 3; ++i)
            {
                if ((edgeMask & (1 << i)) == 0)
                    continue;

                iu = (i + 1) % 3;
                iv = (i + 2) % 3;

                if (cellPos[iu] == 0 || cellPos[iv] == 0)
                    continue;

                du = R[iu];
                dv = R[iv];

                v0 = IndexCache[m];
                v1 = IndexCache[m - du];
                v2 = IndexCache[m - dv];
                v3 = IndexCache[m - du - dv];

                if ((mask & 1) > 0)
                {
                    IndexBuffer[MeshCounts[1]++] = v0;
                    IndexBuffer[MeshCounts[1]++] = v3;
                    IndexBuffer[MeshCounts[1]++] = v1;

                    IndexBuffer[MeshCounts[1]++] = v0;
                    IndexBuffer[MeshCounts[1]++] = v2;
                    IndexBuffer[MeshCounts[1]++] = v3;
                }
                else
                {
                    IndexBuffer[MeshCounts[1]++] = v0;
                    IndexBuffer[MeshCounts[1]++] = v3;
                    IndexBuffer[MeshCounts[1]++] = v2;

                    IndexBuffer[MeshCounts[1]++] = v0;
                    IndexBuffer[MeshCounts[1]++] = v1;
                    IndexBuffer[MeshCounts[1]++] = v3;
                }
            }
        }

        private void Triangulate()
        {
            var grid = new NativeArray<float>(8, Allocator.Temp);
            var ATA = new NativeArray<float>(6, Allocator.Temp);

            var cellPos = int3.zero;
            int3 cellDims = ChunkSize;
            int3 worldSize = new int3(16, 4, 16);
            int3 chunkSize = ChunkSize * CellSize;
            worldSize *= chunkSize;

            for (cellPos[2] = 0; cellPos[2] < cellDims[2]; ++cellPos[2])
            {
                for (cellPos[1] = 0; cellPos[1] < cellDims[1]; ++cellPos[1])
                {
                    for (cellPos[0] = 0; cellPos[0] < cellDims[0]; ++cellPos[0])
                    {
                        int p = 4;
                        //var cellMin = cellPos * CellSize + ChunkMin;
                        //bool placeCentroid = 
                        //    cellMin[0] > p && cellMin[0] < worldSize[0] - p &&
                        //    cellMin[2] > p && cellMin[2] < worldSize[2] - p;
                        bool placeCentroid =
                            cellPos[0] > p && cellPos[0] < cellDims[0] - p &&
                            cellPos[2] > p && cellPos[2] < cellDims[2] - p;

                        TriangulateCell(cellPos, placeCentroid, grid, ATA);
                    }
                }
            }
        }
    }
}
