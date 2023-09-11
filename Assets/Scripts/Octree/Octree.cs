//
// Copyright(c) 2021, Mathias Baske.
// All rights reserved.
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are met:
//
// * Redistributions of source code must retain the above copyright notice, this
//   list of conditions and the following disclaimer.
//
// * Redistributions in binary form must reproduce the above copyright notice,
//   this list of conditions and the following disclaimer in the documentation
//   and/or other materials provided with the distribution.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
// AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
// IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
// DISCLAIMED.IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
// FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
// DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
// SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
// CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
// OR TORT(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
//
using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Octree for Unity.
/// 
/// Unlike [Nition's octree](https://github.com/Nition/UnityOctree), my tree does not distinguish 
/// between point vs bounds nodes, nor does it have special root accessor objects. The idea was 
/// to keep things simple. With Nition's tree, I found myself adding stuff like a Dispose() method 
/// in four different places, although it's always doing the same thing. With this tree, it should 
/// be more straight-forward to implement new features.
/// </summary>
namespace MBaske.Octree
{
    /// <summary>
    /// Interface for node content.
    /// The intention behind NOT making tree contents fully generic was to avoid redundant storage 
    /// of location data.
    /// Nition's octrees keep object positions/bounds separate from contents. This often requires 
    /// additionally storing them in the content objects as well, since the trees' public methods 
    /// don't provide access to the underlying OctreeObjects.
    /// </summary>
    public interface INodeContent
    {
        /// <summary>
        /// Content object bounds.
        /// </summary>
        Bounds Bounds { get; }
        /// <summary>
        /// Content object position.
        /// </summary>
        Vector3 Position { get; }
    }

    /// <summary>
    /// Interface for node content supporting nearest neighbor search.
    /// </summary>
    public interface INodeContentNNS : INodeContent
    {
        /// <summary>
        /// Square distance to nearest neighbor.
        /// </summary>
        float SqrDistanceNN { get; set; }
        /// <summary>
        /// Reference to nearest neighbor.
        /// </summary>
        INodeContentNNS Nearest { get; set; }
    }


    /// <summary>
    /// Convenience base class for content objects.
    /// </summary>
    public class NodeContent : INodeContent
    {
        public virtual Bounds Bounds
        {
            get { return m_Bounds; }
            set { m_Bounds = value; }
        }
        protected Bounds m_Bounds;

        public virtual Vector3 Position
        {
            get { return m_Bounds.center; }
            set { m_Bounds.center = value; }
        }

        public NodeContent() { }

        public NodeContent(Bounds bounds)
        {
            Bounds = bounds;
        }

        public NodeContent(Vector3 position)
        {
            Position = position;
        }
    }



    /// <summary>
    /// Octree node. 
    /// Can only contain unique INodeContent instances. The tree will NOT grow or shrink, 
    /// its initial root node must be large enough for object positions you want to add later on. 
    /// It won't throw errors otherwise, you will need to check the Add/Remove methods' return 
    /// values, in order to see if any objects were ignored.
    /// Instantiate a single node as your tree root:
    /// <c>var tree = new MBaske.Octree.Node<NodeContentType>(treePosition, treeSize, minNodeSize);</c>
    /// </summary>
    public class Node<T> : IDisposable where T : INodeContent
    {
        /// <summary>
        /// Max. object count in splittable node.
        /// </summary>
        public static int MaxObjects = 8;
        /// <summary>
        /// Whether content objects can span multiple nodes. If true, an object 
        /// reference will be stored in all nodes that intersect the content bounds.
        /// Otherwise, the object will be stored in the node that contains its position.
        /// I find this more intuitive and also safer than using loose bounds, because
        /// even large objects spanning multiple nodes can be found reliably this way.
        /// </summary>
        public static bool MultiNodeObjects = false;

        /// <summary>
        /// Node contents, impl. INodeContent.
        /// </summary>
        public HashSet<T> Contents { get; private set; } = new HashSet<T>();
        /// <summary>
        /// Total number of content objects, incl. sub nodes.
        /// </summary>
        public int Count
        {
            get
            {
                if (MultiNodeObjects)
                {
                    s_AuxBuffer.Clear();
                    FindAll(s_AuxBuffer);
                    return s_AuxBuffer.Count;
                }
                else
                {
                    return CountContents(0);
                }
            }
        }
        /// <summary>
        /// Whether the node has any child nodes.
        /// </summary>
        public bool HasChildren => m_Children != null;
        /// <summary>
        /// Number of child nodes, 0 or 8. Used in for-loops, so we 
        /// don't have to check the existance of child nodes everytime.
        /// </summary>
        private int NumChildren => m_Children == null ? 0 : 8;
        /// <summary>
        /// The minimum node size.
        /// Node can't be split if resulting children would be any smaller.
        /// </summary>
        private readonly float m_MinSize;
        /// <summary>
        /// Whether the node can be split into child nodes.
        /// </summary>
        private readonly bool m_CanSplit;
        /// <summary>
        /// Child nodes.
        /// </summary>
        private Node<T>[] m_Children;
        /// <summary>
        /// Node bounds.
        /// </summary>
        private Bounds m_Bounds;
        /// <summary>
        /// Node depth, used for draw.
        /// </summary>
        private readonly int m_Depth;

        /// <summary>
        /// Shared auxiliary buffers.
        /// We assume Add/Remove/Find operations are not threaded.
        /// </summary>
        private static readonly HashSet<T> s_AuxBuffer = new HashSet<T>();
        private static readonly HashSet<T> s_StatsBuffer = new HashSet<T>();
        private static readonly List<T> s_NNSList = new List<T>();

        /// <summary>
        /// Creates a new octree node.
        /// </summary>
        /// <param name="position">Node position.</param>
        /// <param name="size">Node size.</param>
        /// <param name="minSize">Minimum node size.</param>
        /// <param name="depth">Node depth, set by method.</param>
        public Node(Vector3 position = default, float size = 64, float minSize = 1, int depth = 0)
        {
            m_Bounds = new Bounds(position, Vector3.one * size);
            m_CanSplit = m_Bounds.size.x >= minSize * 2;
            m_MinSize = minSize;
            m_Depth = depth;
        }

        /// <summary>
        /// Clears contents, removes child nodes.
        /// </summary>
        public void Dispose()
        {
            for (int i = 0, n = NumChildren; i < n; i++)
            {
                m_Children[i].Dispose();
            }
            Contents.Clear();
            m_Children = null;
        }

        /// <summary>
        /// Whether the node or its subnodes contain a specified content object.
        /// </summary>
        /// <param name="obj">Content object.</param>
        /// <param name="count">Number of found object references (out). 
        /// Can be > 1 if MultiNodeObjects = true.</param>
        /// <returns>true if object was found.</returns>
        public bool Contains(T obj, out int count)
        {
            count = Contains(obj);
            return count > 0;
        }

        #region Add Contents

        /// <summary>
        /// Tries to add a content object to the node, or to its sub nodes.
        /// </summary>
        /// <param name="obj">Content object.</param>
        /// <returns>true if content object was added.</returns>
        public bool Add(T obj)
        {
            return MultiNodeObjects ? AddMultiNode(obj) : AddSingleNode(obj);
        }

        /// <summary>
        /// Tries to add a content object to the node, or to its sub nodes.
        /// Updates nearest neighbor relationships for T impl. INodeContentNNS.
        /// </summary>
        /// <param name="obj">Content object.</param>
        /// <param name="distance">Nearest neighbor max. distance.</param>
        /// <param name="isSphere">Whether distance is sphere radius (true) or cube extent (false).</param>
        /// <returns>true if content object was added.</returns>
        public bool Add(T obj, float distance, bool isSphere = true)
        {
            if (Add(obj))
            {
                Debug.Assert(obj is INodeContentNNS,
                    "Content object does not implment INodeContentNNS.");
                ((INodeContentNNS)obj).SqrDistanceNN = Mathf.Infinity;
                UpdateNearestNeighbors(obj.Position, distance, isSphere);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Tries to add a content object to the node without recursion (used after splitting).
        /// </summary>
        /// <param name="obj">Content object.</param>
        /// <returns>true if content object was added.</returns>
        public bool FastAdd(T obj)
        {
            if (MultiNodeObjects
                ? m_Bounds.Intersects(obj.Bounds)
                : m_Bounds.Contains(obj.Position))
            {
                return Contents.Add(obj);
            }
            return false;
        }

        /// <summary>
        /// Tries to add a collection of content objects to this node, or to its sub nodes.
        /// </summary>
        /// <param name="collection">Content objects.</param>
        /// <returns>Number of added content objects.</returns>
        public int Add(IEnumerable<T> collection)
        {
            int count = 0;
            foreach (var obj in collection)
            {
                count += Add(obj) ? 1 : 0;
            }
            return count;
        }

        /// <summary>
        /// Tries to add a collection of content objects to this node, or to its sub nodes.
        /// Updates nearest neighbor relationships for T impl. INodeContentNNS.
        /// </summary>
        /// <param name="collection">Content objects.</param>
        /// <param name="distance">Nearest neighbor max. distance.</param>
        /// <param name="isSphere">Whether distance is sphere radius (true) or cube extent (false).</param>
        /// <returns>Number of added content objects.</returns>
        // TODO Might not update distant neighbors if MultiNodeObjects = true.
        public int Add(IEnumerable<T> collection, float distance, bool isSphere = true)
        {
            int count = 0;
            foreach (var obj in collection)
            {
                count += Add(obj, distance, isSphere) ? 1 : 0;
            }
            return count;
        }

        #endregion



        #region Remove Contents

        /// <summary>
        /// Tries to remove a content object from the node, or from its sub nodes.
        /// </summary>
        /// <param name="obj">Content object.</param>
        /// <returns>true if content object was removed.</returns>
        public bool Remove(T obj)
        {
            return MultiNodeObjects ? RemoveMultiNode(obj) : RemoveSingleNode(obj);
        }

        /// <summary>
        /// Tries to remove a content object from the node, or from its sub nodes.
        /// Updates nearest neighbor relationships for T impl. INodeContentNNS.
        /// </summary>
        /// <param name="obj">Content object.</param>
        /// <param name="distance">Nearest neighbor max. distance.</param>
        /// <param name="isSphere">Whether distance is sphere radius (true) or cube extent (false).</param>
        /// <returns>true if content object was removed.</returns>
        public bool Remove(T obj, float distance, bool isSphere = true)
        {
            if (Remove(obj))
            {
                Debug.Assert(obj is INodeContentNNS,
                    "Content object does not implment INodeContentNNS.");
                var objNNS = (INodeContentNNS)obj;
                if (objNNS.Nearest != null && objNNS.Nearest.Nearest == objNNS)
                {
                    objNNS.Nearest.SqrDistanceNN = Mathf.Infinity;
                    objNNS.Nearest.Nearest = null;
                }
                objNNS.Nearest = null;
                UpdateNearestNeighbors(obj.Position, distance, isSphere);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Tries to remove a content object from the node, or from its sub nodes.
        /// </summary>
        /// <param name="collection">Content objects.</param>
        /// <returns>Number of removed content objects.</returns>
        public int Remove(IEnumerable<T> collection)
        {
            int count = 0;
            foreach (var obj in collection)
            {
                count += Remove(obj) ? 1 : 0;
            }
            return count;
        }

        /// <summary>
        /// Tries to remove a collection of content objects from this node, or from its sub nodes.
        /// Updates nearest neighbor relationships for T impl. INodeContentNNS.
        /// </summary>
        /// <param name="collection">Content objects.</param>
        /// <param name="distance">Nearest neighbor max. distance.</param>
        /// <param name="isSphere">Whether distance is sphere radius (true) or cube extent (false).</param>
        /// <returns>Number of removed content objects.</returns>
        // TODO Might not update distant neighbors if MultiNodeObjects = true.
        public int Remove(IEnumerable<T> collection, float distance, bool isSphere = true)
        {
            int count = 0;
            foreach (var obj in collection)
            {
                count += Remove(obj, distance, isSphere) ? 1 : 0;
            }
            return count;
        }

        #endregion



        #region Find Contents

        // All Find methods are depth-first and non-allocating.
        // An empty result HashSet<INodeContent> must be provided.


        /// <summary>
        /// Finds all content objects.
        /// </summary>
        /// <param name="result">Buffer for search result.</param>
        /// <returns>true if any objects were found.</returns>
        public bool FindAll(HashSet<T> result)
        {
            for (int i = 0, n = NumChildren; i < n; i++)
            {
                m_Children[i].FindAll(result);
            }

            result.UnionWith(Contents);

            return result.Count > 0;
        }


        #region Bounds intersect search space

        /// <summary>
        /// Finds all content objects whose bounds intersect specified search bounds.
        /// </summary>
        /// <param name="result">Buffer for search result.</param>
        /// <param name="bounds">Bounds to check against.</param>
        /// <returns>true if any objects were found.</returns>
        public bool FindBoundsIntersectBounds(HashSet<T> result, Bounds bounds)
        {
            if (!m_Bounds.Intersects(bounds))
            {
                return false;
            }

            if (bounds.Contains(m_Bounds))
            {
                // Node is fully contained in search bounds.
                // All of its contents are therefore intersecting.

                FindAll(result);
                return result.Count > 0;
            }

            for (int i = 0, n = NumChildren; i < n; i++)
            {
                m_Children[i].FindBoundsIntersectBounds(result, bounds);
            }

            foreach (var obj in Contents)
            {
                if (obj.Bounds.Intersects(bounds))
                {
                    result.Add(obj);
                }
            }

            return result.Count > 0;
        }

        /// <summary>
        /// Finds all content objects whose bounds intersect a search sphere 
        /// with a specified center and radius.
        /// </summary>
        /// <param name="result">Buffer for search result.</param>
        /// <param name="center">Sphere center.</param>
        /// <param name="radius">Sphere radius.</param>
        /// <returns>true if any objects were found.</returns>
        public bool FindBoundsIntersectSphere(HashSet<T> result, Vector3 center, float radius)
        {
            float sqrRadius = radius * radius;

            if (!m_Bounds.IntersectSphere(center, sqrRadius))
            {
                return false;
            }

            if (m_Bounds.IsInsideSphere(center, sqrRadius))
            {
                // Node is fully contained in search sphere.
                // All of its contents are therefore intersecting.

                FindAll(result);
                return result.Count > 0;
            }

            for (int i = 0, n = NumChildren; i < n; i++)
            {
                m_Children[i].FindBoundsIntersectSphere(result, center, radius);
            }

            foreach (var obj in Contents)
            {
                if (obj.Bounds.IntersectSphere(center, sqrRadius))
                {
                    result.Add(obj);
                }
            }

            return result.Count > 0;
        }

        /// <summary>
        /// Finds all content objects whose bounds intersect a specified ray.
        /// </summary>
        /// <param name="result">Buffer for search result.</param>
        /// <param name="ray">Ray to check against.</param>
        /// <param name="radius">Ray radius (spherecast).</param>
        /// <param name="length">Ray length.</param>
        /// <returns>true if any objects were found.</returns>
        public bool FindBoundsIntersectRay(HashSet<T> result, Ray ray, float radius = 0, float length = Mathf.Infinity)
        {
            if (!m_Bounds.IntersectRay(ray, radius, length))
            {
                return false;
            }

            if (radius > 0 && m_Bounds.IsInsideRay(ray, radius * radius, length))
            {
                // Node is fully contained in search ray.
                // All of its contents are therefore intersecting.

                FindAll(result);
                return result.Count > 0;
            }

            for (int i = 0, n = NumChildren; i < n; i++)
            {
                m_Children[i].FindBoundsIntersectRay(result, ray, radius, length);
            }

            foreach (var obj in Contents)
            {
                if (obj.Bounds.IntersectRay(ray, radius, length))
                {
                    result.Add(obj);
                }
            }

            return result.Count > 0;
        }

        /// <summary>
        /// Finds all content objects whose bounds intersect a specified frustum.
        /// </summary>
        /// <param name="result">Buffer for search result.</param>
        /// <param name="camera">Camera that provides frustum.</param>
        /// <returns>true if any objects were found.</returns>
        public bool FindBoundsIntersectFrustum(HashSet<T> result, Camera camera)
        {
            return FindBoundsIntersectFrustum(result, GeometryUtility.CalculateFrustumPlanes(camera));
        }

        /// <summary>
        /// Finds all content objects whose bounds intersect a specified frustum.
        /// </summary>
        /// <param name="result">Buffer for search result.</param>
        /// <param name="planes">Frustum planes.</param>
        /// <returns>true if any objects were found.</returns>
        public bool FindBoundsIntersectFrustum(HashSet<T> result, Plane[] planes)
        {
            if (!GeometryUtility.TestPlanesAABB(planes, m_Bounds))
            {
                return false;
            }

            for (int i = 0, n = NumChildren; i < n; i++)
            {
                m_Children[i].FindBoundsIntersectFrustum(result, planes);
            }

            foreach (var obj in Contents)
            {
                if (GeometryUtility.TestPlanesAABB(planes, obj.Bounds))
                {
                    result.Add(obj);
                }
            }

            return result.Count > 0;
        }

        #endregion


        #region Bounds or positions inside search space

        /// <summary>
        /// Finds all content objects whose bounds are inside specified search bounds.
        /// </summary>
        /// <param name="result">Buffer for search result.</param>
        /// <param name="bounds">Bounds to check against.</param>
        /// <returns>true if any objects were found.</returns>
        public bool FindBoundsInsideBounds(HashSet<T> result, Bounds bounds)
        {
            return FindInsideBounds(result, bounds, false);
        }

        /// <summary>
        /// Finds all content objects whose positions are inside specified search bounds.
        /// </summary>
        /// <param name="result">Buffer for search result.</param>
        /// <param name="bounds">Bounds to check against.</param>
        /// <returns>true if any objects were found.</returns>
        public bool FindPositionsInsideBounds(HashSet<T> result, Bounds bounds)
        {
            return FindInsideBounds(result, bounds, true);
        }

        /// <summary>
        /// Finds all content objects whose bounds or positions are inside specified search bounds.
        /// </summary>
        /// <param name="result">Buffer for search result.</param>
        /// <param name="bounds">Bounds to check against.</param>
        /// <param name="positions">Whether to check object positions (true) or bounds (false).</param>
        /// <returns>true if any objects were found.</returns>
        public bool FindInsideBounds(HashSet<T> result, Bounds bounds, bool positions)
        {
            if (!m_Bounds.Intersects(bounds))
            {
                return false;
            }

            if (bounds.Contains(m_Bounds))
            {
                // Node is fully contained in search bounds.
                // Check all of its contents right away.

                s_AuxBuffer.Clear();
                FindAll(s_AuxBuffer);

                foreach (var obj in s_AuxBuffer)
                {
                    if (positions
                        ? bounds.Contains(obj.Position)
                        : bounds.Contains(obj.Bounds))
                    {
                        result.Add(obj);
                    }
                }

                return result.Count > 0;
            }

            for (int i = 0, n = NumChildren; i < n; i++)
            {
                m_Children[i].FindInsideBounds(result, bounds, positions);
            }

            foreach (var obj in Contents)
            {
                if (positions
                    ? bounds.Contains(obj.Position)
                    : bounds.Contains(obj.Bounds))
                {
                    result.Add(obj);
                }
            }

            return result.Count > 0;
        }


        /// <summary>
        /// Finds all content objects whose bounds are inside a search sphere 
        /// with a specified center and radius.
        /// </summary>
        /// <param name="result">Buffer for search result.</param>
        /// <param name="center">Sphere center.</param>
        /// <param name="radius">Sphere radius.</param>
        /// <returns>true if any objects were found.</returns>
        public bool FindBoundsInsideSphere(HashSet<T> result, Vector3 center, float radius)
        {
            return FindInsideSphere(result, center, radius, false);
        }

        /// <summary>
        /// Finds all content objects whose positions are inside a search sphere 
        /// with a specified center and radius. Same as FindPositionsNearby.
        /// </summary>
        /// <param name="result">Buffer for search result.</param>
        /// <param name="center">Sphere center.</param>
        /// <param name="radius">Sphere radius.</param>
        /// <returns>true if any objects were found.</returns>
        public bool FindPositionsInsideSphere(HashSet<T> result, Vector3 center, float radius)
        {
            return FindInsideSphere(result, center, radius, true);
        }

        /// <summary>
        /// Finds all content objects whose positions are within a specified 
        /// distance from a specified point. Same as FindPositionsInsideSphere.
        /// </summary>
        /// <param name="result">Buffer for search result.</param>
        /// <param name="point">Point to check against.</param>
        /// <param name="distance">Max. distance from point.</param>
        /// <returns>true if any objects were found.</returns>
        public bool FindPositionsNearby(HashSet<T> result, Vector3 point, float distance)
        {
            return FindInsideSphere(result, point, distance, true);
        }

        /// <summary>
        /// Finds all content objects whose bounds or positions are inside 
        /// a search sphere with a specified center and radius.
        /// </summary>
        /// <param name="result">Buffer for search result.</param>
        /// <param name="center">Sphere center.</param>
        /// <param name="radius">Sphere radius.</param>
        /// <param name="positions">Whether to check object positions (true) or bounds (false).</param>
        /// <returns>true if any objects were found.</returns>
        public bool FindInsideSphere(HashSet<T> result, Vector3 center, float radius, bool positions)
        {
            float sqrRadius = radius * radius;

            if (!m_Bounds.IntersectSphere(center, sqrRadius))
            {
                return false;
            }

            if (m_Bounds.IsInsideSphere(center, sqrRadius))
            {
                // Node is fully contained in search sphere.
                // Check all of its contents right away.

                s_AuxBuffer.Clear();
                FindAll(s_AuxBuffer);

                foreach (var obj in s_AuxBuffer)
                {
                    if (positions
                        ? obj.Position.IsInsideSphere(center, sqrRadius)
                        : obj.Bounds.IsInsideSphere(center, sqrRadius))
                    {
                        result.Add(obj);
                    }
                }

                return result.Count > 0;
            }

            for (int i = 0, n = NumChildren; i < n; i++)
            {
                m_Children[i].FindInsideSphere(result, center, radius, positions);
            }

            foreach (var obj in Contents)
            {
                if (positions
                    ? obj.Position.IsInsideSphere(center, sqrRadius)
                    : obj.Bounds.IsInsideSphere(center, sqrRadius))
                {
                    result.Add(obj);
                }
            }

            return result.Count > 0;
        }


        /// <summary>
        /// Finds all content objects whose bounds are inside a specified ray.
        /// </summary>
        /// <param name="result">Buffer for search result.</param>
        /// <param name="ray">Ray to check against.</param>
        /// <param name="radius">Ray radius (spherecast).</param>
        /// <param name="length">Ray length.</param>
        /// <returns>true if any objects were found.</returns>
        public bool FindBoundsInsideRay(HashSet<T> result, Ray ray, float radius, float length)
        {
            return FindInsideRay(result, ray, radius, length, false);
        }

        /// <summary>
        /// Finds all content objects whose positions are inside a specified ray.
        /// </summary>
        /// <param name="result">Buffer for search result.</param>
        /// <param name="ray">Ray to check against.</param>
        /// <param name="radius">Ray radius (spherecast).</param>
        /// <param name="length">Ray length.</param>
        /// <returns>true if any objects were found.</returns>
        public bool FindPositionsInsideRay(HashSet<T> result, Ray ray, float radius, float length)
        {
            return FindInsideRay(result, ray, radius, length, true);
        }

        /// <summary>
        /// Finds all content objects whose bounds or positions are inside a specified ray.
        /// </summary>
        /// <param name="result">Buffer for search result.</param>
        /// <param name="ray">Ray to check against.</param>
        /// <param name="radius">Ray radius (spherecast, required).</param>
        /// <param name="length">Ray length.</param>
        /// <param name="positions">Whether to check object positions (true) or bounds (false).</param>
        /// <returns>true if any objects were found.</returns>
        public bool FindInsideRay(HashSet<T> result, Ray ray, float radius, float length, bool positions)
        {
            Debug.Assert(radius > 0, "Spherecast requires radius > 0");

            if (!m_Bounds.IntersectRay(ray, radius, length))
            {
                return false;
            }

            float sqrRadius = radius * radius;

            if (m_Bounds.IsInsideRay(ray, sqrRadius, length))
            {
                // Node is fully contained in search ray.
                // Check all of its contents right away.

                s_AuxBuffer.Clear();
                FindAll(s_AuxBuffer);

                foreach (var obj in s_AuxBuffer)
                {
                    if (positions
                        ? obj.Position.IsInsideRay(ray, sqrRadius, length)
                        : obj.Bounds.IsInsideRay(ray, sqrRadius, length))
                    {
                        result.Add(obj);
                    }
                }

                return result.Count > 0;
            }

            for (int i = 0, n = NumChildren; i < n; i++)
            {
                m_Children[i].FindInsideRay(result, ray, radius, length, positions);
            }

            foreach (var obj in Contents)
            {
                if (positions
                    ? obj.Position.IsInsideRay(ray, sqrRadius, length)
                    : obj.Bounds.IsInsideRay(ray, sqrRadius, length))
                {
                    result.Add(obj);
                }
            }

            return result.Count > 0;
        }

        #endregion


        /// <summary>
        /// Finds all content objects whose bounds contain a specified point.
        /// </summary>
        /// <param name="result">Buffer for search result.</param>
        /// <param name="point">Point to check against.</param>
        /// <returns>true if any objects were found.</returns>
        public bool FindContainsPoint(HashSet<T> result, Vector3 point)
        {
            if (!m_Bounds.Contains(point))
            {
                return false;
            }

            for (int i = 0, n = NumChildren; i < n; i++)
            {
                m_Children[i].FindContainsPoint(result, point);
            }

            foreach (var obj in Contents)
            {
                if (obj.Bounds.Contains(point))
                {
                    result.Add(obj);
                }
            }

            return result.Count > 0;
        }

        #endregion



        #region Gizmo-Draw

        /// <summary>
        /// Gizmo-draws the node's bounds.
        /// </summary>
        /// <param name="contentsOnly">Whether to only draw inf node contains content objects.</param>
        public void DrawNodeBounds(bool contentsOnly = true)
        {
            for (int i = 0, n = NumChildren; i < n; i++)
            {
                m_Children[i].DrawNodeBounds(contentsOnly);
            }

            if (!contentsOnly || Contents.Count > 0)
            {
                Gizmos.DrawWireCube(m_Bounds.center, m_Bounds.size);
            }
        }

        /// <summary>
        /// Gizmo-draws the node's bounds.
        /// </summary>
        /// <param name="colors">Color array for depth levels.</param>
        /// <param name="contentsAplha">Whether to apply alpha proportional to content count.</param>
        public void DrawNodeBounds(Color[] colors, bool contentsAplha = true)
        {
            for (int i = 0, n = NumChildren; i < n; i++)
            {
                m_Children[i].DrawNodeBounds(colors, contentsAplha);
            }

            if (Contents.Count > 0)
            {
                Color color = colors[Mathf.Min(colors.Length - 1, m_Depth)];
                if (contentsAplha)
                {
                    color.a = Mathf.Clamp01(Contents.Count / (float)MaxObjects) * 0.75f + 0.25f;
                }
                Gizmos.color = color;
                Gizmos.DrawWireCube(m_Bounds.center, m_Bounds.size);
            }
        }

        /// <summary>
        /// Gizmo-draws the contents' bounds.
        /// </summary>
        public void DrawContentBounds()
        {
            for (int i = 0, n = NumChildren; i < n; i++)
            {
                m_Children[i].DrawContentBounds();
            }

            foreach (var obj in Contents)
            {
                Gizmos.DrawWireCube(obj.Bounds.center, obj.Bounds.size);
            }
        }

        /// <summary>
        /// Gizmo-draws the contents' positions.
        /// </summary>
        /// <param name="radius">Sphere radius.</param>
        public void DrawContentPositions(float radius = 0.1f)
        {
            for (int i = 0, n = NumChildren; i < n; i++)
            {
                m_Children[i].DrawContentPositions(radius);
            }

            foreach (var obj in Contents)
            {
                Gizmos.DrawSphere(obj.Position, radius);
            }
        }

        #endregion

        /// <summary>
        /// Returns a string representation of the node and its subnodes.
        /// </summary>
        /// <returns>String.</returns>
        public override string ToString()
        {
            var sb = new StringBuilder();
            ParseToString(sb);
            return sb.ToString();
        }



        #region Private

        /// <summary>
        /// Writes node info to StringBuilder instance.
        /// </summary>
        /// <param name="sb">StringBuilder.</param>
        /// <param name="index">Node index.</param>
        private void ParseToString(StringBuilder sb, int index = 0)
        {
            sb.Append("\n" + string.Concat(Enumerable.Repeat("\t", m_Depth))
                + m_Depth + "/" + index + "\t");

            if (HasChildren)
            {
                for (int i = 0; i < 8; i++)
                {
                    m_Children[i].ParseToString(sb, i);
                }
            }
            else
            {
                sb.Append(Contents.Count > 0
                    ? string.Join(", ", Contents.ToArray())
                    : "-");
            }
        }

        /// <summary>
        /// Tries to add a content object to the node, or to its sub nodes.
        /// Object reference is stored in a single node, according to its position.
        /// </summary>
        /// <param name="obj">Content object.</param>
        /// <returns>true if content object was added.</returns>
        private bool AddSingleNode(T obj)
        {
            if (m_Bounds.Contains(obj.Position))
            {
                if (HasChildren)
                {
                    for (int i = 0; i < 8; i++)
                    {
                        if (m_Children[i].AddSingleNode(obj))
                        {
                            return true;
                        }
                    }
                }
                else
                {
                    bool added = Contents.Add(obj);
                    if (added)
                    {
                        TrySplit();
                    }
                    return added;
                }
            }

            return false;
        }

        /// <summary>
        /// Tries to add a content object to the node, or to its sub nodes.
        /// Object reference can be stored in multiple nodes, depending on bounds.
        /// </summary>
        /// <param name="obj">Content object.</param>
        /// <returns>true if content object was added.</returns>
        private bool AddMultiNode(T obj)
        {
            if (m_Bounds.Intersects(obj.Bounds))
            {
                if (HasChildren)
                {
                    bool added = false;
                    for (int i = 0; i < 8; i++)
                    {
                        added = m_Children[i].AddMultiNode(obj) || added;
                    }
                    return added;
                }
                else
                {
                    bool added = Contents.Add(obj);
                    if (added)
                    {
                        TrySplit();
                    }
                    return added;
                }
            }

            return false;
        }

        /// <summary>
        /// Tries to remove a content object from the node, or from its sub nodes.
        /// Object reference is stored in a single node, according to its position.
        /// </summary>
        /// <param name="obj">Content object.</param>
        /// <returns>true if content object was removed.</returns>
        private bool RemoveSingleNode(T obj)
        {
            if (m_Bounds.Contains(obj.Position))
            {
                if (HasChildren)
                {
                    bool removed = false;
                    for (int i = 0; i < 8; i++)
                    {
                        if (m_Children[i].RemoveSingleNode(obj))
                        {
                            removed = true;
                            break;
                        }
                    }
                    if (removed)
                    {
                        TryMerge();
                        return true;
                    }
                }
                else
                {
                    return Contents.Remove(obj);
                }
            }

            return false;
        }

        /// <summary>
        /// Tries to remove a content object from the node, or from its sub nodes.
        /// Object reference can be stored in multiple nodes, depending on bounds.
        /// </summary>
        /// <param name="obj">Content object.</param>
        /// <returns>true if content object was removed.</returns>
        private bool RemoveMultiNode(T obj)
        {
            if (m_Bounds.Intersects(obj.Bounds))
            {
                if (HasChildren)
                {
                    bool removed = false;
                    for (int i = 0; i < 8; i++)
                    {
                        removed = m_Children[i].RemoveMultiNode(obj) || removed;
                    }
                    if (removed)
                    {
                        TryMerge();
                        return true;
                    }
                }
                else
                {
                    return Contents.Remove(obj);
                }
            }

            return false;
        }

        /// <summary>
        /// Creates eight child nodes if possible and re-assigns parent node contents.
        /// </summary>
        /// <returns>true is node was split.</returns>
        private bool TrySplit()
        {
            if (!m_CanSplit || Contents.Count <= MaxObjects)
            {
                return false;
            }

            Vector3 p = m_Bounds.center;
            float size = m_Bounds.extents.x;
            float offset = size * 0.5f;
            int depth = m_Depth + 1;

            m_Children = new Node<T>[]
            {
                new Node<T>(p + new Vector3(-offset, -offset, -offset), size, m_MinSize, depth),
                new Node<T>(p + new Vector3( offset, -offset, -offset), size, m_MinSize, depth),
                new Node<T>(p + new Vector3(-offset,  offset, -offset), size, m_MinSize, depth),
                new Node<T>(p + new Vector3( offset,  offset, -offset), size, m_MinSize, depth),
                new Node<T>(p + new Vector3(-offset, -offset,  offset), size, m_MinSize, depth),
                new Node<T>(p + new Vector3( offset, -offset,  offset), size, m_MinSize, depth),
                new Node<T>(p + new Vector3(-offset,  offset,  offset), size, m_MinSize, depth),
                new Node<T>(p + new Vector3( offset,  offset,  offset), size, m_MinSize, depth)
            };

            // TODO Maybe swap loops and remove added objects? 
            foreach (var obj in Contents)
            {
                for (int i = 0; i < 8; i++)
                {
                    m_Children[i].FastAdd(obj);
                }
            }

            Contents.Clear();

            return true;
        }

        /// <summary>
        /// Merges child nodes if possible.
        /// </summary>
        /// <returns>true if children were merged.</returns>
        private bool TryMerge()
        {
            if (CanMerge())
            {
                for (int i = 0; i < 8; i++)
                {
                    Contents.UnionWith(m_Children[i].Contents);
                    m_Children[i].Dispose();
                }
                m_Children = null;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Whether child nodes can be merged.
        /// </summary>
        /// <returns>true if children can be merged.</returns>
        private bool CanMerge()
        {
            if (HasChildren)
            {
                int count = 0;
                for (int i = 0; i < 8; i++)
                {
                    if (m_Children[i].HasChildren)
                    {
                        return false;
                    }
                    count += m_Children[i].Contents.Count;
                }
                return count <= MaxObjects;
            }

            return false;
        }

        /// <summary>
        /// Updates nearest neighbor relationships.
        /// </summary>
        /// <param name="position">Center position.</param>
        /// <param name="distance">Nearest neighbor max. distance.</param>
        /// <param name="isSphere">Whether distance is sphere radius (true) or cube extent (false).</param>
        private void UpdateNearestNeighbors(Vector3 position, float distance, bool isSphere = true)
        {
            s_StatsBuffer.Clear();
            if (isSphere)
            {
                FindInsideSphere(s_StatsBuffer, position, distance, true);
            }
            else
            {
                FindInsideBounds(s_StatsBuffer, new Bounds(position, Vector3.one * distance * 2), true);
            }

            int n = s_StatsBuffer.Count;
            if (n > 1)
            {
                s_NNSList.Clear();
                s_NNSList.AddRange(s_StatsBuffer);

                for (int i = 0; i < n; i++)
                {
                    var objI = (INodeContentNNS)s_NNSList[i];
                    Vector3 pos = objI.Position;

                    for (int j = i + 1; j < n; j++)
                    {
                        var objJ = (INodeContentNNS)s_NNSList[j];
                        float d = (objJ.Position - pos).sqrMagnitude;

                        if (d < objI.SqrDistanceNN)
                        {
                            objI.SqrDistanceNN = d;
                            objI.Nearest = objJ;
                        }

                        if (d < objJ.SqrDistanceNN)
                        {
                            objJ.SqrDistanceNN = d;
                            objJ.Nearest = objI;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Counts all content objects.
        /// Can include duplicate references if MultiNodeObjects = true.
        /// To get a unique object count, you can use FindAll and count the buffer items.
        /// </summary>
        /// <param name="count">Count value.</param>
        /// <returns>Total object count.</returns>
        private int CountContents(int count = 0)
        {
            if (HasChildren)
            {
                for (int i = 0; i < 8; i++)
                {
                    count = m_Children[i].CountContents(count);
                }
            }
            else
            {
                count += Contents.Count;
            }
            return count;
        }

        /// <summary>
        /// Counts the stored references for specified content object.
        /// Can be > 1 if MultiNodeObjects = true.
        /// </summary>
        /// <param name="obj">Content object.</param>
        /// <returns>Number of found references.</returns>
        private int Contains(T obj, int count = 0)
        {
            if (HasChildren)
            {
                for (int i = 0; i < 8; i++)
                {
                    count = m_Children[i].Contains(obj, count);
                }
            }
            else
            {
                count += Contents.Contains(obj) ? 1 : 0;
            }

            return count;
        }

        #endregion
    }


    /// <summary>
    /// Bounds and Vector3 extension methods.
    /// </summary>
    public static class OctreeExtensions
    {
        #region Bounds

        /// <summary>
        /// Whether bounds intersect a specified sphere.
        /// </summary>
        /// <param name="bounds">Bounds.</param>
        /// <param name="center">Sphere center.</param>
        /// <param name="sqrRadius">Sphere square radius.</param>
        /// <returns>true if intersecting sphere.</returns>
        public static bool IntersectSphere(this Bounds bounds, Vector3 center, float sqrRadius)
        {
            return bounds.Contains(center) || center.SqrDistanceTo(bounds) <= sqrRadius;
        }

        /// <summary>
        /// Whether bounds intersect a specified ray.
        /// </summary>
        /// <param name="bounds">Bounds.</param>
        /// <param name="ray">The ray to check against.</param>
        /// <param name="radius">Ray radius (spherecast, optional).</param>
        /// <param name="length">Ray length.</param>
        /// <returns>true if intersecting ray.</returns>
        // NOTE We're passing radius here, NOT square radius.
        public static bool IntersectRay(this Bounds bounds, Ray ray, float radius, float length)
        {
            if (radius > 0)
            {
                Bounds exp = bounds;
                exp.Expand(radius * 2);

                if (exp.IntersectRay(ray))
                {
                    float halfLength = length * 0.5f;
                    Vector3 midPoint = ray.origin + ray.direction * halfLength;
                    return bounds.ClosestPoint(midPoint).IsInsideRay(ray, radius * radius, halfLength, midPoint);
                }
            }
            else
            {
                return bounds.IntersectRay(ray, out float distance) && distance <= length;
            }

            return false;
        }

        /// <summary>
        /// Whether bounds contain other specified bounds.
        /// </summary>
        /// <param name="bounds">Bounds.</param>
        /// <param name="other">Bounds to check against.</param>
        /// <returns>true if containing other bounds.</returns>
        public static bool Contains(this Bounds bounds, Bounds other)
        {
            return bounds.Contains(other.min) && bounds.Contains(other.max);
        }

        /// <summary>
        /// Whether bounds are contained inside other specified bounds.
        /// Inverse of bounds.Contains(other bounds)
        /// </summary>
        /// <param name="bounds">Bounds.</param>
        /// <param name="other">Bounds to check against.</param>
        /// <returns>true if being contained in other bounds.</returns>
        public static bool IsInsideBounds(this Bounds bounds, Bounds other)
        {
            return other.Contains(bounds.min) && other.Contains(bounds.max);
        }

        /// <summary>
        /// Whether bounds are contained inside specified sphere.
        /// </summary>
        /// <param name="bounds">Bounds.</param>
        /// <param name="center">Sphere center.</param>
        /// <param name="sqrRadius">Sphere square radius.</param>
        /// <returns>true if being contained in sphere.</returns>
        public static bool IsInsideSphere(this Bounds bounds, Vector3 center, float sqrRadius)
        {
            bool inside = true;
            Vector3[] points = bounds.Corners();

            for (int i = 0; i < 8; i++)
            {
                inside = inside && points[i].IsInsideSphere(center, sqrRadius);
            }
            return inside;
        }

        /// <summary>
        /// Whether bounds are contained inside specified ray (spherecast).
        /// </summary>
        /// <param name="bounds">Bounds.</param>
        /// <param name="ray">Ray to check against.</param>
        /// <param name="sqrRadius">Ray square radius (spherecast, required).</param>
        /// <param name="sqrLength">Ray length.</param>
        /// <returns>true if being contained in ray.</returns>
        public static bool IsInsideRay(this Bounds bounds, Ray ray, float sqrRadius, float length)
        {
            bool inside = true;
            float halfLength = length * 0.5f;
            Vector3 midPoint = ray.origin + ray.direction * halfLength;
            Vector3[] points = bounds.Corners();

            for (int i = 0; i < 8; i++)
            {
                inside = inside && points[i].IsInsideRay(ray, sqrRadius, halfLength, midPoint);
            }
            return inside;
        }

        /// <summary>
        /// Returns the bounds corner vertices.
        /// </summary>
        /// <param name="bounds">Bounds.</param>
        /// <returns>Corner vertices<./returns>
        private static Vector3[] Corners(this Bounds bounds)
        {
            return new Vector3[]
            {
                bounds.min,
                new Vector3(bounds.max.x, bounds.min.y, bounds.min.z),
                new Vector3(bounds.min.x, bounds.max.y, bounds.min.z),
                new Vector3(bounds.max.x, bounds.max.y, bounds.min.z),
                new Vector3(bounds.min.x, bounds.min.y, bounds.max.z),
                new Vector3(bounds.max.x, bounds.min.y, bounds.max.z),
                new Vector3(bounds.min.x, bounds.max.y, bounds.max.z),
                bounds.max
            };
        }

        #endregion


        #region Vector3

        /// <summary>
        /// Whether point is contained inside specified sphere.
        /// </summary>
        /// <param name="point">Point.</param>
        /// <param name="center">Sphere center.</param>
        /// <param name="sqrRadius">Sphere square radius.</param>
        /// <returns>true if being contained in sphere.</returns>
        public static bool IsInsideSphere(this Vector3 point, Vector3 center, float sqrRadius)
        {
            return (point - center).sqrMagnitude <= sqrRadius;
        }

        // TODO Simpify inside ray / spherecast checks.

        /// <summary>
        /// Whether point is contained inside specified ray (spherecast).
        /// </summary>
        /// <param name="point">Point.</param>
        /// <param name="ray">Ray to check against.</param>
        /// <param name="sqrRadius">Ray square radius (spherecast, required).</param>
        /// <param name="length">Ray length.</param>
        /// <returns>true if being contained in ray.</returns>
        public static bool IsInsideRay(this Vector3 point, Ray ray, float sqrRadius, float length)
        {
            float halfLength = length * 0.5f;
            return point.IsInsideRay(ray, sqrRadius, halfLength, ray.origin + ray.direction * halfLength);
        }

        /// <summary>
        /// Whether point is contained inside specified ray (spherecast).
        /// </summary>
        /// <param name="point">Point.</param>
        /// <param name="ray">Ray to check against.</param>
        /// <param name="sqrRadius">Ray square radius (spherecast, required).</param>
        /// <param name="halfLength">Ray half length.</param>
        /// <param name="midPoint">Ray mid point.</param>
        /// <returns>true if being contained in ray.</returns>
        public static bool IsInsideRay(this Vector3 point, Ray ray, float sqrRadius, float halfLength, Vector3 midPoint)
        {
            return point.SqrDistanceTo(ray) <= sqrRadius && (point.Project(ray) - midPoint).magnitude <= halfLength;
        }

        /// <summary>
        /// Projects a point onto specified ray.
        /// </summary>
        /// <param name="point">Point.</param>
        /// <param name="ray">Ray to project onto.</param>
        /// <returns>Point on ray.</returns>
        public static Vector3 Project(this Vector3 point, Ray ray)
        {
            return ray.origin + ray.direction * Vector3.Dot(ray.direction, point - ray.origin);
        }

        /// <summary>
        /// Returns square distance between point and specified ray.
        /// </summary>
        /// <param name="point">Point.</param>
        /// <param name="ray">Ray to measure against.</param>
        /// <returns>Square distance to ray.</returns>
        public static float SqrDistanceTo(this Vector3 point, Ray ray)
        {
            return Vector3.Cross(ray.direction, point - ray.origin).sqrMagnitude;
        }

        /// <summary>
        /// Returns square distance between point and specified bounds.
        /// </summary>
        /// <param name="point">Point.</param>
        /// <param name="bounds">Bounds to measure against.</param>
        /// <returns>Square distance to bounds.</returns>
        public static float SqrDistanceTo(this Vector3 point, Bounds bounds)
        {
            return (bounds.ClosestPoint(point) - point).sqrMagnitude;
        }

        /// <summary>
        /// Returns square distance between point and specified other point.
        /// </summary>
        /// <param name="point">Point.</param>
        /// <param name="other">Point to measure against.</param>
        /// <returns>Square distance to bounds.</returns>
        public static float SqrDistanceTo(this Vector3 point, Vector3 other)
        {
            return (other - point).sqrMagnitude;
        }

        #endregion
    }
}