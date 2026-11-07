using Pollution.Target;
using Xunit;

namespace Pollution.Target.Tests;

public class A_WarmupTests
{
    [Fact] public void Peek_three() => Assert.Equal(6, Warmup.Peek(3));
}

public class MemoTests
{
    [Fact] public void Get_three() => Assert.Equal(6, Memo.Get(3));
}

public class ReportTests
{
    [Fact] public void Describe_three() => Assert.Equal("3:6", Report.Describe(3));
}
