using Mutation.Mutating.Infrastructure.Git;
using TUnit.Core;

namespace Mutation.Tests;

public sealed partial class ChangedLinesGateFacts
{
    /// <summary>消えたファイルと、行の差分を持たない binary のファイルが、変わったファイルに含まれ、消えたファイルが消えたものとして載ること</summary>
    [Test]
    public async Task Deleted_and_binary_files_are_changed_files_and_the_deleted_one_is_listed_as_deleted()
    {
        using var repo = await TempRepository.CreateAsync();
        await repo.WriteAsync("src/Kept.cs", "kept\n");
        await repo.WriteAsync("src/Gone.cs", "gone\n");
        await File.WriteAllBytesAsync(repo.PathOf("src/blob.bin"), [0, 1, 2, 0]);
        var baseRef = await repo.CommitAllAsync();
        File.Delete(repo.PathOf("src/Gone.cs"));
        await File.WriteAllBytesAsync(repo.PathOf("src/blob.bin"), [0, 9, 9, 0]);

        var resolved = ChangedFiles.Resolve(repo.Root, baseRef);

        await Assert.That(resolved.IsFailed(out _, out var changes)).IsFalse();
        await Assert.That(changes!.Files.Order(StringComparer.Ordinal).ToArray())
            .IsEquivalentTo([repo.PathOf("src/Gone.cs"), repo.PathOf("src/blob.bin")]);
        await Assert.That(changes.Deleted.ToArray()).IsEquivalentTo([repo.PathOf("src/Gone.cs")]);
        await Assert.That(changes.RangesOf(repo.PathOf("src/blob.bin"))).IsEmpty();
    }

    /// <summary>移動したファイルの移動元が消えたものとして載り、移動先が変わった行を持つこと</summary>
    [Test]
    public async Task A_moved_file_leaves_its_old_path_deleted_and_its_new_path_changed()
    {
        using var repo = await TempRepository.CreateAsync();
        await repo.WriteAsync("src/Moving.cs", NumberedLines("moving"));
        var baseRef = await repo.CommitAllAsync();
        await repo.MoveAsync("src/Moving.cs", "src/moved/Moving.cs");

        var resolved = ChangedFiles.Resolve(repo.Root, baseRef);

        await Assert.That(resolved.IsFailed(out _, out var changes)).IsFalse();
        await Assert.That(changes!.Deleted.ToArray()).IsEquivalentTo([repo.PathOf("src/Moving.cs")]);
        await Assert.That(changes.Overlaps(repo.PathOf("src/moved/Moving.cs"), 1, 1)).IsTrue();
        await Assert.That(changes.Deleted.Contains(repo.PathOf("src/moved/Moving.cs"))).IsFalse();
    }
}
