using TUnit.Core;

namespace Mutation.Tests;

/// <summary>文脈の隔離・層の依存方向・単位構造の機械検査</summary>
public sealed class ArchitectureFacts
{
    /// <summary>Mutation package 直下の文脈の一覧</summary>
    private static readonly string[] Contexts = ["mutating", "verifying"];

    /// <summary>文脈が互いと composition を参照しないこと</summary>
    [Test]
    public async Task Contexts_stay_ignorant_of_each_other()
    {
        var offending = new (string Context, string Forbidden)[]
        {
            ("mutating", "Mutation.Verifying"),
            ("verifying", "Mutation.Mutating"),
            ("mutating", "Mutation.Composition"),
            ("verifying", "Mutation.Composition"),
        }
            .SelectMany(rule =>
                SourcesUnder(Path.Combine("src", "Mutation", rule.Context))
                    .Where(f => File.ReadAllText(f).Contains(rule.Forbidden, StringComparison.Ordinal))
            )
            .ToArray();
        await Assert.That(offending).IsEmpty();
    }

    /// <summary>各文脈の domain が application と infrastructure を参照しないこと</summary>
    [Test]
    public async Task Domain_depends_on_nothing_above()
    {
        var offending = Contexts
            .SelectMany(context => SourcesUnder(Path.Combine("src", "Mutation", context, "domain")))
            .Where(f => File.ReadAllText(f) is { } s
                && (s.Contains(".Application", StringComparison.Ordinal)
                    || s.Contains(".Infrastructure", StringComparison.Ordinal)))
            .ToArray();
        await Assert.That(offending).IsEmpty();
    }

    /// <summary>各文脈の application が infrastructure を参照しないこと(依存は infrastructure → application)</summary>
    [Test]
    public async Task Application_does_not_reach_into_infrastructure()
    {
        var offending = Contexts
            .SelectMany(context => SourcesUnder(Path.Combine("src", "Mutation", context, "application")))
            .Where(f => File.ReadAllText(f).Contains(".Infrastructure", StringComparison.Ordinal))
            .ToArray();
        await Assert.That(offending).IsEmpty();
    }

    /// <summary>domain と application の直下が規範の単位フォルダだけで構成されること</summary>
    [Test]
    public async Task Layers_hold_only_standard_units()
    {
        var units = new Dictionary<string, string[]>
        {
            ["domain"] = ["aggregates", "values", "events", "services"],
            ["application"] = ["usecases", "workflows", "ports"],
        };
        var offending = Contexts
            .SelectMany(context => units.SelectMany(layer =>
            {
                var path = RootedPath(Path.Combine("src", "Mutation", context, layer.Key));
                var looseFiles = Directory.EnumerateFiles(path, "*.cs", SearchOption.TopDirectoryOnly);
                var strangeFolders = Directory.EnumerateDirectories(path)
                    .Where(d => !layer.Value.Contains(Path.GetFileName(d), StringComparer.Ordinal));
                return looseFiles.Concat(strangeFolders);
            }))
            .ToArray();
        await Assert.That(offending).IsEmpty();
    }

    /// <summary>各 project の root 直下のファイルが entry point だけであること</summary>
    [Test]
    public async Task Project_roots_hold_only_entry_points()
    {
        var roots = new[]
        {
            Path.Combine("src", "Mutation"),
            Path.Combine("src", "Mutation.Cli"),
            Path.Combine("src", "Mutation.Worker"),
            Path.Combine("tests", "Mutation.Tests"),
        };
        var offending = roots
            .SelectMany(root => Directory.EnumerateFiles(RootedPath(root), "*.cs", SearchOption.TopDirectoryOnly))
            .Where(f => Path.GetFileName(f) != "Program.cs")
            .ToArray();
        await Assert.That(offending).IsEmpty();
    }

    /// <summary>shared kernel がどの文脈にも依存しないこと</summary>
    [Test]
    public async Task Shared_kernel_depends_on_no_context()
    {
        var offending = SourcesUnder(Path.Combine("src", "Mutation", "shared"))
            .Where(f => File.ReadAllText(f).Contains("using Mutation.", StringComparison.Ordinal))
            .ToArray();
        await Assert.That(offending).IsEmpty();
    }

    /// <summary>protocol がどの境界にも依存しないこと</summary>
    [Test]
    public async Task Protocol_depends_on_nothing()
    {
        var offending = SourcesUnder(Path.Combine("src", "Mutation", "protocol"))
            .Where(f => File.ReadAllText(f).Contains("using Mutation", StringComparison.Ordinal))
            .ToArray();
        await Assert.That(offending).IsEmpty();
    }

    /// <summary>Cli が infrastructure へ届かないこと</summary>
    [Test]
    public async Task Console_surface_uses_only_the_operations()
    {
        var offending = SourcesUnder(Path.Combine("src", "Mutation.Cli"))
            .Where(f => File.ReadAllText(f).Contains(".Infrastructure", StringComparison.Ordinal))
            .ToArray();
        await Assert.That(offending).IsEmpty();
    }

    /// <summary>repo root からの相対 path の絶対化</summary>
    private static string RootedPath(string relative)
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Mutation.slnx")))
        {
            root = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("repo root が見つからない");
        }

        return Path.Combine(root, relative);
    }

    /// <summary>repo root からの相対での source の列挙</summary>
    private static IEnumerable<string> SourcesUnder(string relative)
    {
        return Directory
            .EnumerateFiles(RootedPath(relative), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }
}
