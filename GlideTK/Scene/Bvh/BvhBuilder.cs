using System;
using System.Collections.Generic;
using System.Linq;

namespace GlideTK.Scene.Bvh;

/// <summary>
/// Builds a binary BVH over finite boxes: median split along the longest axis of the centroids.
/// Nodes are stored depth first, the root is node 0, one item per leaf.
/// </summary>
public static class BvhBuilder
{
    public static List<BvhNode> Build(IReadOnlyList<Aabb> items, out int depth)
    {
        var nodes = new List<BvhNode>();
        depth = 0;

        if (items.Count > 0)
        {
            int[] order = [.. Enumerable.Range(0, items.Count)];
            BuildNode(nodes, items, order, 0, items.Count, 1, ref depth);
        }

        return nodes;
    }

    static int BuildNode(
        List<BvhNode> nodes,
        IReadOnlyList<Aabb> items,
        int[] order,
        int from,
        int to,
        int level,
        ref int depth)
    {
        int index = nodes.Count;
        var node = new BvhNode();
        nodes.Add(node);
        depth = Math.Max(depth, level);

        Aabb bounds = items[order[from]];
        Aabb centroids = new(bounds.Center, bounds.Center);
        for (int i = from + 1; i < to; i++)
        {
            bounds = bounds.Union(items[order[i]]);
            centroids = centroids.Union(
                new Aabb(items[order[i]].Center, items[order[i]].Center));
        }
        node.Bounds = bounds;

        if (to - from == 1)
        {
            node.Item = order[from];
            return index;
        }

        OpenTK.Mathematics.Vector3 extent = centroids.Max - centroids.Min;
        int axis = 
            extent.X >= extent.Y 
            && extent.X >= extent.Z ? 0 
                : extent.Y >= extent.Z ? 1 : 2;

        int[] range = order[from..to];
        float[] keys = Array.ConvertAll(range, i => items[i].Center[axis]);
        Array.Sort(keys, range);
        range.CopyTo(order, from);

        int mid = (from + to) / 2;
        node.Left = BuildNode(nodes, items, order, from, mid, level + 1, ref depth);
        node.Right = BuildNode(nodes, items, order, mid, to, level + 1, ref depth);

        return index;
    }
}
