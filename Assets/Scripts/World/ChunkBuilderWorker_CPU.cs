using System;
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
        private NativeArray<float> SignedDistanceField;
        private NativeArray<Vertex> TempVerticesArray;
        private NativeArray<int> TempIndicesArray;
        private NativeArray<int> IndexCacheArray;
        private NativeArray<int> MeshCountsArray;

        private int BufferSize;
        private int DataSize;
        private int CellSize;
        private int ChunkSize;

        private BuildJobDC_CPU CurrentJob;
        private ChunkBuilder.JobParams CurrentParams;
        private ChunkBuilder Builder;
        private bool JobActive;

        private int WorkerIndex;

        private void Awake()
        {
            Builder = GetComponentInParent<ChunkBuilder>();
            Builder.GetChunkMeta(out CellSize, out ChunkSize, out DataSize, out BufferSize);

            IndexCacheArray = new NativeArray<int>(ChunkBuilder.INDEX_BUFFER_SIZE, Allocator.Persistent);
            TempIndicesArray = new NativeArray<int>(ChunkBuilder.INDEX_BUFFER_SIZE, Allocator.Persistent);
            TempVerticesArray = new NativeArray<Vertex>(ChunkBuilder.VERTEX_BUFFER_SIZE, Allocator.Persistent);
            SignedDistanceField = new NativeArray<float>(BufferSize, Allocator.Persistent);
            MeshCountsArray = new NativeArray<int>(2, Allocator.Persistent);

            JobActive = false;
        }

        private void OnDestroy()
        {
            try
            {
                if (IndexCacheArray != null)
                    IndexCacheArray.Dispose();
                if (TempIndicesArray != null)
                    TempIndicesArray.Dispose();
                if (TempVerticesArray != null)
                    TempVerticesArray.Dispose();
                if (SignedDistanceField != null)
                    SignedDistanceField.Dispose();
                if (MeshCountsArray != null)
                    MeshCountsArray.Dispose();
            }
            catch (Exception) { }
        }

        public bool ScheduleJob(ChunkBuilder.JobParams Params)
        {
            if (JobActive) return true;

            CurrentJob = new BuildJobDC_CPU
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
            MeshCountsArray[0] = 0;
            MeshCountsArray[1] = 0;

            var job = CurrentJob;
            job.SignedDistanceField = SignedDistanceField;
            job.IndexCacheArray = IndexCacheArray;
            job.TempIndicesArray = TempIndicesArray;
            job.TempVerticesArray = TempVerticesArray;
            job.MeshCountsArray = MeshCountsArray;

            JobActive = true;

            var jobHandle = job.Schedule();
            yield return StartCoroutine(UpdateChunk(jobHandle, job));
        }

        private IEnumerator UpdateChunk(JobHandle jobHandle, BuildJobDC_CPU job)
        {
            yield return new WaitUntil(() => jobHandle.IsCompleted);
            jobHandle.Complete();

            var counts = new Counts();
            counts.VertexCount = MeshCountsArray[0];
            counts.IndexCount = MeshCountsArray[1];

            var Params = CurrentParams;

            if (counts.IndexCount > 0)
            {
                var dataArray = Mesh.AllocateWritableMeshData(1);
                var data = dataArray[0];

                data.SetVertexBufferParams(counts.VertexCount, ChunkBuilder.VERTEX_ATTRIBUTES);
                data.SetIndexBufferParams(counts.IndexCount, ChunkBuilder.INDEX_FORMAT);

                var vertices = data.GetVertexData<Vertex>();
                var indices = data.GetIndexData<int>();

                for (int i = 0; i < counts.VertexCount; i++)
                {
                    vertices[i] = TempVerticesArray[i];
                }

                for (int i = 0; i < counts.IndexCount; i++)
                {
                    indices[i] = TempIndicesArray[i];
                }

                data.subMeshCount = 1;
                data.SetSubMesh(0, new SubMeshDescriptor(0, counts.IndexCount));

                Params.Callback(job.ChunkIndex, counts.IndexCount, dataArray);
            }
            else
            {
                Params.Callback(job.ChunkIndex, 0, default);
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
