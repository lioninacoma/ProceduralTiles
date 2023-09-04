using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace ChunkBuilder
{
    public interface IChunkBuilderWorker
    {
        public bool ScheduleJob(ChunkBuilder.JobParams Params);

        public bool IsJobActive();

        public int GetWorkerIndex();

        public void SetWorkerIndex(int index);

        public void NotifyBuilderReady(int index);

        public IEnumerator ExecuteJob();
    }
}
