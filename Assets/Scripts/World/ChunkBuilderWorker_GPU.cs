using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

using static IsoMeshStructs;

namespace ChunkBuilder
{
    public class ChunkBuilderWorker_GPU : MonoBehaviour, IChunkBuilderWorker
    {
        private class BufferReadback
        {
            public bool IndexBufferReady;
            public bool VertexBufferReady;
            public bool VolumeBufferReady;

            public float3[] VertexBuffer;
            public int[] IndexBuffer;
            public float[] VolumeBuffer;

            public BufferReadback(bool readbackVolume)
            {
                IndexBufferReady = false;
                VertexBufferReady = false;
                VolumeBufferReady = !readbackVolume;
            }

            public void OnReadbackVertexBuffer(AsyncGPUReadbackRequest request)
            {
                var data = request.GetData<float3>();
                VertexBuffer = data.ToArray(); // copies data
                VertexBufferReady = true;
                data.Dispose();
            }

            public void OnReadbackIndexBuffer(AsyncGPUReadbackRequest request)
            {
                var data = request.GetData<int>();
                IndexBuffer = data.ToArray(); // copies data
                IndexBufferReady = true;
                data.Dispose();
            }

            public void OnReadbackVolumeBuffer(AsyncGPUReadbackRequest request)
            {
                var data = request.GetData<float>();
                VolumeBuffer = data.ToArray(); // copies data
                VolumeBufferReady = true;
                data.Dispose();
            }
        }

        public static int MAX_BUFFERS = 8;

        private static readonly int CHUNKS_GROUP_SIZE_Y = 9;
        private static readonly int CELLS_GROUP_SIZE = 16;

        private static readonly int _SignedDistanceField = Shader.PropertyToID("_SignedDistanceField");
        private static readonly int _MeshCounts = Shader.PropertyToID("_MeshCounts");
        private static readonly int _ActiveCells = Shader.PropertyToID("_ActiveCells");
        private static readonly int _IndexCache = Shader.PropertyToID("_IndexCache");
        private static readonly int _VertexBuffer = Shader.PropertyToID("_VertexBuffer");
        private static readonly int _IndexBuffer = Shader.PropertyToID("_IndexBuffer");
        private static readonly int _ArgsBuffer = Shader.PropertyToID("_ArgsBuffer");
        private static readonly int _NodeKeys = Shader.PropertyToID("_NodeKeys");
        private static readonly int _DataSizes = Shader.PropertyToID("_DataSizes");
        private static readonly int _VolumeBufferSize = Shader.PropertyToID("_VolumeBufferSize");
        private static readonly int _VertexBufferSize = Shader.PropertyToID("_VertexBufferSize");
        private static readonly int _IndexBufferSize = Shader.PropertyToID("_IndexBufferSize");
        private static readonly int _IndexCacheSize = Shader.PropertyToID("_IndexCacheSize");

        private int BufferSize;
        private int DataSize;
        private int CellSize;
        private int ChunkSize;

        private ComputeShader BuildVolumeCS;
        private ComputeShader GenerateIndicesCS;
        private ComputeShader GenerateVerticesCS;

        private ComputeBuffer SignedDistanceField;
        private ComputeBuffer IndexCache;
        private ComputeBuffer VertexBuffer;
        private ComputeBuffer IndexBuffer;
        private ComputeBuffer ActiveCells;
        private ComputeBuffer MeshCounts;
        private ComputeBuffer ArgsBuffer;

        private int BuildVolumeKernelID;
        private int GenerateIndicesKernelID;
        private int GenerateVerticesKernelID;

        private Queue<ChunkBuilder.JobParams> JobQueue;
        private List<ChunkBuilder.JobParams> CurrentJobs;
        private bool JobActive;
        private ChunkBuilder Builder;

        private int WorkerIndex;

        private void Awake()
        {
            Builder = GetComponentInParent<ChunkBuilder>();
            Builder.GetChunkMeta(out CellSize, out ChunkSize, out DataSize, out BufferSize);

            BuildVolumeCS = Resources.Load<ComputeShader>("Shader/ChunkBuilder/BuildVolumeCS");
            GenerateIndicesCS = Resources.Load<ComputeShader>("Shader/ChunkBuilder/GenerateIndicesCS");
            GenerateVerticesCS = Resources.Load<ComputeShader>("Shader/ChunkBuilder/GenerateVerticesCS");

            SignedDistanceField = new ComputeBuffer(MAX_BUFFERS * BufferSize, sizeof(float));
            SignedDistanceField.SetData(new float[MAX_BUFFERS * BufferSize]);

            IndexCache = new ComputeBuffer(MAX_BUFFERS * ChunkBuilder.INDEX_CACHE_SIZE, sizeof(int));
            IndexCache.SetData(new uint[MAX_BUFFERS * ChunkBuilder.INDEX_CACHE_SIZE]);

            ActiveCells = new ComputeBuffer(MAX_BUFFERS * BufferSize, UnsafeUtility.SizeOf<ActiveCell>(), ComputeBufferType.Counter);
            ActiveCells.SetData(new ActiveCell[MAX_BUFFERS * BufferSize]);

            VertexBuffer = new ComputeBuffer(MAX_BUFFERS * ChunkBuilder.VERTEX_BUFFER_SIZE, UnsafeUtility.SizeOf<float3>());
            VertexBuffer.SetData(new float3[MAX_BUFFERS * ChunkBuilder.VERTEX_BUFFER_SIZE]);

            IndexBuffer = new ComputeBuffer(MAX_BUFFERS * ChunkBuilder.INDEX_BUFFER_SIZE, sizeof(int));
            IndexBuffer.SetData(new int[MAX_BUFFERS * ChunkBuilder.INDEX_BUFFER_SIZE]);

            MeshCounts = new ComputeBuffer(MAX_BUFFERS, UnsafeUtility.SizeOf<Counts>());
            ArgsBuffer = new ComputeBuffer(1, UnsafeUtility.SizeOf<Args>(), ComputeBufferType.IndirectArguments);

            BuildVolumeKernelID = BuildVolumeCS.FindKernel("CSMain");
            GenerateIndicesKernelID = GenerateIndicesCS.FindKernel("CSMain");
            GenerateVerticesKernelID = GenerateVerticesCS.FindKernel("CSMain");

            BuildVolumeCS.SetBuffer(BuildVolumeKernelID, _SignedDistanceField, SignedDistanceField);
            BuildVolumeCS.SetInt(_VolumeBufferSize, BufferSize);

            GenerateVerticesCS.SetBuffer(GenerateVerticesKernelID, _SignedDistanceField, SignedDistanceField);
            GenerateVerticesCS.SetBuffer(GenerateVerticesKernelID, _MeshCounts, MeshCounts);
            GenerateVerticesCS.SetBuffer(GenerateVerticesKernelID, _ActiveCells, ActiveCells);
            GenerateVerticesCS.SetBuffer(GenerateVerticesKernelID, _IndexCache, IndexCache);
            GenerateVerticesCS.SetBuffer(GenerateVerticesKernelID, _VertexBuffer, VertexBuffer);
            GenerateVerticesCS.SetInt(_VolumeBufferSize, BufferSize);
            GenerateVerticesCS.SetInt(_VertexBufferSize, ChunkBuilder.VERTEX_BUFFER_SIZE);
            GenerateVerticesCS.SetInt(_IndexCacheSize, ChunkBuilder.INDEX_CACHE_SIZE);

            GenerateIndicesCS.SetBuffer(GenerateIndicesKernelID, _ActiveCells, ActiveCells);
            GenerateIndicesCS.SetBuffer(GenerateIndicesKernelID, _IndexCache, IndexCache);
            GenerateIndicesCS.SetBuffer(GenerateIndicesKernelID, _MeshCounts, MeshCounts);
            GenerateIndicesCS.SetBuffer(GenerateIndicesKernelID, _IndexBuffer, IndexBuffer);
            GenerateIndicesCS.SetBuffer(GenerateIndicesKernelID, _ArgsBuffer, ArgsBuffer);
            GenerateIndicesCS.SetInt(_IndexBufferSize, ChunkBuilder.INDEX_BUFFER_SIZE / 3);
            GenerateIndicesCS.SetInt(_IndexCacheSize, ChunkBuilder.INDEX_CACHE_SIZE);

            JobQueue = new Queue<ChunkBuilder.JobParams>();
            CurrentJobs = new List<ChunkBuilder.JobParams>();
            JobActive = false;
        }

        private void OnDestroy()
        {
            SignedDistanceField.Dispose();
            IndexCache.Dispose();
            ActiveCells.Dispose();
            VertexBuffer.Dispose();
            IndexBuffer.Dispose();
            MeshCounts.Dispose();
            ArgsBuffer.Dispose();
        }

        public bool ScheduleJob(ChunkBuilder.JobParams Params)
        {
            if (JobActive) return false;
            JobQueue.Enqueue(Params);
            return JobQueue.Count < MAX_BUFFERS;
        }

        private void Update()
        {
            if (JobActive || JobQueue.Count == 0) return;

            int buildCount = System.Math.Min(JobQueue.Count, MAX_BUFFERS);

            for (int i = 0; i < buildCount; i++)
            {
                CurrentJobs.Add(JobQueue.Dequeue());
            }

            JobActive = true;
            StartCoroutine(ExecuteJob());
        }

        public IEnumerator ExecuteJob()
        {
            int buildCount = CurrentJobs.Count;
            var counts = new Counts[buildCount];
            var nodeKeys = new Vector4[buildCount];
            var dataSizes = new Vector4[buildCount];
            bool initVolume = false;
            ChunkBuilder.JobParams job;
            int volumeOffset;

            for (int i = 0; i < buildCount; i++)
            {
                job = CurrentJobs[i];
                nodeKeys[i] = new float4(job.ChunkMin, CellSize);
                dataSizes[i] = new float4(DataSize, 0, 0, 0);
                initVolume |= job.InitSDF;

                if (!job.InitSDF)
                {
                    volumeOffset = i * BufferSize;
                    SignedDistanceField.SetData(job.SDF, 0, volumeOffset, BufferSize);
                }
            }

            MeshCounts.SetData(counts);
            ActiveCells.SetCounterValue(0);
            
            GenerateVerticesCS.SetVectorArray(_NodeKeys, nodeKeys);
            GenerateVerticesCS.SetVectorArray(_DataSizes, dataSizes);
            GenerateIndicesCS.SetVectorArray(_DataSizes, dataSizes);

            int groupsY;

            if (initVolume)
            {
                BuildVolumeCS.SetVectorArray(_NodeKeys, nodeKeys);
                BuildVolumeCS.SetVectorArray(_DataSizes, dataSizes);

                groupsY = (int)Mathf.Max(1, Mathf.Ceil(DataSize / (float)CHUNKS_GROUP_SIZE_Y));
                BuildVolumeCS.Dispatch(BuildVolumeKernelID, buildCount, groupsY, DataSize);
            }

            groupsY = (int)Mathf.Max(1, Mathf.Ceil(ChunkSize / (float)CHUNKS_GROUP_SIZE_Y));
            GenerateVerticesCS.Dispatch(GenerateVerticesKernelID, buildCount, groupsY, ChunkSize);

            ComputeBuffer.CopyCount(ActiveCells, ArgsBuffer, 0);

            var argsData = new Args[1];
            ArgsBuffer.GetData(argsData);

            int cellCount = (int)argsData[0].indexCountPerInstance;

            if (cellCount > 0)
            {
                int groupX = Mathf.Max(1, (int)Mathf.Ceil(cellCount / (float)CELLS_GROUP_SIZE));
                GenerateIndicesCS.Dispatch(GenerateIndicesKernelID, groupX, 1, 1);

                MeshCounts.GetData(counts);

                var rb = new BufferReadback(initVolume);
                AsyncGPUReadback.Request(VertexBuffer, rb.OnReadbackVertexBuffer);
                AsyncGPUReadback.Request(IndexBuffer, rb.OnReadbackIndexBuffer);
                
                if (initVolume)
                {
                    AsyncGPUReadback.Request(SignedDistanceField, rb.OnReadbackVolumeBuffer);
                }

                yield return StartCoroutine(UpdateChunk(rb, counts));
            }
            else
            {
                foreach (var j in CurrentJobs)
                    j.Callback(j.ChunkIndex, 0, 1, null);
            }

            JobActive = false;
            CurrentJobs.Clear();
            NotifyBuilderReady(WorkerIndex);
        }

        private IEnumerator UpdateChunk(BufferReadback rb, Counts[] countsArray)
        {
            yield return new WaitUntil(() => rb.VertexBufferReady && rb.IndexBufferReady && rb.VolumeBufferReady);

            ChunkBuilder.JobParams job;
            Chunk.Data chunkData;
            Counts counts;

            int buildCount = CurrentJobs.Count;

            for (int i = 0; i < buildCount; i++)
            {
                counts = countsArray[i];
                job = CurrentJobs[i];

                if (counts.IndexCount == 0 || counts.VertexCount == 0)
                {
                    job.Callback(job.ChunkIndex, 0, 1, null);
                }
                else
                {

                    chunkData = BuildChunkData(i, counts, rb);
                    job.Callback(job.ChunkIndex, counts.IndexCount, 0, chunkData);
                }
            }

            rb.VertexBuffer = null;
            rb.IndexBuffer = null;
            rb.VolumeBuffer = null;
        }

        private Chunk.Data BuildChunkData(int chunkId, Counts counts, BufferReadback rb)
        {
            var job = CurrentJobs[chunkId];

            var dataArray = Mesh.AllocateWritableMeshData(1);
            var data = dataArray[0];

            int vertexCount = counts.VertexCount;
            int indexCount = counts.IndexCount * 3;
            int vertexOffset = chunkId * ChunkBuilder.VERTEX_BUFFER_SIZE;
            int indexOffset = chunkId * ChunkBuilder.INDEX_BUFFER_SIZE;
            int volumeOffset = chunkId * BufferSize;

            data.SetVertexBufferParams(vertexCount, ChunkBuilder.VERTEX_ATTRIBUTES);
            data.SetIndexBufferParams(indexCount, ChunkBuilder.INDEX_FORMAT);

            int i;
            var vertices = data.GetVertexData<float3>();
            var indices = data.GetIndexData<int>();
            NativeArray<float> volume;

            if (job.InitSDF)
            {
                volume = new NativeArray<float>(BufferSize, Allocator.Persistent);
                
                for (i = 0; i < BufferSize; i++)
                {
                    volume[i] = rb.VolumeBuffer[volumeOffset + i];
                }
            }
            else
            {
                volume = job.SDF;
            }

            for (i = 0; i < vertexCount; i++)
            {
                vertices[i] = rb.VertexBuffer[vertexOffset + i];
            }

            for (i = 0; i < indexCount; i++)
            {
                indices[i] = rb.IndexBuffer[indexOffset + i];
            }

            data.subMeshCount = 1;
            data.SetSubMesh(0, new SubMeshDescriptor(0, indexCount));

            return new Chunk.Data()
            {
                MeshData = dataArray,
                SDF = volume
            };
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
