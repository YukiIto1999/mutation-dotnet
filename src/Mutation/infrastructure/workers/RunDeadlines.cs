namespace Mutation.Infrastructure.Workers;

/// <summary>worker の run 応答を待つ上限の決め方</summary>
/// <param name="Grace">実行時間予算に足す、応答の往復の猶予</param>
public sealed record RunDeadlines(TimeSpan Grace)
{
    /// <summary>通常の猶予。probe を通らない無限ループも、この期限と worker の終了で捕らえる</summary>
    public static RunDeadlines Standard { get; } = new(TimeSpan.FromSeconds(5));

    /// <summary>run 応答を待つ上限。実行時間予算に猶予を足した値</summary>
    /// <param name="budgetMs">変異一件の実行時間予算</param>
    /// <returns>これを超えたら worker を時間切れとみなす上限</returns>
    public TimeSpan For(double budgetMs) => TimeSpan.FromMilliseconds(budgetMs) + Grace;
}
