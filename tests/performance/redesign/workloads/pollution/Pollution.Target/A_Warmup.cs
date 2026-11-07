namespace Pollution.Target;

/// 先に実行される mutant の検査が、正しい値で memo を温める
public static class Warmup
{
    public static int Peek(int key) => Memo.Get(key) + 0;
}
