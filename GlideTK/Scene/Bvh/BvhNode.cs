namespace GlideTK.Scene.Bvh;

/// <summary>
/// Node of the bounding volume hierarchy: an inner node has two children, a leaf references one item
/// </summary>
public sealed class BvhNode
{
    public Aabb Bounds;
    public int Left = -1;
    public int Right = -1;
    public int Item = -1;

    public bool IsLeaf => Item >= 0;
}
