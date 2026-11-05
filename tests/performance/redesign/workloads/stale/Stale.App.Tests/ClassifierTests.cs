using Stale.App;
using Xunit;

namespace Stale.App.Tests;

public class ClassifierTests
{
    [Fact] public void Positive() => Assert.Equal("pos", Classifier.Classify(5));

    [Fact] public void Negative() => Assert.Equal("neg", Classifier.Classify(-5));
}
