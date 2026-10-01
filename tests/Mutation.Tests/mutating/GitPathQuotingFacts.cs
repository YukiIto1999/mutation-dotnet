using Mutation.Mutating.Infrastructure.Git;
using TUnit.Core;

namespace Mutation.Tests;

/// <summary>git が C 形式で引用した path の復元の検査</summary>
public sealed class GitPathQuotingFacts
{
    /// <summary>両端が引用符でない path が、そのまま返ること</summary>
    [Test]
    [Arguments("b/dir/A.cs")]
    [Arguments("\"")]
    [Arguments("\"b/open.cs")]
    [Arguments("b/close.cs\"")]
    public async Task Paths_not_wrapped_in_quotes_stay_as_they_are(string path)
    {
        await Assert.That(GitPathQuoting.Unquote(path)).IsEqualTo(path);
    }

    /// <summary>引用符だけの path が空の path に戻ること</summary>
    [Test]
    public async Task Empty_quoted_path_becomes_empty()
    {
        await Assert.That(GitPathQuoting.Unquote("\"\"")).IsEqualTo("");
    }

    /// <summary>名前のある escape が、それぞれの制御文字と引用符と逆斜線に戻ること</summary>
    [Test]
    public async Task Named_escapes_become_their_characters()
    {
        var unquoted = GitPathQuoting.Unquote("\"\\a\\b\\t\\n\\v\\f\\r\\\"\\\\\"");

        await Assert.That(unquoted).IsEqualTo("\a\b\t\n\v\f\r\"\\");
    }

    /// <summary>8 進数の byte 列が UTF-8 として、前後の文字とつながって戻ること</summary>
    [Test]
    public async Task Octal_bytes_decode_as_utf8_between_plain_text()
    {
        await Assert.That(GitPathQuoting.Unquote("\"b/x\\303\\244y\\tz.cs\"")).IsEqualTo("b/x\u00e4y\tz.cs");
    }
}
