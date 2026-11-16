using Stale.Core;

namespace Stale.App;

public static class Classifier
{
    public static string Classify(int x) => Normalizer.Normalize(x) >= 0 ? "pos" : "neg";
}
