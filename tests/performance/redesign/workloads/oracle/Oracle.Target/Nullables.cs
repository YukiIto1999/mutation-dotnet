namespace Oracle.Target;

/// null 合体、null 条件、nullable 値型
public sealed class Profile
{
    public string? Nickname { get; set; }

    public int? Age { get; set; }

    public Profile? Manager { get; set; }

    public string DisplayName(string fallback) => Nickname ?? fallback;

    public int AgeOrZero() => Age ?? 0;

    public string? ManagerName() => Manager?.Nickname;

    public void EnsureNickname(string value) => Nickname ??= value;
}
