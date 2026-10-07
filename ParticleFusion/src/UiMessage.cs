namespace InFalsusMod;

/// <summary>保存消息身份和参数，显示时使用最新游戏语言；不保留库存或原生对象引用。</summary>
internal sealed class UiMessage
{
    /// <summary>本地化资源键。</summary>
    private readonly string key;
    /// <summary>结果数字或异常信息，不随游戏后续库存变化重新计算。</summary>
    private readonly object[] args;
    /// <summary>提交已完成但刷新失败等附加提示的资源键。</summary>
    internal string? Warning { get; set; }

    /// <summary>捕获一条可随语言切换重新呈现的消息。</summary>
    /// <param name="key">资源键。</param>
    /// <param name="args">格式参数；异常在呈现时转换为玩家语言。</param>
    internal UiMessage(string key, params object[] args)
    {
        this.key = key;
        this.args = args;
    }

    /// <summary>使用当前语言生成界面或日志文字。</summary>
    /// <returns>主消息及可选警告。</returns>
    public override string ToString()
    {
        var values = args.Select(value => value is Exception exception ? ModText.Error(exception) : value).ToArray();
        string text = ModText.Get(key, values);
        return Warning == null ? text : text + "\n" + ModText.Get(Warning);
    }
}
