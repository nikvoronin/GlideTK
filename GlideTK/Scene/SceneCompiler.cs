using GlideTK.Scene.Bvh;
using GlideTK.Scene.Defs;
using GlideTK.Scene.Expr;
using GlideTK.Scene.Glsl;
using GlideTK.Scene.Nodes;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GlideTK.Scene;


/// <summary>
/// Compiles the scene tree, with all expressions evaluated at time t, into the program
/// described in <see cref="SceneOpcodes"/>: the scene is split into leaves (separate
/// stack programs united at the top level), finite leaves are put into a BVH.
/// </summary>
public class SceneCompiler
{
    static readonly Vector3 DEFAULT_COLOR = new(1f);
    const int MAX_SCALARS = 4;

    /// <summary>
    /// An evaluated domain operation applied to every leaf below it
    /// </summary>
    sealed record DomainStep(
        int Op, 
        float[] Scalars, 
        Vector3[] Vectors, 
        Vector4[] Args);

    sealed record Leaf(List<Vector4> Code, Aabb Bounds, int ShapeId);

    readonly float t;
    readonly SceneShapes shapes;
    readonly List<Leaf> leaves = [];
    readonly List<DomainStep> chain = [];
    readonly List<Vector3> materials = [DEFAULT_COLOR];
    List<Vector4> code = [];
    int distDepth, posDepth;

    SceneCompiler(float t, SceneShapes shapes)
    {
        this.t = t;
        this.shapes = shapes;
    }

    /// <param name="shapes">
    /// Receives the shapes of the leaves, 
    /// the shader is generated from them (see <see cref="SceneGlsl"/>)
    /// </param>
    public static Vector4[] Compile(
        IReadOnlyList<SceneNode> nodes, 
        float t, 
        SceneShapes shapes)
    {
        var compiler = new SceneCompiler(t, shapes);
        foreach (SceneNode node in nodes)
        {
            if (node is not EnvNode)
                compiler.Flatten(node);
        }

        return compiler.Assemble();
    }

    // Leaves ----------------------------------------------------------------------

    /// <summary>
    /// Unions and pure domain operations distribute over the union, so they are opened
    /// and their children become separate leaves with the domain operations prepended
    /// </summary>
    void Flatten(SceneNode node)
    {
        if (node is BlockNode block
            && block.Def.FoldOp == SceneOpcodes.UNION
            && block.Def.PostOp == 0)
        {
            bool isDomain = block.Def.PushOp != 0;
            if (isDomain)
            {
                var (scalars, vectors, args) = EvalArgs(block);
                chain.Add(new DomainStep(block.Def.PushOp, scalars, vectors, args));
            }

            foreach (SceneNode child in block.Children)
                Flatten(child);

            if (isDomain)
                chain.RemoveAt(chain.Count - 1);

            return;
        }

        EmitLeaf(node);
    }

    void EmitLeaf(SceneNode node)
    {
        code = [];
        distDepth = 0;
        posDepth = 1;   // pos stack always holds the original position

        foreach (DomainStep step in chain)
        {
            Emit(step.Op, 0f, step.Args);
            PushPos(node);
        }

        Aabb bounds = EmitNode(node);
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            bounds = SceneBounds.Domain(
                chain[i].Op, 
                chain[i].Scalars, 
                chain[i].Vectors, 
                bounds);
        }

        // the shader selects the leaf function by the w of the first instruction
        int shapeId = shapes.Register(code);
        Vector4 header = code[0];
        code[0] = new Vector4(header.X, header.Y, header.Z, shapeId);

        leaves.Add(new Leaf(code, bounds, shapeId));
    }

    /// <summary>
    /// [0] header, always evaluated (infinite) leaf ranges, 
    /// leaf programs, BVH nodes, materials
    /// </summary>
    Vector4[] Assemble()
    {
        List<Leaf> always = [.. leaves.Where(l => !l.Bounds.IsFinite)];
        List<Leaf> bounded = [.. leaves.Where(l => l.Bounds.IsFinite)];
        shapes.SetAlways(always.Select(l => l.ShapeId));

        var data = new List<Vector4> { Vector4.Zero };
        data.AddRange(Enumerable.Repeat(Vector4.Zero, always.Count));

        var ranges = new Dictionary<Leaf, (int start, int end)>();
        foreach (Leaf leaf in always.Concat(bounded))
        {
            ranges[leaf] = (data.Count, data.Count + leaf.Code.Count);
            data.AddRange(leaf.Code);
        }

        for (int i = 0; i < always.Count; i++)
        {
            data[1 + i] = new Vector4(
                ranges[always[i]].start, 
                ranges[always[i]].end, 
                0f, 
                0f);
        }

        List<BvhNode> nodes = BvhBuilder.Build(
            bounded.ConvertAll(l => l.Bounds), 
            out int depth);

        if (depth >= Const.BVH_STACK_DEPTH)
        {
            throw new SceneCompileException(
                $"BVH is too deep: {depth} of {Const.BVH_STACK_DEPTH}");
        }

        int bvhStart = data.Count;
        foreach (BvhNode node in nodes)
        {
            if (node.IsLeaf)
            {
                var (start, end) = ranges[bounded[node.Item]];
                data.Add(new Vector4(node.Bounds.Min, -start));
                data.Add(new Vector4(node.Bounds.Max, end));
            }
            else
            {
                data.Add(new Vector4(node.Bounds.Min, node.Left));
                data.Add(new Vector4(node.Bounds.Max, node.Right));
            }
        }

        int materialBase = data.Count;
        foreach (Vector3 color in materials)
        {
            data.Add(new Vector4(color, 1f));
        }

        data[0] = new Vector4(materialBase, bvhStart, nodes.Count, always.Count);

        if (data.Count > Const.UBO_SDELEMENTSMAP_BLOCKCOUNT)
        {
            throw new SceneCompileException(
                "Scene is too big: " +
                    $"{data.Count} of {Const.UBO_SDELEMENTSMAP_BLOCKCOUNT} vec4");
        }

        return [.. data];
    }

    // Instructions ----------------------------------------------------------------------

    /// <summary>Header (op, length, material, 0) followed by the arguments</summary>
    void Emit(int op, float material, Vector4[] args)
    {
        code.Add(new Vector4(op, 1 + args.Length, material, 0f));
        code.AddRange(args);
    }

    void PushDist(SceneNode node)
    {
        if (++distDepth > Const.SCENE_STACK_DEPTH)
        {
            throw new SceneCompileException(
                $"Line {node.Line}: scene is nested too deep " +
                    $"(distance stack > {Const.SCENE_STACK_DEPTH})");
        }
    }

    void PushPos(SceneNode node)
    {
        if (++posDepth > Const.SCENE_STACK_DEPTH)
        {
            throw new SceneCompileException(
                $"Line {node.Line}: transforms are nested too deep " +
                    $"(position stack > {Const.SCENE_STACK_DEPTH})");
        }
    }

    /// <summary>Emits the nodes, combines them pairwise left to right with op, returns their box</summary>
    Aabb EmitFold(IReadOnlyList<SceneNode> nodes, int op, Vector4[] args)
    {
        float k = args.Length > 0 ? args[0].X : 0f;

        Aabb bounds = EmitNode(nodes[0]);
        for (int i = 1; i < nodes.Count; i++)
        {
            Aabb next = EmitNode(nodes[i]);
            Emit(op, 0f, args);
            distDepth--;
            bounds = SceneBounds.Fold(op, k, bounds, next);
        }

        return bounds;
    }

    /// <summary>Emits the node, returns its box in the local space</summary>
    Aabb EmitNode(SceneNode node) =>
        node switch
        {
            PrimNode prim => EmitPrim(prim),
            BlockNode block => EmitBlock(block),

            _ => throw new InvalidOperationException(
                $"Unknown node {node.GetType().Name}"),
        };

    Aabb EmitPrim(PrimNode node)
    {
        float[] scalars = EvalScalars(node.Def.Args, node.Scalars);
        Vector3[] vectors = EvalVectors(node.Vectors);

        if (node.Def.Opcode == SceneOpcodes.PLANE
            && vectors[0].LengthSquared > 0f)
        {
            vectors[0].Normalize();
        }

        Emit(node.Def.Opcode, Material(node.Color), PackArgs(scalars, vectors));
        PushDist(node);

        return SceneBounds.Primitive(node.Def.Opcode, scalars, vectors);
    }

    Aabb EmitBlock(BlockNode node)
    {
        BlockDef def = node.Def;
        var (scalars, vectors, args) = EvalArgs(node);

        if (def.PushOp != 0)
        {
            Emit(def.PushOp, 0f, def.ArgsTo == ArgTarget.Push ? args : []);
            PushPos(node);
        }

        Aabb bounds = EmitFold(
            node.Children, 
            def.FoldOp, 
            def.ArgsTo == ArgTarget.Fold ? args : []);

        if (def.PushOp != 0)
        {
            Emit(SceneOpcodes.POPPOS, 0f, []);
            posDepth--;
            bounds = SceneBounds.Domain(def.PushOp, scalars, vectors, bounds);
        }

        if (def.PostOp != 0)
        {
            Emit(def.PostOp, 0f, def.ArgsTo == ArgTarget.Post ? args : []);
            bounds = SceneBounds.Post(def.PostOp, scalars, bounds);
        }

        return bounds;
    }

    // Arguments ----------------------------------------------------------------------

    (float[] scalars, Vector3[] vectors, Vector4[] args) EvalArgs(BlockNode node)
    {
        float[] scalars = EvalScalars(node.Def.Args, node.Scalars);
        Vector3[] vectors = EvalVectors(node.Vectors);

        if (node.Def.PushOp == SceneOpcodes.ROTATE)
        {
            vectors = InverseRotationRows(vectors[0]);
        }

        return (scalars, vectors, PackArgs(scalars, vectors));
    }

    /// <summary>Evaluates scalar arguments, an angle in degrees becomes (sin, cos)</summary>
    float[] EvalScalars(ArgKind[] kinds, List<ExprBase> exprs)
    {
        var values = new List<float>();
        int i = 0;

        foreach (ArgKind kind in kinds)
        {
            if (kind == ArgKind.Vector)
                continue;

            float value = exprs[i++].Eval(t);
            if (kind == ArgKind.Angle)
            {
                float radians = MathHelper.DegreesToRadians(value);
                values.Add(MathF.Sin(radians));
                values.Add(MathF.Cos(radians));
            }
            else
            {
                values.Add(value);
            }
        }

        if (values.Count > MAX_SCALARS)
        {
            throw new InvalidOperationException(
                $"More than {MAX_SCALARS} scalars do not fit an instruction");
        }

        return [.. values];
    }

    Vector3[] EvalVectors(List<Vec3Expr> exprs) =>
        [.. exprs.ConvertAll(v => v.Eval(t))];

    /// <summary>S (scalars) then a vec4 per vector, nothing when there are no arguments</summary>
    static Vector4[] PackArgs(float[] scalars, Vector3[] vectors)
    {
        if (scalars.Length == 0 && vectors.Length == 0)
            return [];

        var args = new Vector4[1 + vectors.Length];
        for (int i = 0; i < scalars.Length; i++)
        {
            args[0][i] = scalars[i];
        }

        for (int i = 0; i < vectors.Length; i++)
        {
            args[1 + i] = new Vector4(vectors[i], 0f);
        }

        return args;
    }

    /// <summary>
    /// Object rotation R = Rz * Ry * Rx (degrees). The sample point is rotated back with
    /// inverse(R) = transpose(R), so rows of the inverse are the columns of R.
    /// </summary>
    static Vector3[] InverseRotationRows(Vector3 degrees)
    {
        float ax = MathHelper.DegreesToRadians(degrees.X);
        float ay = MathHelper.DegreesToRadians(degrees.Y);
        float az = MathHelper.DegreesToRadians(degrees.Z);
        float cx = MathF.Cos(ax), sx = MathF.Sin(ax);
        float cy = MathF.Cos(ay), sy = MathF.Sin(ay);
        float cz = MathF.Cos(az), sz = MathF.Sin(az);

        // R[row, col], column-vector convention
        var r = new float[3, 3]
        {
            { cz * cy, cz * sy * sx - sz * cx, cz * sy * cx + sz * sx },
            { sz * cy, sz * sy * sx + cz * cx, sz * sy * cx - cz * sx },
            { -sy,     cy * sx,                cy * cx                },
        };

        var rows = new Vector3[3];
        for (int col = 0; col < 3; col++)
        {
            rows[col] = new Vector3(r[0, col], r[1, col], r[2, col]);
        }

        return rows;
    }

    float Material(Vec3Expr color)
    {
        if (color == null)
            return 0f;

        Vector3 rgb = color.Eval(t);
        int index = materials.IndexOf(rgb);
        if (index < 0)
        {
            index = materials.Count;
            materials.Add(rgb);
        }

        return index;
    }
}
