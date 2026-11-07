using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;

// PoC: mutant を schemata ではなく EnC の差分(EmitDifference)として生成し、MetadataUpdater.ApplyUpdate で
// 常駐 process 内のメソッド本体を差し替える → 判定 → 逆差分で元に戻す、の費用と実現性を測る。
static class Program
{
    static readonly Guid EncLocalSlotMap = new("755F52A8-91C5-45BE-B4B8-209571E552BD");
    static readonly Guid EncLambdaAndClosureMap = new("A643004C-0240-496F-A783-30D64F4979DE");
    static readonly Guid EncStateMachineStateMap = new("8B78CD68-2EDE-420B-980B-E15884B8AAA3");

    static int Main(string[] args)
    {
        var methods = args.Length > 0 ? int.Parse(args[0]) : 400;
        Console.WriteLine($"MetadataUpdater.IsSupported={MetadataUpdater.IsSupported} methods={methods}");
        if (!MetadataUpdater.IsSupported)
        {
            Console.WriteLine("DOTNET_MODIFIABLE_ASSEMBLIES=debug が必要");
            return 2;
        }

        var source = GenerateSource(methods);
        var parse = new CSharpParseOptions(LanguageVersion.Latest);
        var tree = CSharpSyntaxTree.ParseText(source, parse, path: "Lib.cs", encoding: Encoding.UTF8);
        var refs = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(p => Path.GetFileName(p) is "System.Runtime.dll" or "System.Private.CoreLib.dll" or "System.Linq.dll" or "System.Collections.dll")
            .Select(p => MetadataReference.CreateFromFile(p)).ToArray();
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Debug);
        var sw = Stopwatch.StartNew();
        var compilation0 = CSharpCompilation.Create("Lib", [tree], refs, options);
        using var pe = new MemoryStream();
        using var pdb = new MemoryStream();
        var emit = compilation0.Emit(pe, pdb, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
        if (!emit.Success) { foreach (var d in emit.Diagnostics) Console.WriteLine(d); return 1; }
        var fullEmitMs = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine($"full emit (initial, {methods} methods): {fullEmitMs:F0} ms");

        var peBytes = pe.ToArray();
        var pdbBytes = pdb.ToArray();
        var assembly = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(peBytes), new MemoryStream(pdbBytes));
        var type = assembly.GetType("Lib.Calc")!;

        var moduleMetadata = ModuleMetadata.CreateFromImage(peBytes);
        var pdbProvider = MetadataReaderProvider.FromPortablePdbImage(ImmutableArray.Create(pdbBytes));
        var pdbReader = pdbProvider.GetMetadataReader();
        var peReader = new PEReader(ImmutableArray.Create(peBytes));
        var mdReader = peReader.GetMetadataReader();
        var baseline = EmitBaseline.CreateInitialBaseline(
            compilation0,
            moduleMetadata,
            handle => DebugInfo(pdbReader, handle),
            handle =>
            {
                var def = mdReader.GetMethodDefinition(handle);
                return def.RelativeVirtualAddress == 0 ? default : peReader.GetMethodBody(def.RelativeVirtualAddress).LocalSignature;
            },
            hasPortableDebugInformation: true);

        var current = compilation0;
        var currentTree = tree;
        var emitMs = new List<double>();
        var applyMs = new List<double>();
        var revertEmitMs = new List<double>();
        var revertApplyMs = new List<double>();
        var failures = 0;
        var gcBefore = GC.GetTotalMemory(true);
        var rssBefore = Environment.WorkingSet;
        var root = (CompilationUnitSyntax)tree.GetRoot();
        var classDecl = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
        var methodDecls = classDecl.Members.OfType<MethodDeclarationSyntax>().ToArray();
        for (var k = 0; k < methodDecls.Length; k++)
        {
            var name = methodDecls[k].Identifier.ValueText;
            var target = type.GetMethod(name)!;
            var expected = (int)target.Invoke(null, [7, 3])!;

            // mutant: 本体の最初の二項演算子 + を - へ
            var originalMethod = (MethodDeclarationSyntax)currentTree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().First(m => m.Identifier.ValueText == name);
            var binary = originalMethod.DescendantNodes().OfType<BinaryExpressionSyntax>().First(b => b.IsKind(SyntaxKind.AddExpression));
            var mutatedRoot = currentTree.GetRoot().ReplaceNode(binary, SyntaxFactory.BinaryExpression(SyntaxKind.SubtractExpression, binary.Left, binary.Right));
            var mutatedTree = currentTree.WithRootAndOptions(mutatedRoot, parse);

            var (next, nextBaseline, eMs, aMs, ok) = Apply(current, currentTree, mutatedTree, baseline, name, assembly);
            if (!ok) { failures++; continue; }
            emitMs.Add(eMs); applyMs.Add(aMs);
            var mutatedValue = (int)target.Invoke(null, [7, 3])!;
            if (mutatedValue == expected) failures++;

            // 元に戻す(逆向きの差分)
            var (back, backBaseline, rEms, rAms, ok2) = Apply(next, mutatedTree, currentTree, nextBaseline, name, assembly);
            if (!ok2) { failures++; continue; }
            revertEmitMs.Add(rEms); revertApplyMs.Add(rAms);
            var restored = (int)target.Invoke(null, [7, 3])!;
            if (restored != expected) failures++;
            current = back;
            baseline = backBaseline;
        }

        var gcAfter = GC.GetTotalMemory(true);
        var rssAfter = Environment.WorkingSet;
        Console.WriteLine($"mutants applied+reverted: {emitMs.Count}, failures: {failures}");
        Console.WriteLine($"EmitDifference (mutate): median {Median(emitMs):F2} ms, p90 {P(emitMs, 0.9):F2} ms");
        Console.WriteLine($"ApplyUpdate    (mutate): median {Median(applyMs):F3} ms, p90 {P(applyMs, 0.9):F3} ms");
        Console.WriteLine($"EmitDifference (revert): median {Median(revertEmitMs):F2} ms");
        Console.WriteLine($"ApplyUpdate    (revert): median {Median(revertApplyMs):F3} ms");
        Console.WriteLine($"managed heap growth: {(gcAfter - gcBefore) / 1024.0 / 1024.0:F1} MB, RSS growth: {(rssAfter - rssBefore) / 1024.0 / 1024.0:F1} MB over {emitMs.Count * 2} generations");
        return failures == 0 ? 0 : 1;
    }

    static (CSharpCompilation, EmitBaseline, double, double, bool) Apply(
        CSharpCompilation from, SyntaxTree oldTree, SyntaxTree newTree, EmitBaseline baseline, string methodName, Assembly assembly)
    {
        var sw = Stopwatch.StartNew();
        var to = from.ReplaceSyntaxTree(oldTree, newTree);
        var oldSymbol = from.GetTypeByMetadataName("Lib.Calc")!.GetMembers(methodName).Single();
        var newSymbol = to.GetTypeByMetadataName("Lib.Calc")!.GetMembers(methodName).Single();
        var edit = new SemanticEdit(SemanticEditKind.Update, oldSymbol, newSymbol);
        using var md = new MemoryStream();
        using var il = new MemoryStream();
        using var pdb = new MemoryStream();
        var diff = to.EmitDifference(baseline, [edit], _ => false, md, il, pdb);
        var emitMs = sw.Elapsed.TotalMilliseconds;
        if (!diff.Success)
        {
            foreach (var d in diff.Diagnostics.Take(3)) Console.WriteLine("  " + d);
            return (from, baseline, emitMs, 0, false);
        }

        sw.Restart();
        MetadataUpdater.ApplyUpdate(assembly, md.ToArray(), il.ToArray(), pdb.ToArray());
        return (to, diff.Baseline!, emitMs, sw.Elapsed.TotalMilliseconds, true);
    }

    static EditAndContinueMethodDebugInformation DebugInfo(MetadataReader pdb, MethodDefinitionHandle handle)
    {
        ImmutableArray<byte> slots = default, lambdas = default, states = default;
        foreach (var cdiHandle in pdb.GetCustomDebugInformation(handle))
        {
            var cdi = pdb.GetCustomDebugInformation(cdiHandle);
            var kind = pdb.GetGuid(cdi.Kind);
            if (kind == EncLocalSlotMap) slots = pdb.GetBlobContent(cdi.Value);
            else if (kind == EncLambdaAndClosureMap) lambdas = pdb.GetBlobContent(cdi.Value);
            else if (kind == EncStateMachineStateMap) states = pdb.GetBlobContent(cdi.Value);
        }

        return EditAndContinueMethodDebugInformation.Create(slots, lambdas, states);
    }

    static string GenerateSource(int methods)
    {
        var sb = new StringBuilder("namespace Lib;\npublic static class Calc\n{\n");
        for (var i = 0; i < methods; i++)
        {
            sb.Append($"    public static int M{i}(int a, int b)\n    {{\n        var t = a + b * {i % 5 + 1};\n        if (t > {i % 13}) {{ t = t - 1; }}\n        return t;\n    }}\n");
        }

        return sb.Append("}\n").ToString();
    }

    static double Median(List<double> v) => P(v, 0.5);

    static double P(List<double> v, double q)
    {
        if (v.Count == 0) return double.NaN;
        var s = v.OrderBy(x => x).ToArray();
        return s[Math.Min(s.Length - 1, (int)(q * s.Length))];
    }
}
