using System.Collections;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

using static IsoMeshStructs;

namespace ChunkBuilder
{
    public class ChunkBuilderWorker_CPU : MonoBehaviour, IChunkBuilderWorker
    {
        private NativeArray<float3> TempVerticesArray;
        private NativeArray<int> TempIndicesArray;
        private NativeArray<int> TempIndexCacheArray;
        private NativeArray<int> TempMeshCountsArray;

        private int BufferSize;
        private int DataSize;
        private int CellSize;
        private int ChunkSize;

        private BuildJob_CPU CurrentJob;
        private ChunkBuilder.JobParams CurrentParams;
        private ChunkBuilder Builder;
        private bool JobActive;

        private int WorkerIndex;

        private void Awake()
        {
            Builder = GetComponentInParent<ChunkBuilder>();
            Builder.GetChunkMeta(out CellSize, out ChunkSize, out DataSize, out BufferSize);

            TempIndexCacheArray = new NativeArray<int>(ChunkBuilder.INDEX_BUFFER_SIZE, Allocator.Persistent);
            TempIndicesArray = new NativeArray<int>(ChunkBuilder.INDEX_BUFFER_SIZE, Allocator.Persistent);
            TempVerticesArray = new NativeArray<float3>(ChunkBuilder.VERTEX_BUFFER_SIZE, Allocator.Persistent);
            TempMeshCountsArray = new NativeArray<int>(2, Allocator.Persistent);

            JobActive = false;
        }

        private void OnDestroy()
        {
            TempIndexCacheArray.Dispose();
            TempIndicesArray.Dispose();
            TempVerticesArray.Dispose();
            TempMeshCountsArray.Dispose();
        }

        public bool ScheduleJob(ChunkBuilder.JobParams Params)
        {
            if (JobActive) return true;

            CurrentJob = new BuildJob_CPU
            {
                ChunkMin = Params.ChunkMin,
                ChunkSize = ChunkSize,
                DataSize = DataSize,
                BufferSize = BufferSize,
                CellSize = CellSize,
                ChunkIndex = Params.ChunkIndex
            };

            CurrentParams = Params;
            JobActive = true;

            StartCoroutine(ExecuteJob());
            return false;
        }

        public IEnumerator ExecuteJob()
        {
            TempMeshCountsArray[0] = 0;
            TempMeshCountsArray[1] = 0;

            var job = CurrentJob;
            job.IndexCache = TempIndexCacheArray;
            job.IndexBuffer = TempIndicesArray;
            job.VertexBuffer = TempVerticesArray;
            job.MeshCounts = TempMeshCountsArray;
            job.InitSDF = CurrentParams.InitSDF;

            if (job.InitSDF)
            {
                job.SignedDistanceField = new NativeArray<float>(BufferSize, Allocator.Persistent);
                job.CentroidCellBuffer = new NativeArray<bool>(BufferSize, Allocator.Persistent);
                
                for (int i = 0; i < BufferSize; i++)
                    job.CentroidCellBuffer[i] = false;
            }
            else
            {
                job.SignedDistanceField = CurrentParams.SDF;
                job.CentroidCellBuffer = CurrentParams.CCs;
            }

            JobActive = true;

            var jobHandle = job.Schedule();
            yield return StartCoroutine(UpdateChunk(jobHandle, job));
        }

        private IEnumerator UpdateChunk(JobHandle jobHandle, BuildJob_CPU job)
        {
            yield return new WaitUntil(() => jobHandle.IsCompleted);
            jobHandle.Complete();

            var counts = new Counts();
            counts.VertexCount = TempMeshCountsArray[0];
            counts.IndexCount = TempMeshCountsArray[1];

            var Params = CurrentParams;

            if (counts.IndexCount > 0)
            {
                var dataArray = Mesh.AllocateWritableMeshData(1);
                var data = dataArray[0];

                data.SetVertexBufferParams(counts.IndexCount, ChunkBuilder.VERTEX_ATTRIBUTES);
                data.SetIndexBufferParams(counts.IndexCount, ChunkBuilder.INDEX_FORMAT);

                var vertices = data.GetVertexData<float3>();
                var indices = data.GetIndexData<int>();

                for (int i = 0; i < counts.IndexCount; i++)
                {
                    vertices[i] = TempVerticesArray[TempIndicesArray[i]];
                }

                for (int i = 0; i < counts.IndexCount; i++)
                {
                    indices[i] = i;
                }

                data.subMeshCount = 1;
                data.SetSubMesh(0, new SubMeshDescriptor(0, counts.IndexCount));

                var chunkData = new Chunk.Data()
                {
                    MeshData = dataArray,
                    SDF = job.SignedDistanceField,
                    CCs = job.CentroidCellBuffer
                };

                Params.Callback(job.ChunkIndex, counts.IndexCount, chunkData);
            }
            else
            {
                job.SignedDistanceField.Dispose();
                job.CentroidCellBuffer.Dispose();
                Params.Callback(job.ChunkIndex, 0, null);
            }

            JobActive = false;
            NotifyBuilderReady(WorkerIndex);
        }

        public bool IsJobActive()
        {
            return JobActive;
        }

        public int GetWorkerIndex()
        {
            return WorkerIndex;
        }

        public void NotifyBuilderReady(int index)
        {
            Builder.OnJobReady(index);
        }

        public void SetWorkerIndex(int index)
        {
            WorkerIndex = index;
        }
    }
}
