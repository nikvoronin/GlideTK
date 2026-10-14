using GldeTK.Scene.Defs;
using GldeTK.Scene.Expr;
using GldeTK.Scene.Nodes;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;

namespace GldeTK.Scene;


/// <summary>
/// Flattens the scene tree into a stack program (see <see cref="SceneOpcodes"/>)
/// with all expressions evaluated at time t.
/// </summary>
public class SceneCompiler
{
    static readonly Vector3 DEFAULT_COLOR = new(1f);
    const int MAX_SCALARS = 4;

    readonly float t;
    readonly List<Vector4> code = [Vector4.Zero];   // [0] is the header
    readonly List<Vector3> materials = [DEFAULT_COLOR];
    int distDepth, posDepth = 1;    // pos stack always holds the original position

    SceneCompiler(float t) => this.t = t;

    public static Vector4[] Compile(IReadOnlyList<SceneNode> nodes, float t)
    {
        var compiler = new SceneCompiler(t);
        if (nodes.Count > 0)
            compiler.EmitFold(nodes, SceneOpcodes.UNION, []);

        int programEnd = compiler.code.Count;
        compiler.code[0] = new Vector4(programEnd, programEnd, 0f, 0f);
        foreach (Vector3 color in compiler.materials)
            compiler.code.Add(new Vector4(color, 1f));

        if (compiler.code.Count > Const.UBO_SDELEMENTSMAP_BLOCKCOUNT)
            throw new SceneCompileException(
                $"Scene is too big: {compiler.code.Count} of {Const.UBO_SDELEMENTSMAP_BLOCKCOUNT} vec4");

        return [.. compiler.code];
    }

    /// <summary>Header (op, length, material, 0) followed by the arguments</summary>
    void Emit(int op, float material, Vector4[] args)
    {
        code.Add(new Vector4(op, 1 + args.Length, material, 0f));
        code.AddRange(args);
    }

    void PushDist(SceneNode node)
    {
        if (++distDepth > Const.SCENE_STACK_DEPTH)
            throw new SceneCompileException($"Line {node.Line}: scene is nested too deep (distance stack > {Const.SCENE_STACK_DEPTH})");
    }

    void PushPos(SceneNode node)
    {
        if (++posDepth > Const.SCENE_STACK_DEPTH)
            throw new SceneCompileException($"Line {node.Line}: transforms are nested too deep (position stack > {Const.SCENE_STACK_DEPTH})");
    }

    /// <summary>Emits the nodes and combines them pairwise left to right with op.</summary>
    void EmitFold(IReadOnlyList<SceneNode> nodes, int op, Vector4[] args)
    {
        EmitNode(nodes[0]);
        for (int i = 1; i < nodes.Count; i++)
        {
            EmitNode(nodes[i]);
            Emit(op, 0f, args);
            distDepth--;
        }
    }

    void EmitNode(SceneNode node)
    {
        switch (node)
        {
            case PrimNode prim: EmitPrim(prim); break;
            case BlockNode block: EmitBlock(block); break;
            default: throw new InvalidOperationException($"Unknown node {node.GetType().Name}");
        }
    }

    void EmitPrim(PrimNode node)
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
    }

    void EmitBlock(BlockNode node)
    {
        BlockDef def = node.Def;
        float[] scalars = EvalScalars(def.Args, node.Scalars);
        Vector3[] vectors = EvalVectors(node.Vectors);

        if (def.PushOp == SceneOpcodes.ROTATE)
            vectors = InverseRotationRows(vectors[0]);

        Vector4[] args = PackArgs(scalars, vectors);

        if (def.PushOp != 0)
        {
            Emit(def.PushOp, 0f, def.ArgsTo == ArgTarget.Push ? args : []);
            PushPos(node);
        }

        EmitFold(node.Children, def.FoldOp, def.ArgsTo == ArgTarget.Fold ? args : []);

        if (def.PushOp != 0)
        {
            Emit(SceneOpcodes.POPPOS, 0f, []);
            posDepth--;
        }

        if (def.PostOp != 0)
            Emit(def.PostOp, 0f, def.ArgsTo == ArgTarget.Post ? args : []);
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
            throw new InvalidOperationException($"More than {MAX_SCALARS} scalars do not fit an instruction");

        return [.. values];
    }

    Vector3[] EvalVectors(List<Vec3Expr> exprs) =>
        exprs.ConvertAll(v => v.Eval(t)).ToArray();

    /// <summary>S (scalars) then a vec4 per vector, nothing when there are no arguments</summary>
    static Vector4[] PackArgs(float[] scalars, Vector3[] vectors)
    {
        if (scalars.Length == 0 && vectors.Length == 0)
            return [];

        var args = new Vector4[1 + vectors.Length];
        for (int i = 0; i < scalars.Length; i++)
            args[0][i] = scalars[i];
        for (int i = 0; i < vectors.Length; i++)
            args[1 + i] = new Vector4(vectors[i], 0f);

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
            rows[col] = new Vector3(r[0, col], r[1, col], r[2, col]);

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
