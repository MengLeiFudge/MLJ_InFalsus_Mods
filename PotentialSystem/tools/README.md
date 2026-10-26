# PTT 字形素材

在仓库根目录运行 `python3 PotentialSystem/tools/build-rating-font.py`，重新生成 `Assets/Fonts/RatingGlyphs.png` 和 `RatingGlyphs.json`。构建素材需要 Pillow；游戏运行不需要 Python 或 Pillow。

字体原件为 `Assets/Fonts/Exo-SemiBold.ttf`，与 Arcaea 7.0.255c APK 的 `assets/Fonts/Exo-SemiBold.ttf` 逐字节相同。生成程序校验原件的 SHA256。图集保存四倍分辨率的白色字面和黑色描边，JSON 保存前进宽度、基线偏移与字偶距。游戏合成时将描边着色，按徽章大小等比缩放，不根据字符串长度缩小字号。

布局依据为该 APK 的 `layouts/mainmenu/network/UserCell.csb` 和 arm64 `libcocos2dcpp.so`：

| 项目 | 来源与数值 |
| --- | --- |
| 字体与字号 | `rating_text_large` 为 Exo-SemiBold 34；`rating_text_small` 为 Exo-SemiBold 24 |
| 文字锚点 | 大字 `(1,0)`，位置 `(2,-3.5)`；小字 `(0,0)`，位置 `(0,0)` |
| 纵向基准 | 文字容器 y=3.5，徽章中心 y=22.5 |
| 横向居中 | 函数 `0x7e937c`：容器 x=徽章中心 x−(小字宽−大字宽)/2 |
| 描边 | 同一函数：宽度 3；通常 RGB(81,72,92)，PTT≥13 时 RGB(104,9,52) |
| 字体基线 | Exo `hhea.descender=-328`，`head.unitsPerEm=1000` |

`PotentialRatingText` 只负责选曲入口的数字图形。它与徽章共享父坐标，数值变化时重新合成透明纹理，尺寸变化时只调整显示大小；关闭页面时释放图形、材质和纹理。

昵称由 `PotentialNameText` 独立绘制。ASCII 昵称使用 DLL 中的 `GeosansLight.ttf`；该字体不含中文，其他昵称沿用 APK 的 `NotoSansCJKsc-Regular.otf`。两份字体均与 APK 原件一致。中文回退选型是模组的兼容处理，尚未确认 Arcaea 原生中文昵称的字体切换路径。

`UserCell.csb` 的 username 字号为 45。函数 `0x854078` 为昵称添加宽度 1、与文字同色的描边，以及偏移 `(1,-2)`、模糊 0 的阴影。正文颜色来自 `0xb31cdc`，阴影颜色来自 `0x1416f74`。当前四款名片的颜色如下，透明度均为 255：

| 模组配色 | APK 资源 | 正文及描边 | 阴影 |
| --- | --- | --- | --- |
| 青 | course_banner_4 | #344961 | #F2F3F5 |
| 紫 | course_banner_12 | #FFFFFF | #D59F78 |
| 红 | course_banner_8 | #FFFFFF | #4B1A79 |
| 蓝 | course_banner_2 | #38505C | #FFFFFF |

颜色随名片款式切换，不随昵称语言或 PTT 数值切换。Windows 位图画布临时注册嵌入字体，按四倍分辨率描绘字形轮廓，再合成透明纹理；字体不安装到系统。正常昵称保持 45 字号，过长时才缩至横幅可用宽度。名字或配色变化后重建纹理，页面关闭时释放图形资源。GDI 与 Cocos 的字形栅格化实现不同，不能据静态参数相同声称逐像素一致。
