namespace Mutation.Domain;

/// <summary>前回の判定を再利用してよいかを見分ける、対象一件の入力の同一性</summary>
/// <remarks>準備から判定の確定と保存まで、二つの値の対のまま運ぶ。保存形は対から必要な側だけを写す</remarks>
/// <param name="Closure">判定を決める入力の閉包の同一性。変異の集合を絞る差分の行を含まない。変異ごとの継承はこれが一致するときだけ許す</param>
/// <param name="Fingerprint">閉包の同一性に、対象のソースごとの変わった行の範囲を加えた全体の指紋。全体の再構築はこれが一致するときだけ許す</param>
public sealed record ReuseIdentity(string Closure, string Fingerprint);
