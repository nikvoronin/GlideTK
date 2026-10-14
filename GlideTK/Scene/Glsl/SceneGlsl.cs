using System;
using System.Collections.Generic;
using System.Text;

namespace GlideTK.Scene.Glsl;

/// <summary>
/// Generates the GLSL of the scene leaves: 
/// every shape of <see cref="SceneShapes"/> becomes a function 
/// of straight-line code (the stack program is simulated at generation time, 
/// so no interpreter, no switch on opcodes and no arrays at run time). 
/// The arguments are still read from the program by constant offsets, 
/// so animated values need no new shader.
/// </summary>
public static class SceneGlsl
{
    /// <summary>
    /// The line of shaders/fragment.c replaced with the generated code
    /// </summary>
    public const string MARKER = "//@SCENE_SHAPES";

    public static string Inject(string fragmentSource, SceneShapes shapes)
    {
        if (!fragmentSource.Contains(MARKER))
        {
            throw new InvalidOperationException(
                $"Fragment shader has no {MARKER} line");
        }

        return fragmentSource.Replace(MARKER, Generate(shapes));
    }

    /// <summary>
    /// shapeN(pc, p) for every shape, evalLeaf(start, p) selecting the shape 
    /// of a leaf by the w of its first instruction, 
    /// evalAlways(p) calling the unbounded leaves one after another
    /// </summary>
    public static string Generate(SceneShapes shapes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// generated from the scene by Scene/Glsl/SceneGlsl.cs");

        for (int id = 0; id < shapes.Shapes.Count; id++)
            EmitShape(sb, id, shapes.Shapes[id]);

        sb.AppendLine("vec2 evalLeaf(int start, vec3 pos)");
        sb.AppendLine("{");
        sb.AppendLine("\tswitch (int(g_map[start].w))");
        sb.AppendLine("\t{");
        for (int id = 0; id < shapes.Shapes.Count; id++)
            sb.AppendLine($"\t\tcase {id}: return shape{id}(start, pos);");
        sb.AppendLine("\t}");
        sb.AppendLine("\treturn vec2(EMPTY_DISTANCE, 0.0);");
        sb.AppendLine("}");
        sb.AppendLine();

        sb.AppendLine("vec2 evalAlways(vec3 pos)");
        sb.AppendLine("{");
        sb.AppendLine("\tvec2 best = vec2(EMPTY_DISTANCE, 0.0);");
        for (int i = 0; i < shapes.Always.Count; i++)
        {
            sb.AppendLine(
                $"\tbest = opUnion(best, shape{shapes.Always[i]}(int(g_map[{1 + i}].x), pos));");
        }
        sb.AppendLine("\treturn best;");
        sb.AppendLine("}");

        return sb.ToString();
    }

    static void EmitShape(StringBuilder sb, int id, (int Op, int Length)[] code)
    {
        sb.AppendLine($"vec2 shape{id}(int pc, vec3 p0)");
        sb.AppendLine("{");
        sb.AppendLine("\tfloat s0 = 1.0;");

        // the stack of positions (name, scale name)
        // and of distances (names) at the instruction
        var positions = new Stack<(string P, string S)>();
        positions.Push(("p0", "s0"));
        var distances = new List<string>();
        int n = 1;
        int offset = 0;

        foreach (var (op, length) in code)
        {
            var (p, s) = positions.Peek();
            string Expand(
                string template, 
                string d = null, 
                string a = null, 
                string b = null, 
                string k = null)
            {
                template = template
                    .Replace("$S", $"g_map[pc + {offset + 1}]")
                    .Replace("$p", p)
                    .Replace("$s", s);

                for (int i = 3; i >= 0; i--)
                    template = template.Replace($"$V{i}", $"g_map[pc + {offset + 2 + i}].xyz");

                return template
                    .Replace("$d", d)
                    .Replace("$a", a)
                    .Replace("$b", b)
                    .Replace("$k", k);
            }

            if (op < SceneOpcodes.DOMAIN_FIRST)
            {
                string d = $"d{n++}";
                string distance = Expand(Template(GlslOps.Primitives, op));
                sb.AppendLine($"\tvec2 {d} = vec2({distance} * {s}, g_map[pc + {offset}].z);");
                distances.Add(d);
            }
            else if (op == SceneOpcodes.POPPOS)
            {
                positions.Pop();
            }
            else if (op < SceneOpcodes.POST_FIRST)
            {
                var (position, scale) = Template(GlslOps.Domains, op);
                string np = $"p{n}", ns = $"s{n}";
                n++;
                sb.AppendLine($"\tvec3 {np} = {Expand(position)};");
                sb.AppendLine($"\tfloat {ns} = {Expand(scale)};");
                positions.Push((np, ns));
            }
            else if (op < SceneOpcodes.FOLD_FIRST)
            {
                string d = $"d{n++}";
                sb.AppendLine(
                    $"\tvec2 {d} = {Expand(Template(GlslOps.Posts, op), d: distances[^1])};");
                distances[^1] = d;
            }
            else
            {
                string b = distances[^1];
                distances.RemoveAt(distances.Count - 1);
                string a = distances[^1];

                // the radius is present when the instruction has arguments
                string k = $"k{n}";
                string radius = length > 1 ? Expand("$S.x * $s") : "0.0";
                sb.AppendLine($"\tfloat {k} = {radius};");

                string d = $"d{n++}";
                sb.AppendLine(
                    $"\tvec2 {d} = {Expand(Template(GlslOps.Folds, op), a: a, b: b, k: k)};");
                distances[^1] = d;
            }

            offset += length;
        }

        if (distances.Count != 1)
        {
            throw new InvalidOperationException(
                $"Shape {id} leaves {distances.Count} distances on the stack");
        }

        sb.AppendLine($"\treturn {distances[0]};");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    static T Template<T>(Dictionary<int, T> table, int op) =>
        table.TryGetValue(op, out T template)
            ? template
            : throw new InvalidOperationException($"No GLSL for opcode {op}");
}
