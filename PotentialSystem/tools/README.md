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

昵称使用 DLL 中的独立字体资源：ASCII 昵称使用 `GeosansLight.ttf`，含中日韩字符时使用 `NotoSansCJKsc-Regular.otf`。这两个文件同样来自 APK。昵称显式选用常规字重，不继承游戏文字模板的字重或富文本标签样式。
