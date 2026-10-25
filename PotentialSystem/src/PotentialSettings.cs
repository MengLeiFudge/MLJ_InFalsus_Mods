using BepInEx.Configuration;

namespace InFalsusMod.Potential;

/// <summary>潜力值与个性化的 profile 配置；界面由模组页面持有，不再注入游戏通用设置。</summary>
internal sealed class PotentialSettings
{
    private readonly ConfigEntry<int> source, selectionSort, browserSort, browserDifficulty, portrait, namecardColor;
    private readonly ConfigEntry<string> profileName;

    /// <summary>沿用既有配置键，不改写游戏存档。</summary>
    internal PotentialSettings(ConfigFile config)
    {
        source = config.Bind("PotentialSystem", "ChartConstantSource", (int)ChartConstantSource.AnXiaoLi,
            new ConfigDescription("谱面定数来源：0 官方等级、1 安小璃（默认）、2 萌泪酱；缺失定数回退等级。",
                new AcceptableValueList<int>(Enum.GetValues<ChartConstantSource>().Select(value => (int)value).ToArray())));
        selectionSort = config.Bind("PotentialSystem", "SelectionSort", 0,
            new ConfigDescription("选歌排序：0 原版，1 定数升序，2 定数降序，3 PTT 升序，4 PTT 降序。", new AcceptableValueRange<int>(0, 4)));
        browserSort = config.Bind("PotentialSystem", "BrowserSort", 0,
            new ConfigDescription("成绩页排序：0 PTT降序，1 定数升序，2 定数降序，3 分数降序，4 歌名。", new AcceptableValueRange<int>(0, 4)));
        browserDifficulty = config.Bind("PotentialSystem", "BrowserDifficulty", 0,
            new ConfigDescription("成绩页难度：0 全部，1 MIN，2 EVO，4 ULT，8 FBD。", new AcceptableValueList<int>(0, 1, 2, 4, 8)));
        portrait = config.Bind("Appearance", "PortraitCharacter", -1,
            new ConfigDescription("头像角色编号；-1 跟随游戏当前角色。", new AcceptableValueRange<int>(-1, 25)));
        namecardColor = config.Bind("Appearance", "NamecardColor", 0,
            new ConfigDescription("名片配色：0 青，1 紫，2 红，3 蓝。", new AcceptableValueRange<int>(0, 3)));
        profileName = config.Bind("Appearance", "ProfileName", "", "名片昵称，留空使用角色名称。");
    }

    internal ChartConstantSource Source => (ChartConstantSource)source.Value;
    internal int SelectionSort { get => selectionSort.Value; set => selectionSort.Value = value; }
    internal int BrowserSort { get => browserSort.Value; set => browserSort.Value = value; }
    internal int BrowserDifficulty { get => browserDifficulty.Value; set => browserDifficulty.Value = value; }
    internal int Portrait { get => portrait.Value; set => portrait.Value = value; }
    internal int NamecardColor { get => namecardColor.Value; set => namecardColor.Value = value; }
    internal string ProfileName { get => profileName.Value; set => profileName.Value = value; }

    /// <summary>原生左右箭头提交合法来源；数据更新仍由原控制器处理。</summary>
    internal void SetSource(int index)
    {
        if (!Enum.IsDefined(typeof(ChartConstantSource), index))
            throw new ArgumentOutOfRangeException(nameof(index));
        source.Value = index;
    }

    internal void CycleSource() => SetSource((source.Value + 1) % Enum.GetValues<ChartConstantSource>().Length);
}
