using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace ChunkOctree
{
    public interface IOctreeNodeContent
    {
        public int3 Min { get; set; }
        public int3 Max { get; set; }
        public float3 Position { get; set; }
        public int Size { get; set; }
    }

    public static class BoundsUtils
    {
        public static bool IntersectsBounds<T>(float3 min, float3 max, OctreeNode<T> b) where T : IOctreeNodeContent
        {
            return (min.x <= b.Max.x && max.x >= b.Min.x) &&
                   (min.y <= b.Max.y && max.y >= b.Min.y) &&
                   (min.z <= b.Max.z && max.z >= b.Min.z);
        }

        public static bool IntersectsBounds<T>(T a, OctreeNode<T> b) where T : IOctreeNodeContent
        {
            return IntersectsBounds(a.Min, a.Max, b);
        }

        public static bool IntersectsPosition<T>(float3 p, OctreeNode<T> b) where T : IOctreeNodeContent
        {
            return (p.x <= b.Max.x && p.x >= b.Min.x) &&
                   (p.y <= b.Max.y && p.y >= b.Min.y) &&
                   (p.z <= b.Max.z && p.z >= b.Min.z);
        }

        public static bool IntersectsPosition<T>(T a, OctreeNode<T> b) where T : IOctreeNodeContent
        {
            return IntersectsPosition(a.Position, b);
        }
    }

    public class OctreeNode<T> where T : IOctreeNodeContent
    {
        public static readonly int3[] CHILD_MIN_OFFSETS =
        {
            new int3(0, 0, 0),
            new int3(1, 0, 0),
            new int3(0, 1, 0),
            new int3(1, 1, 0),
            new int3(0, 0, 1),
            new int3(1, 0, 1),
            new int3(0, 1, 1),
            new int3(1, 1, 1),
        };

        public T Content { get; set; }
        public int3 Min { get; set; }
        public int3 Max { get; set; }
        public int Size { get; set; }
        public OctreeNode<T>[] Children { get; set; }
        private int MinSize { get; }

        public OctreeNode(int3 min, int size, int minSize)
        {
            Content = default;
            Min = min;
            Max = min + size;
            Size = size;
            Children = new OctreeNode<T>[8];
            MinSize = minSize;
        }

        private OctreeNode<T> AllocateChild(int i)
        {
            int childSize = Size / 2;
            int3 childMin = Min + (childSize * CHILD_MIN_OFFSETS[i]);
            return new OctreeNode<T>(childMin, childSize, MinSize);
        }

        public bool Insert(T Content)
        {
            if (Size < Content.Size || !BoundsUtils.IntersectsPosition(Content, this))
                return false;

            if (Size > Content.Size)
            {
                OctreeNode<T> child;

                for (int i = 0; i < 8; i++)
                {
                    child = Children[i];

                    if (child == null)
                    {
                        child = AllocateChild(i);
                        Children[i] = child;
                    }

                    if (BoundsUtils.IntersectsPosition(Content, child))
                    {
                        return child.Insert(Content);
                    }
                }
            }
            else if (Min.Equals(Content.Min) && Size == Content.Size)
            {
                this.Content = Content;
                return true;
            }

            return false;
        }

        public void Find(float3 min, float3 max, List<T> nodes)
        {
            if (!BoundsUtils.IntersectsBounds(min, max, this))
                return;

            if (Size > MinSize)
            {
                OctreeNode<T> child;

                for (int i = 0; i < 8; i++)
                {
                    child = Children[i];

                    if (child == null)
                    {
                        continue;
                    }

                    if (BoundsUtils.IntersectsBounds(min, max, child))
                    {
                        child.Find(min, max, nodes);
                    }
                }
            }
            else
            {
                nodes.Add(Content);
            }
        }

        public void DrawBounds(Transform transform)
        {
            var position = new float3(Min + Max) / 2f;
            Gizmos.DrawWireCube(
                transform.TransformPoint(position), 
                transform.TransformVector(Vector3.one * Size));
        }

        public void DrawNodeBounds(Transform transform)
        {
            if (Size > MinSize)
            {
                OctreeNode<T> child;

                for (int i = 0; i < 8; i++)
                {
                    child = Children[i];

                    if (child == null)
                    {
                        continue;
                    }

                    child.DrawNodeBounds(transform);
                }
            }
            else
            {
                DrawBounds(transform);
            }
        }
    }
}
