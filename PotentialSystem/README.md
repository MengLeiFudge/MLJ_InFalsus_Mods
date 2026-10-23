# PotentialSystem · 潜力值系统

为 In Falsus 添加基于 Arcaea 新版规则的潜力值（PTT）。当前版本 1.0.1，适用于 Windows IL2CPP 版游戏 1.0.7 和 BepInEx 6。

## 功能

- 选歌界面顶部中央显示所选角色头像菱形，上角裁至游戏上边缘，右下徽章显示账号 PTT。头像和徽章按 Arcaea 原始布局比例组合，数字使用 Exo-SemiBold 的大整数、小小数样式，并共同适配徽章宽度。
- 左右歌曲卡片上方显示实际绑定难度的定数与潜力值：未游玩时为 `10.0`，已游玩时为 `#10 11.1 -> 11.123`，依次表示完整成绩排名、定数、单曲潜力值；文字按单曲 PTT 档位配色，排名包含超过 B50 的成绩。
- 选中谱面使用与返回按钮隔开的深色成绩区，显示难度、定数、单曲 PTT、完整排名、对应纪录的打击分数、完成等级、本地时间与定数来源；未知时间明确标注，不拼接其他局的成绩。
- 点击顶部头像或 PTT 菱形打开全部最佳成绩页面，命中区域按各自菱形轮廓及游戏显示边界限定。横向卡片排成五列，左侧显示曲绘，右侧显示歌名、作者、难度、定数、大号打击分数、通关状态、排名和单曲潜力值；卡片与 B50 导出图共用绘制，圆角、字体与文字位置一致，背景及边框使用曲绘的深色印象色。页头仅显示 PTT、B10、B50。支持滚动与歌名、作者、难度搜索，列表包含全部已游玩谱面，每个歌曲难度保留一项最佳表现。
- 成绩页提供“导出 B50 到剪贴板”按钮，按成绩和游戏封面独立生成五列分享图片，包含账号 PTT、动态上限及 B50/B10 均值，可直接粘贴到聊天或图片应用。
- 原版排序按钮的切换序列中依次加入“歌曲定数 升序”“歌曲定数 降序”“歌曲潜力值 升序”“歌曲潜力值 降序”，沿用原按钮的双向切换，保留所有原版排序选项。均按歌曲行实际难度排序：定数使用当前设置的来源，包含未游玩谱面；歌曲潜力值使用该谱面的最佳单曲 PTT，未游玩的歌曲始终排在已游玩歌曲之后。
- 结算界面顶部中央显示账号 PTT 徽章，右侧显示增加值或 Keep，左侧显示单曲旧最佳、更新后最佳及本次潜力值。入场稍候后，账号数值从旧值滚动到新值。
- 在原生“游戏和 UI 设置”中增加“潜力值系统”分类与“谱面定数来源”左右箭头选项，依次为官方等级、安小璃、萌泪酱，默认安小璃。官方等级直接作为定数；安小璃在每次启动时后台检查更新，萌泪酱使用 v6 内置表并离线计算新谱面。所有定数始终显示一位小数。
- 直接读取当前 SongData 的可用谱面与稳定 Id；游戏新增谱面自动纳入，尚未收录或计算失败时暂用原生 Rating。切换来源会重算卡片、排名、账号 PTT、上限、结算、Best 与导出数据，保留原始成绩和时间。
- 每次载入账号从完整逐局 history 计算最佳 PTT，并在历史变化时同步。独立文件保存对应真实纪录的已知 UTC 时间，关闭模组期间的成绩也可从历史补入。
- 模组成绩按原版存档目录隔离，不改变原版 .sav 格式。

选歌入口、排名、设置及成绩页控件使用原生 FastText、布局与按钮输入，中文字体由 DLL 内嵌的 NotoSansCJKsc 提供。成绩卡片与导出图共用 Windows 绘图和系统字体，只为可见行保留卡片纹理。成绩页的窗口和滚动列表以游戏显示区域为边界，在 16:10 屏幕上也保持在游戏的 16:9 区域内；可使用滚轮、拖动列表或右侧滚动条浏览。点击搜索框后输入歌名、作者或难度，支持中文输入、方向键、退格及 Ctrl+A/C/X/V；按 Enter 结束输入，点击右上角红叉或按 Escape 返回选歌。页面打开时持有独立输入，关闭后归还选歌输入。排序模式只在当前选歌场景生效，不写入原版排序枚举。

导出图片根据完整成绩快照取前 50 项，独立绘图，不受游戏分辨率、当前选歌或成绩页滚动位置影响。分享图采用五列横向圆角卡片，卡片背景与成绩页共用曲绘印象色，浅色文字保持清晰。已有成绩不足 50 项时按实际数量展示。封面加载或剪贴板被占用时明确提示导出失败；关闭页面取消尚未完成的导出。剪贴板只在玩家点击导出按钮且图片生成完成后更新。

## 定数来源与设置

路径：游戏和 UI 设置 → 潜力值系统 → 谱面定数来源。使用左右箭头切换官方等级、安小璃、萌泪酱，原生滚轮或右侧滚动条可滚至完整选项行。选项保存在当前 BepInEx profile 的 `config/menglei.infalsus.potentialsystem.cfg`，键为 `PotentialSystem/ChartConstantSource`：0 为官方等级、1 为安小璃、2 为萌泪酱。首次使用默认为 1，后续启动保留选择；旧布尔键不再控制此项，升级后尚未保存新选择时使用安小璃。

| 来源 | 定数规则 |
| --- | --- |
| 官方等级 | 定数等于游戏当前 `SongChartInfo.Rating`。 |
| 安小璃（默认） | 读取 [In Falsus Tools 的 experimental.json](https://github.com/Anling20/InFalsusTools/blob/master/config/levels/experimental.json)，按歌曲 ID 与难度匹配；DLL 内置一份离线副本。 |
| 萌泪酱 | v6 四难度独立模型，内置当前 320 个谱面结果；新谱面在后台离线读取本机谱面文件计算。 |

两份定数都嵌入 DLL：安小璃按歌曲 ID 与难度匹配，萌泪酱按原生谱面 ID、难度和官方等级匹配。运行时无需 Python、工作簿或研究工具。B50 分享图标明当前定数来源。

每次启动先使用本地定数，同时在后台读取一次安小璃数据，请求总超时为 15 秒。曲库就绪后，萌泪酱只计算尚未收录或官方等级改变的谱面，不重新训练模型。已有结果从内置表或缓存读取；同一 ID、难度和等级的谱面不会重复读谱。单次后台计算最多两分钟，已完成结果保存，未完成项下次启动重试。缺失 DLC 文件、未知谱面格式或计算失败时暂用官方等级，原因写入 BepInEx 日志。

两边都结束后，在选歌界面空闲时统一应用并最多弹一次窗，分别显示安小璃、萌泪酱的变化数量，不显示具体清单。仅统计当前可用谱面的实际生效定数变化；全为零、仅格式变化或仅未进入当前曲库的条目变化时不弹窗。网络失败保留安小璃本地定数，不阻止萌泪酱结果生效。

缓存位于当前 profile 的 `BepInEx/config/PotentialSystem/`：`anxiaoli-cache.json` 和 `menglei-v6-cache.json`，原子更新时保留上一版 `.bak`。缓存与内置表配套；萌泪酱还核对模型指纹。模组更换内置数据或模型后，旧缓存不会覆盖新版本。没有可用缓存时使用 DLL 内置副本，因此断网也可运行。

萌泪酱 v6 的结果限制在官方等级 `L` 到 `L+0.9` 内：

| 官方等级 | 可用小数档位 |
| --- | --- |
| 低于 7 | .0、.5 |
| 7、8 | .0、.5、.8 |
| 9 及以上 | .0～.9，每 0.1 一档 |

取最近合法档位，等距取较高档，不进位到下一等级。MIN、EVO 使用各自结构公式；ULT 在 10～12 级、FBD 在 10～14 级使用各自独立的线性与核回归，其他等级使用对应难度的结构公式。缺少同级结构中心时，以 `L+0.45` 为连续估计再分档。社区定数用于趋势参照；低难度缺少细分社区标签，结果属于结构估计。上述边界与分档只约束萌泪酱，安小璃保留上游原值。

## 计算方式

每首歌的不同难度作为不同谱面。游戏 PlayerScore 除以 10 后对应 Arcaea 的分数尺度；当前来源的谱面定数为 C，归一化分数为 A，本次成绩完成等级达到 DiveCleared（含 FullLink、PerfectDive）时 bonus=0.2，否则为 0。原生 Lamp 仅按 Pace 判断，可能与未判定音符导致的 DiveFailed 不一致，因此 PTT 的通关状态统一由完成等级决定：

```text
A >= 10,000,000：C + 2 + bonus
9,800,000 <= A < 10,000,000：C + 1 + (A - 9,800,000) / 200,000 + bonus
A < 9,800,000：max(0, C + (A - 9,500,000) / 300,000 + bonus)
```

保存每谱面分数增量最大的原始成绩，再以当前定数计算单曲潜力值。取最高的 50 项作为 B50，B10 为其中最高的 10 项：

```text
PTT = (B50 单曲潜力值总和 + B10 单曲潜力值总和) / 60
```

分母始终为 60，成绩不足时缺项贡献零。同一定数表及曲库下，每个谱面的最高值只升不降，因此账号 PTT 只升不降。切换定数来源、后台定数更新，或游戏调整已有谱面等级、可用状态及身份时，模组按当前数据重新计算，PTT 可能上升或下降。

定数固定显示一位小数，例如 10 级显示 `10.0`；单曲及账号 PTT 截断到三位小数，内部计算保留 decimal 精度。结算增加值按显示前后的差计算；显示未变化时为 Keep。切换定数来源后，已打开的结算页更新当前数值，不把来源变化显示为一次新成绩。

理论上限使用当前所有可用谱面的 C+2.2 套用同一 B50/B10 公式计算，随曲库及定数来源变化。

徽章使用以下固定门槛；达到每档下界时切换。选歌、结算、完整成绩浏览和 B50 导出共用同一规则。

| 本游戏潜力值范围 | 徽章外观 | 对应 Arcaea 原门槛 |
| --- | --- | --- |
| 0.000～3.999 | 青色，无星 | 0.000 |
| 4.000～7.999 | 绿色，无星 | 3.500 |
| 8.000～10.999 | 浅紫色，无星 | 7.000 |
| 11.000～11.999 | 紫色，外围光效，无星 | 10.000 |
| 12.000～12.999 | 红色，外围光效，无星 | 11.000 |
| 13.000～13.499 | 粉红色，一星 | 12.000 |
| 13.500～13.999 | 粉红色，二星 | 12.500 |
| 14.000 及以上 | 玫红色，圆形图案，三星 | 13.000 |

## 安装

需要 BepInEx-BepInExPack_IL2CPP-6.0.755。将 PotentialSystem.dll 放入当前启动方式使用的 BepInEx/plugins/MengLei-PotentialSystem 目录，重启游戏后进入选歌界面。

从本仓库编译时，DLL 位于 PotentialSystem/bin/Release/PotentialSystem.dll。安装该 DLL 即可运行，不依赖本仓库其他模组。

## 成绩文件与异常

独立记录保存在 `savestate_V3.sav` 相同目录的 `savestate_ptt_v1.json`，不写入原生存档。正常替换时，上一版保留为 `savestate_ptt_v1.json.bak`；文件归属于原版账号/存档目录，切换 BepInEx profile 不会混用其他 Steam 账号的成绩。

每次载入账号均遍历 history 的实际 length，逐局使用同一次成绩的分数与完成等级，按歌曲／难度计算最高表现。较低分的通关成绩仍可能比更高分的未通关成绩有更高 PTT。当前有效谱面的数值从历史重建；独立文件只为歌曲、难度、谱面身份、分数和完成等级一致的真实成绩提供已知时间，暂时移除的谱面继续归档。

同步同时检查原生成绩对象、更新号和 history 实际条数。游戏未破最高分或完成等级纪录时，updateId 不增加，但逐局历史仍然追加，因此须以条数识别变化。结算先捕获本局 UTC 时间和更新前数值，再同步历史，保持增加动画。

根字段为 `schemaVersion: 1`、`rule`、UTC `importedAt` 和 `bestPlays`。官方等级的 rule 为 `integer-rating-v1`，安小璃为 `anxiaoli-v1`，萌泪酱为 `menglei-v6`；同时兼容旧 `menglei-v4`、`community-experimental-v1` 和整数记录，读取后按当前来源重算。每条记录包含 `songId`、`difficulty`（1/2/4/8）、`chartId`、`score`（十进制字符串）、`lamp`（用于 PTT 的通关状态）、`resultClear`、`constant`、未截断 `potential`、`achievedAt` 和 `source`。`history` 来源为原生真实历史，`play` 为模组捕获的真实结算及时间；原生时间为零或无效时 `achievedAt` 为 null。`importedAt` 仅表示首次创建文件的时间。

新结算由模组捕获 UTC 时间，表现更高时更新记录；相同表现优先已知的较早时间，历史同步不会用 null 覆盖已知时间。关闭模组期间的游玩也会补入，无法还原的完成时间保持 null。定数来源、定数表或游戏等级改变时，文件中的定数与 PTT 同步重算，保留成绩和取得时间。外部读取者须识别当前 rule 及小数 constant，不能把社区定数来源按整数方案解释。

旧 `PotentialSystem/integer-v1.json` 不再读取或生成。新文件成功写入后，旧文件及其 `.bak` 会移为同目录的 `savestate_ptt_v1.json.legacy.<唯一标识>.bak`，移除旧路径并保留可恢复数据。旧格式不作为可靠最佳成绩导入。已有 v1 文件中的 `source: "summary"` 条目由加载器识别后丢弃，当前谱面随完整历史重建；保存前一版仍由 `.bak` 保留。新文档只输出真实 `history`／`play` 纪录。

读取失败或发现不支持的格式时，模组保留原文件并显示 Data error，不把坏档当作空档覆盖。请查看 BepInEx 日志，备份并修复文件后重启游戏。备份文件由用户判断后恢复，模组不自动用较旧备份替换当前成绩。

写入或旧格式归档失败时，待处理状态保留在内存，并显示 Unsaved。新的场景刷新或结算事件会再尝试保存；成绩可在下次载入时从 history 恢复，但尚未成功保存的模组 UTC 时间无法由原生存档补回。

首次安装或在已经打开的结算界面加载插件时，已有原生成绩作为初始基线，不把它们伪装成本次新增 PTT；之后的结算显示正常增加动画。当前原版历史列表的 1024 是初始容量，写满会扩容，模组读取实际 length。历史缺失或结构无效时停止该账号的计算并报告错误；有效的空历史可生成零成绩文件。

## 参考

- [Arcaea 中文 Wiki：潜力值](https://wiki.arcaea.cn/index.php/潜力值)
- [In Falsus B50](https://github.com/unknnownnn003/infalsus-b50)：核实整数 Rating 的使用方式、分数尺度与 B50/B10 规则。
- [v6 模型说明](Assets/定数测量/v6/README.md)：四难度模型、分档、适用范围和社区趋势对照。
- [小数定数估算研究交接](../docs/chart-constant-estimation-handoff.md)：独立研究的目标、可用原生数据和谱面解码资料。

潜力值徽章图片原作来自 Arcaea / lowiro，档位图片对应中文 Wiki 的 Rating_0～Rating_7 和 Rating_off。实际档位素材取自 [SkyDynamic/nonebot-plugin-arcaeabot](https://github.com/SkyDynamic/nonebot-plugin-arcaeabot/tree/49ce0c7f4f95107caf6c9aa3443066222eca76b7/nonebot_plugin_arcaeabot/resource/ptt)，固定于提交 `49ce0c7f4f95107caf6c9aa3443066222eca76b7`，保留透明背景并嵌入 DLL。嵌入字体取自用户提供的 Arcaea 7.0.255c APK 内 `assets/Fonts/`，中文显示使用 NotoSansCJKsc，账号数字使用 Exo-SemiBold。头像白色边框取自同一 APK 内 `assets/img/1080/char_icon_border_white-selected.png`；深色菱形底为本模组绘制。头像框、头像、徽章的 150:128:85 比例、徽章相对偏移 (56,-37.5) 和 34:24 字号比例来自 `UserCell.csb`，成绩区参考 `HighscoreEntry.csb` 的 433×110 横向结构，再适配本游戏的信息量和导航区。角色头像使用当前游戏的角色图标材质，身份取自原生存档 SelectedCharacter；歌曲封面和名称从当前游戏加载。

SAM/ICP1 解码改编自 [REDDRAGON-HL/InFalsus-Resource](https://github.com/REDDRAGON-HL/InFalsus-Resource) 提交 `71cdbfe6e9e4959605a39bdfd53192a61ebb290c` 的 `spc_to_json.py`、`extract_infalsus.py`。许可证同时嵌入 DLL：

```text
MIT License

Copyright (c) 2026 RedDragon

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
