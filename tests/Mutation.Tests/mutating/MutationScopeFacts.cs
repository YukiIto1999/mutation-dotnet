
using Mutation.Mutating.Domain;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>変異対象の glob 範囲の検査</summary>
public sealed class MutationScopeFacts
{
    /// <summary>指定なしでの全 .cs の包含と obj/bin の除外</summary>
    [Test]
    public async Task Default_scope_includes_every_source_but_build_outputs()
    {
        var scope = MutationScope.FromPatterns([]);
        await Assert.That(scope.Includes("domain/Foo.cs")).IsTrue();
        await Assert.That(scope.Includes("obj/Debug/net10.0/Foo.g.cs")).IsFalse();
        await Assert.That(scope.Includes("bin/Debug/Foo.cs")).IsFalse();
    }

    /// <summary>Stryker 流の包含と `!` 除外の後勝ち</summary>
    [Test]
    public async Task Include_and_exclude_patterns_follow_last_match_wins()
    {
        var scope = MutationScope.FromPatterns(["domain/**/*.cs", "application/**/*.cs", "!domain/legacy/**"]);
        await Assert.That(scope.Includes("domain/values/Money.cs")).IsTrue();
        await Assert.That(scope.Includes("application/UseCase.cs")).IsTrue();
        await Assert.That(scope.Includes("domain/legacy/Old.cs")).IsFalse();
        await Assert.That(scope.Includes("infrastructure/Db.cs")).IsFalse();
    }

    /// <summary>`*` が directory 区切りを跨がないこと</summary>
    [Test]
    public async Task Single_star_does_not_cross_directories()
    {
        var scope = MutationScope.FromPatterns(["*/Mapper*.cs"]);
        await Assert.That(scope.Includes("composition/MapperA.cs")).IsTrue();
        await Assert.That(scope.Includes("composition/nested/MapperB.cs")).IsFalse();
    }
}
