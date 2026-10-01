using Mutation.Mutating.Infrastructure.Git;
using Mutation.Shared;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>`git diff -U0` の出力からの変わった行の読み取りの検査</summary>
public sealed class UnifiedDiffFacts
{
    /// <summary>新しい側の行だけが変わった行になり、削除だけの hunk と削除されたファイルは範囲を持たないこと</summary>
    [Test]
    public async Task New_side_of_each_hunk_becomes_the_changed_lines()
    {
        string[] diff =
        [
            "diff --git a/src/A.cs b/src/A.cs",
            "index 1111111..2222222 100644",
            "--- a/src/A.cs",
            "+++ b/src/A.cs",
            "@@ -3 +3 @@ class A",
            "-    int x = 1;",
            "+    int x = 2;",
            "@@ -10,0 +11,2 @@ class A",
            "+    int y;",
            "+    int z;",
            "@@ -20,2 +21,0 @@ class A",
            "-    int gone;",
            "-    int alsoGone;",
            "diff --git a/src/B.cs b/src/B.cs",
            "deleted file mode 100644",
            "--- a/src/B.cs",
            "+++ /dev/null",
            "@@ -1,3 +0,0 @@",
            "-class B",
            "-{",
            "-}",
        ];
        var changes = new ChangedLines(UnifiedDiff.Parse("/repo", diff));

        await Assert.That(changes.Files.Order().ToArray()).IsEquivalentTo([Path.GetFullPath("/repo/src/A.cs")]);
        await Assert.That(changes.Overlaps("/repo/src/A.cs", 3, 3)).IsTrue();
        await Assert.That(changes.Overlaps("/repo/src/A.cs", 4, 10)).IsFalse();
        await Assert.That(changes.Overlaps("/repo/src/A.cs", 12, 12)).IsTrue();
        await Assert.That(changes.Overlaps("/repo/src/A.cs", 13, 30)).IsFalse();
        await Assert.That(changes.Overlaps("/repo/src/B.cs", 1, 3)).IsFalse();
    }

    /// <summary>`++` で始まる行を足した内容行が、ファイルの見出しとして読まれないこと</summary>
    [Test]
    public async Task Added_content_that_looks_like_a_header_stays_content()
    {
        string[] diff =
        [
            "diff --git a/C.cs b/C.cs",
            "new file mode 100644",
            "--- /dev/null",
            "+++ b/C.cs",
            "@@ -0,0 +1,2 @@",
            "+++ b/Fake.cs",
            "+x",
        ];
        var changes = new ChangedLines(UnifiedDiff.Parse("/repo", diff));

        await Assert.That(changes.Files.ToArray()).IsEquivalentTo([Path.GetFullPath("/repo/C.cs")]);
        await Assert.That(changes.Overlaps("/repo/C.cs", 1, 2)).IsTrue();
    }

    /// <summary>git が引用した path が、escape と 8 進数の byte を解いた元の path として読まれること</summary>
    [Test]
    public async Task Quoted_paths_are_read_as_the_original_path()
    {
        string[] diff =
        [
            "diff --git \"a/d\\\"q\\303\\244.cs\" \"b/d\\\"q\\303\\244.cs\"",
            "--- \"a/d\\\"q\\303\\244.cs\"",
            "+++ \"b/d\\\"q\\303\\244.cs\"",
            "@@ -1 +1 @@",
            "-a",
            "+b",
        ];
        var changes = new ChangedLines(UnifiedDiff.Parse("/repo", diff));

        await Assert.That(changes.Overlaps("/repo/d\"q\u00e4.cs", 1, 1)).IsTrue();
    }
}
