namespace Frameworks.Target;

/// <summary>フレームワーク横断の検証に使う小さな計算</summary>
public static class Calc
{
    /// <summary>二値の加算</summary>
    public static int Add(int a, int b) => a + b;

    /// <summary>閾値 10 を超えるかの判定</summary>
    public static bool Above(int value) => value > 10;

    /// <summary>どのテストも触らない計算</summary>
    public static int Untested(int value) => value * 3;
}
