using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Unity.Collections;
using UnityEngine;

public class NativeArrayPool<T> where T : struct
{
    private List<NativeArray<T>> pooledObjects;
    private ConcurrentQueue<int> freeIndices;
    private ConcurrentDictionary<int, int> inFreeIndices;
    private Semaphore freeIndicesSemaphore, testMultipleFreeSemaphore;
    private int poolSize;
    private Mutex getMutex;

    public NativeArrayPool(int poolSize, int arrayLen)
    {
        this.poolSize = poolSize;

        freeIndices = new ConcurrentQueue<int>();
        inFreeIndices = new ConcurrentDictionary<int, int>();
        pooledObjects = new List<NativeArray<T>>();
        freeIndicesSemaphore = new Semaphore(poolSize, poolSize);
        getMutex = new Mutex();
        testMultipleFreeSemaphore = new Semaphore(1, 1);

        NativeArray<T> tmp;
        for (int i = 0; i < poolSize; i++)
        {
            tmp = new NativeArray<T>(arrayLen, Allocator.Persistent);
            pooledObjects.Add(tmp);
            freeIndices.Enqueue(i);
            inFreeIndices.TryAdd(i, i);
        }
    }

    public int GetAmountFree()
    {
        return freeIndices.Count;
    }

    public void ReturnNativeArray(int index)
    {
        if (inFreeIndices.ContainsKey(index)) return;

        freeIndices.Enqueue(index);
        inFreeIndices.TryAdd(index, index);

        try { testMultipleFreeSemaphore.Release(); } catch (Exception) { }
        freeIndicesSemaphore.Release();
        //Debug.Log(freeIndices.Count);
    }

    private void WaitMultiple(int count)
    {
        while (freeIndices.Count < count)
        {
            testMultipleFreeSemaphore.WaitOne();
        }
    }

    public int[] GetNativeArrays(ref NativeArray<T>[] arrays)
    {
        var indices = new int[arrays.Length];
        //Debug.Log(freeIndices.Count);

        getMutex.WaitOne();
        {
            WaitMultiple(arrays.Length);

            int i, index;
            for (i = 0; i < arrays.Length; i++)
            {
                freeIndicesSemaphore.WaitOne();

                freeIndices.TryDequeue(out index);
                inFreeIndices.TryRemove(index, out _);
                arrays[i] = pooledObjects[index];
                indices[i] = index;
            }
        }
        getMutex.ReleaseMutex();

        return indices;
    }

    public int GetNativeArray(out NativeArray<T> array)
    {
        int index;
        //Debug.Log(freeIndices.Count);

        getMutex.WaitOne();
        {
            freeIndicesSemaphore.WaitOne();

            freeIndices.TryDequeue(out index);
            inFreeIndices.TryRemove(index, out _);
            array = pooledObjects[index];
        }
        getMutex.ReleaseMutex();

        return index;
    }

    public void Dispose()
    {
        for (int i = 0; i < poolSize; i++)
        {
            pooledObjects[i].Dispose();
        }
    }
}