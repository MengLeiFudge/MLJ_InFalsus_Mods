extern alias GameData;

using System.Globalization;
using System.Reflection;
using System.Text.Json;
using ifapp.Game.Data;
using Str;
using UnityEngine;
using SaveOwner = GameData::_K._NH;

namespace InFalsusMod;

/// <summary>按游戏当前语言提供模组文案；资源列顺序固定为简中、繁中、英、日、韩。</summary>
internal static class ModText
{
    /// <summary>模组自有文案，不写入游戏字符串表。</summary>
    private static readonly Dictionary<string, string[]> messages = Load();
    /// <summary>用于识别领域异常的原始简中文案。</summary>
    private static readonly Dictionary<string, string> errorKeys = messages.Where(pair => pair.Key.StartsWith("Error", StringComparison.Ordinal))
        .ToDictionary(pair => pair.Value[0], pair => pair.Key);
    /// <summary>语言读取按Unity帧缓存，避免每个标签反复跨入原生代码。</summary>
    private static int frame = -1;
    /// <summary>本帧有效语言。</summary>
    private static Strings.Localization language = Strings.Localization.English;
    /// <summary>特性译名缓存的语言。</summary>
    private static Strings.Localization traitLanguage;
    /// <summary>键仍为稳定Family身份，值为当前语言的游戏译名。</summary>
    private static readonly Dictionary<string, string> traitNames = new();

    /// <summary>游戏实际选择的语言；未设置时采用游戏的系统语言映射。</summary>
    internal static Strings.Localization Language
    {
        get
        {
            if (frame != Time.frameCount)
            {
                frame = Time.frameCount;
                language = Strings.GetCurrentLocalization();
                if (language == Strings.Localization.Unset)
                    language = Strings.GetLocalizationForSystemLanguage();
            }
            return language;
        }
    }

    /// <summary>读取并检查嵌入资源的五语结构。</summary>
    /// <returns>按稳定键索引的文案。</returns>
    private static Dictionary<string, string[]> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("InFalsusMod.Data.ui-text.json")
            ?? throw new InvalidOperationException("Missing mod localization resource.");
        var entries = JsonSerializer.Deserialize<Dictionary<string, string[]>>(stream)
            ?? throw new InvalidOperationException("Empty mod localization resource.");
        if (entries.Values.Any(values => values.Length != 5 || values.Any(string.IsNullOrWhiteSpace)))
            throw new InvalidOperationException("Incomplete mod localization resource.");
        return entries;
    }

    /// <summary>取得当前语言的文案并使用对应数字格式填入参数。</summary>
    /// <param name="key">资源中的稳定文案键。</param>
    /// <param name="args">格式参数，未格式化时为空。</param>
    /// <returns>可直接显示的本地化文案。</returns>
    internal static string Get(string key, params object[] args)
    {
        var (column, culture) = Language switch
        {
            Strings.Localization.ChineseSC => (0, "zh-CN"),
            Strings.Localization.ChineseTC => (1, "zh-TW"),
            Strings.Localization.Japanese => (3, "ja-JP"),
            Strings.Localization.Korean => (4, "ko-KR"),
            _ => (2, "en-US")
        };
        string text = messages[key][column];
        return args.Length == 0 ? text : string.Format(CultureInfo.GetCultureInfo(culture), text, args);
    }

    /// <summary>将操作失败转换为用户语言；技术异常原文由调用者完整写日志。</summary>
    /// <param name="exception">领域校验或底层失败。</param>
    /// <returns>本地化原因或日志指引。</returns>
    internal static string Error(Exception exception)
    {
        if (errorKeys.TryGetValue(exception.Message, out var key))
            return Get(key);
        // 这些消息包含动态编号或来自结构校验；保留其诊断原文到日志，界面说明失败类别。
        if (exception is InvalidOperationException && new[] { "粒子", "游戏粒子", "技能", "回想", "融合", "强化" }
            .Any(prefix => exception.Message.StartsWith(prefix, StringComparison.Ordinal)))
            return Get("ErrorRules");
        return Get("ErrorUnknown");
    }

    /// <summary>读取游戏当前语言的特性名称，筛选身份不随译名改变。</summary>
    /// <param name="family">原有稳定同名特性分组。</param>
    /// <returns>该组最低等级的游戏译名，去掉等级后缀。</returns>
    internal static string TraitName(string family)
    {
        var current = Language;
        if (traitLanguage != current)
        {
            traitNames.Clear();
            traitLanguage = current;
        }
        if (traitNames.TryGetValue(family, out var name))
            return name;
        var info = TraitCatalog.Entries.Where(entry => entry.Family == family).OrderBy(entry => entry.Tier).First();
        var mapping = SaveOwner._CEb?._VNA()?.DynamicStringMapping;
        if (mapping == null)
            return Get("TraitId", info.Id);
        name = mapping.Get(DynamicStringTypeFlags.TraitName, current, new TraitId { Value = (short)info.Id }).CreateString();
        if (string.IsNullOrWhiteSpace(name))
            return Get("TraitId", info.Id);
        foreach (var suffix in new[] { " III", " II", " I" })
            if (name.EndsWith(suffix, StringComparison.Ordinal))
            {
                name = name[..^suffix.Length];
                break;
            }
        traitNames.Add(family, name);
        return name;
    }
}
