using System;
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
        [WriteOnly] public NativeArray<float3> TempVerticesArray;
        [WriteOnly] public NativeArray<int> TempIndicesArray;

        public NativeArray<int> IndexCacheArray;
        public NativeArray<int> MeshCountsArray;

        [ReadOnly] public int BufferSize;
        [ReadOnly] public int DataSize;

        [ReadOnly] public int3 ChunkMin;
        [ReadOnly] public int ChunkSize;
        [ReadOnly] public int CellSize;
        [ReadOnly] public int ChunkIndex;

        public void Execute()
        {
            Triangulate(MeshCountsArray, TempIndicesArray, TempVerticesArray, IndexCacheArray);
        }

        private static float OpUnion(float d1, float d2) { return math.min(d1, d2); }
        private static float OpSubtract(float d1, float d2) { return math.max(-d1, d2); }
        private static float OpIntersect(float d1, float d2) { return math.max(d1, d2); }

        private static float OpUnionSmooth(float d1, float d2, float k)
        {
            float h = math.clamp(0.5f + 0.5f * (d2 - d1) / k, 0f, 1f);
            return math.lerp(d2, d1, h) - k * h * (1f - h);
        }

        private static float OpSubtractSmooth(float d1, float d2, float k)
        {
            float h = math.clamp(0.5f - 0.5f * (d2 + d1) / k, 0f, 1f);
            return math.lerp(d2, -d1, h) + k * h * (1f - h);
        }

        private static float OpIntersectSmooth(float d1, float d2, float k)
        {
            float h = math.clamp(0.5f - 0.5f * (d2 - d1) / k, 0f, 1f);
            return math.lerp(d2, d1, h) + k * h * (1f - h);
        }

        private static float3x3 OpRotateX(float a) { float sa = math.sin(a); float ca = math.cos(a); return new float3x3(1f, 0f, 0f, 0f, ca, sa, 0f, -sa, ca); }
        private static float3x3 OpRotateY(float a) { float sa = math.sin(a); float ca = math.cos(a); return new float3x3(ca, 0f, sa, 0f, 1f, 0f, -sa, 0f, ca); }
        private static float3x3 OpRotateZ(float a) { float sa = math.sin(a); float ca = math.cos(a); return new float3x3(ca, sa, 0f, -sa, ca, 0f, 0f, 0f, 1f); }

        private static float3 OpRep(float3 p, float s)
        {
            return p - s * math.round(p / s);
        }

        private static float3 OpRepLim(float3 p, float s, float3 l)
        {
            return p - s * math.clamp(math.round(p / s), -l, l);
        }

        private static float3 OpRepLim(float3 p, float s, float3 lima, float3 limb)
        {
            return p - s * math.clamp(math.round(p / s), lima, limb);
        }

        private static float3 OpRepLim(float3 p, float s, float2 lima, float2 limb)
        {
            var lima3 = new float3(lima, 0);
            var limb3 = new float3(limb, 0);
            return p - s * math.clamp(math.round(p / s), lima3.xzy, limb3.xzy);
        }

        private static float SdSphere(float3 p, float s)
        {
            return math.length(p) - s;
        }

        private static float SdBox(float3 p, float3 b)
        {
            float3 q = math.abs(p) - b;
            return math.length(math.max(q, 0.0f)) + math.min(math.max(q.x, math.max(q.y, q.z)), 0.0f);
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
                return SdModel(OpRepLim(p, s, lima, limb));
            }
        }

        private static float SdModel(float3 p)
        {
            float r = 6f;
            return SdBox(p, r);
        }

        private static readonly float SDF_BIAS = 0.0001f;

        private static float Map(float3 x)
        {
            float s = SdSurface(x);
            float d = SdRepLimModel(x - 16f, 20f, 0f, new float3(8f, 2f, 8f), true);
            s = OpUnion(d, s);
            return s + SDF_BIAS;
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
            const int linearSearchSteps = 8;
            const int binarySearchSteps = 8;
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

        private void Triangulate(NativeArray<int> meshCounts, NativeArray<int> indexBuffer, NativeArray<float3> vertexBuffer, NativeArray<int> indexCache)
        {
            int i, j, m, iu, iv, du, dv;
            int mask, edgeMask, edgeCount, bufNo;
            int v0, v1, v2, v3;
            float d;
            float3 position, p, n, p0, p1, vi;
            var cellPos = int3.zero;
            int3 cellDims = ChunkSize;
            var R = int3.zero;
            var x = int3.zero;
            var e = int2.zero;
            var grid = new NativeArray<float>(8, Allocator.Temp);
            var ATA = new NativeArray<float>(6, Allocator.Temp);
            
            R[0] = 1;
            R[1] = DataSize + 1;
            R[2] = R[1] * R[1];

            for (x[2] = 0; x[2] < cellDims[2]; ++x[2])
            {
                for (x[1] = 0; x[1] < cellDims[1]; ++x[1])
                {
                    for (x[0] = 0; x[0] < cellDims[0]; ++x[0])
                    {
                        cellPos.x = x[0]; cellPos.y = x[1]; cellPos.z = x[2];

                        bufNo = x[2];
                        m = 1 + R[1] * (1 + bufNo * R[1]);
                        m += (x[0] + x[1] * (DataSize - 1) + 2 * x[1]);

                        mask = 0;

                        for (i = 0; i < 8; i++)
                        {
                            vi = SurfaceNets.CUBE_VERTS[i] + cellPos;
                            d = Map(vi * CellSize + ChunkMin);
                            grid[i] = d;
                            mask |= (d > 0) ? (1 << i) : 0;
                        }

                        if (mask == 0 || mask == 0xff)
                            continue;

                        edgeMask = SurfaceNets.EDGE_TABLE[mask];
                        edgeCount = 0;
                        float3 normal = float3.zero;

                        QefSolver.ClearMatTri(ref ATA);
                        var ATb = float4.zero;
                        var pointaccum = float4.zero;

                        for (i = 0; i < 12 && edgeCount < 6; ++i)
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
                            n = CalcNormal(p);

                            QefSolver.Add(n, p, ref ATA, ref ATb, ref pointaccum);

                            normal += n;
                            edgeCount++;
                        }

                        if (edgeCount == 0) continue;

                        QefSolver.Solve(ATA, ATb, pointaccum, out position);

                        normal *= (1f / edgeCount);
                        var masspoint = pointaccum.xyz / pointaccum.w;
                        var dotWithMassPoint = math.max(0, math.dot(normal, math.normalize(position - masspoint)));

                        // gradient descent with inflated position
                        position += normal * .1f * dotWithMassPoint;
                        for (j = 0; j < 6; j++)
                        {
                            d = Map(position);
                            if (math.abs(d) < 0.001f) break;
                            position -= CalcNormal(position) * d;
                        }

                        indexCache[m] = meshCounts[0];
                        vertexBuffer[meshCounts[0]++] = position - ChunkMin;

                        for (i = 0; i < 3; ++i)
                        {
                            if ((edgeMask & (1 << i)) == 0)
                                continue;

                            iu = (i + 1) % 3;
                            iv = (i + 2) % 3;

                            if (x[iu] == 0 || x[iv] == 0)
                                continue;

                            du = R[iu];
                            dv = R[iv];

                            v0 = indexCache[m];
                            v1 = indexCache[m - du];
                            v2 = indexCache[m - dv];
                            v3 = indexCache[m - du - dv];

                            if ((mask & 1) > 0)
                            {
                                indexBuffer[meshCounts[1]++] = v0;
                                indexBuffer[meshCounts[1]++] = v3;
                                indexBuffer[meshCounts[1]++] = v1;

                                indexBuffer[meshCounts[1]++] = v0;
                                indexBuffer[meshCounts[1]++] = v2;
                                indexBuffer[meshCounts[1]++] = v3;
                            }
                            else
                            {
                                indexBuffer[meshCounts[1]++] = v0;
                                indexBuffer[meshCounts[1]++] = v3;
                                indexBuffer[meshCounts[1]++] = v2;

                                indexBuffer[meshCounts[1]++] = v0;
                                indexBuffer[meshCounts[1]++] = v1;
                                indexBuffer[meshCounts[1]++] = v3;
                            }
                        }
                    }
                }
            }
        }
    }
}
