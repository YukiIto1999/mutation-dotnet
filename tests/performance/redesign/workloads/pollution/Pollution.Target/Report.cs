namespace Pollution.Target;

/// v は常に正(key > 0 の範囲)なので `v < 0` → `v <= 0` は等価 mutant(真の判定は Survived)
public static class Report
{
    public static string Describe(int key)
    {
        var v = Memo.Get(key);
        return v < 0 ? "neg" : $"{key}:{v}";
    }
}
