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
    static readonly Vector3 DEFAULT_COLOR = new Vector3(1f);

    readonly float t;
    readonly List<Vector4> code = new List<Vector4> { Vector4.Zero };   // [0] is the header
    readonly List<Vector3> materials = new List<Vector3> { DEFAULT_COLOR };
    int distDepth, posDepth = 1;    // pos stack always holds the original position

    SceneCompiler(float t) => this.t = t;

    public static Vector4[] Compile(IReadOnlyList<SceneNode> nodes, float t)
    {
        var compiler = new SceneCompiler(t);
        if (nodes.Count > 0)
            compiler.EmitFold(nodes, SceneOpcodes.UNION, 0f);

        int programEnd = compiler.code.Count;
        compiler.code[0] = new Vector4(programEnd, programEnd, 0f, 0f);
        foreach (Vector3 color in compiler.materials)
            compiler.code.Add(new Vector4(color, 1f));

        if (compiler.code.Count > Const.UBO_SDELEMENTSMAP_BLOCKCOUNT)
            throw new SceneCompileException(
                $"Scene is too big: {compiler.code.Count} of {Const.UBO_SDELEMENTSMAP_BLOCKCOUNT} vec4");

        return compiler.code.ToArray();
    }

    void Emit(int op, float a = 0f, float b = 0f, float c = 0f) => code.Add(new Vector4(op, a, b, c));

    void Emit(Vector3 v) => code.Add(new Vector4(v, 0f));

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
    void EmitFold(IReadOnlyList<SceneNode> nodes, int op, float k)
    {
        EmitNode(nodes[0]);
        for (int i = 1; i < nodes.Count; i++)
        {
            EmitNode(nodes[i]);
            Emit(op, k);
            distDepth--;
        }
    }

    void EmitNode(SceneNode node)
    {
        switch (node)
        {
            case PrimNode prim: EmitPrim(prim); break;
            case DomainNode domain: EmitDomain(domain); break;
            case CsgNode csg: EmitCsg(csg); break;
            default: throw new InvalidOperationException($"Unknown node {node.GetType().Name}");
        }
    }

    void EmitPrim(PrimNode node)
    {
        float mat = Material(node.Color);

        switch (node.Kind)
        {
            case PrimKind.Sphere:
                Emit(SceneOpcodes.SPHERE, node.A.Eval(t), mat);
                break;
            case PrimKind.Box:
                Emit(SceneOpcodes.BOX, 0f, mat);
                Emit(node.V.Eval(t));
                break;
            case PrimKind.Plane:
                Vector3 normal = node.V.Eval(t);
                if (normal.LengthSquared > 0f)
                    normal.Normalize();
                Emit(SceneOpcodes.PLANE, node.A.Eval(t), mat);
                Emit(normal);
                break;
            case PrimKind.Cylinder:
                Emit(SceneOpcodes.CYLINDER, node.A.Eval(t), mat, node.B.Eval(t));
                break;
            case PrimKind.Torus:
                Emit(SceneOpcodes.TORUS, node.A.Eval(t), mat, node.B.Eval(t));
                break;
        }

        PushDist(node);
    }

    void EmitDomain(DomainNode node)
    {
        switch (node.Kind)
        {
            case DomainKind.Translate:
                Vector3 offset = node.V.Eval(t);
                Emit(SceneOpcodes.TRANSLATE, offset.X, offset.Y, offset.Z);
                break;
            case DomainKind.Repeat:
                Vector3 period = node.V.Eval(t);
                Emit(SceneOpcodes.REPEAT, period.X, period.Y, period.Z);
                break;
            case DomainKind.Rotate:
                Emit(SceneOpcodes.ROTATE);
                EmitInverseRotation(node.V.Eval(t));
                break;
            case DomainKind.Scale:
                Emit(SceneOpcodes.SCALE, node.A.Eval(t));
                break;
        }

        PushPos(node);
        EmitFold(node.Children, SceneOpcodes.UNION, 0f);
        Emit(SceneOpcodes.POPPOS);
        posDepth--;
    }

    void EmitCsg(CsgNode node)
    {
        switch (node.Kind)
        {
            case CsgKind.Union: EmitFold(node.Children, SceneOpcodes.UNION, 0f); break;
            case CsgKind.Subtract: EmitFold(node.Children, SceneOpcodes.SUBTRACT, 0f); break;
            case CsgKind.Intersect: EmitFold(node.Children, SceneOpcodes.INTERSECT, 0f); break;
            case CsgKind.Smooth: EmitFold(node.Children, SceneOpcodes.SMOOTH, node.K.Eval(t)); break;
        }
    }

    /// <summary>
    /// Object rotation R = Rz * Ry * Rx (degrees). The sample point is rotated back with
    /// inverse(R) = transpose(R), so rows of the inverse are the columns of R.
    /// </summary>
    void EmitInverseRotation(Vector3 degrees)
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

        for (int col = 0; col < 3; col++)
            Emit(new Vector3(r[0, col], r[1, col], r[2, col]));
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
