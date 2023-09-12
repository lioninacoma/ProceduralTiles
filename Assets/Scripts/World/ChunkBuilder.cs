using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering;

namespace ChunkBuilder
{
    public class ChunkBuilder : MonoBehaviour
    {
        public static readonly IndexFormat INDEX_FORMAT = IndexFormat.UInt32;
        public static readonly int VERTEX_BUFFER_SIZE = 16000;
        public static readonly int INDEX_BUFFER_SIZE = VERTEX_BUFFER_SIZE * 3;
        public static readonly int INDEX_CACHE_SIZE = INDEX_BUFFER_SIZE;
        public static readonly VertexAttributeDescriptor[] VERTEX_ATTRIBUTES = new[] {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3)
        };

        private static readonly int MAX_CONCURRENT_SCHEDULED_JOBS = 16;

        public struct JobParams
        {
            public System.Action<int, int, Chunk.Data> Callback;
            public int3 ChunkMin;
            public int ChunkIndex;
            public bool InitSDF;
            public NativeArray<float> SDF;
        }

        private List<IChunkBuilderWorker> Workers;
        private Queue<JobParams> PendingBuildJobs;
        private Queue<int> AvailableWorkers;

        public int GPUWorkerCount = 4;
        public int CPUWorkerCount = 4;

        private void Awake()
        {
            Workers = new List<IChunkBuilderWorker>();
            PendingBuildJobs = new Queue<JobParams>();
            AvailableWorkers = new Queue<int>();
        }

        private void Start()
        {
            IChunkBuilderWorker worker;

            if (GPUWorkerCount > 0)
            {
                ChunkBuilderWorker_GPU.MAX_BUFFERS = GPUWorkerCount;
                worker = transform.AddComponent<ChunkBuilderWorker_GPU>();
                worker.SetWorkerIndex(Workers.Count);
                AvailableWorkers.Enqueue(Workers.Count);
                Workers.Add(worker);
            }

            for (int i = 0; i < CPUWorkerCount; i++)
            {
                worker = transform.AddComponent<ChunkBuilderWorker_CPU>();
                worker.SetWorkerIndex(Workers.Count);
                AvailableWorkers.Enqueue(Workers.Count);
                Workers.Add(worker);
            }
        }

        public void AddJob(int3 chunkMin, int chunkIndex, System.Action<int, int, Chunk.Data> callback)
        {
            PendingBuildJobs.Enqueue(new JobParams()
            {
                ChunkMin = chunkMin,
                ChunkIndex = chunkIndex,
                InitSDF = true,
                Callback = callback
            });
        }

        public void AddJob(int3 chunkMin, int chunkIndex, NativeArray<float> sdf, System.Action<int, int, Chunk.Data> callback)
        {
            PendingBuildJobs.Enqueue(new JobParams()
            {
                ChunkMin = chunkMin,
                ChunkIndex = chunkIndex,
                SDF = sdf,
                InitSDF = false,
                Callback = callback
            });
        }

        public void GetChunkMeta(out int CellSize, out int ChunkSize, out int DataSize, out int BufferSize)
        {
            var world = GetComponentInParent<World>();
            world.GetChunkMeta(out CellSize, out ChunkSize, out DataSize, out BufferSize);
        }

        private void Update()
        {
            if (AvailableWorkers.Count == 0 || PendingBuildJobs.Count == 0) return;

            int maxJobsCount = System.Math.Min(PendingBuildJobs.Count, MAX_CONCURRENT_SCHEDULED_JOBS);

            for (int i = 0; i < maxJobsCount && AvailableWorkers.Count > 0; i++)
            {
                var worker = Workers[AvailableWorkers.Dequeue()];
                var job = PendingBuildJobs.Dequeue();
                bool stillAvailable = worker.ScheduleJob(job);
                
                if (stillAvailable)
                {
                    AvailableWorkers.Enqueue(worker.GetWorkerIndex());
                }
            }
        }

        public void OnJobReady(int index)
        {
            AvailableWorkers.Enqueue(index);
        }
    }
}
