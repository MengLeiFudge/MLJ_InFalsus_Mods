# 账号区域资源与布局

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

昵称由 `PotentialNameText` 独立绘制。ASCII 昵称使用 DLL 中的 `GeosansLight.ttf`；该字体不含中文，其他昵称使用 APK 的 `NotoSansCJKsc-Light.otf`，GDI 字重为 300，描边降为 1/4 原始单位，以保留细笔画。两份字体均与 APK 原件一致。中文回退选型是模组的兼容处理，尚未确认 Arcaea 原生中文昵称的字体切换路径。

`UserCell.csb` 的 username 字号为 45。函数 `0x854078` 为昵称添加宽度 1、与文字同色的描边，以及偏移 `(1,-2)`、模糊 0 的阴影。正文颜色来自 `0xb31cdc`，阴影颜色来自 `0x1416f74`。当前四款名片的颜色如下，透明度均为 255：

| 名片款式 | APK 资源 | 正文及描边 | 阴影 |
| --- | --- | --- | --- |
| 阶梯 4 | course_banner_4 | #344961 | #F2F3F5 |
| 阶梯 12 | course_banner_12 | #FFFFFF | #D59F78 |
| 阶梯 8 | course_banner_8 | #FFFFFF | #4B1A79 |
| 阶梯 2 | course_banner_2 | #38505C | #FFFFFF |

颜色随名片款式切换，不随昵称语言或 PTT 数值切换。Windows 位图画布临时注册嵌入字体，按四倍分辨率描绘字形轮廓，再合成透明纹理；字体不安装到系统。正常昵称保持 45 字号，过长时才缩至横幅非装饰区域的可用宽度。名字或配色变化后重建纹理，页面关闭时释放图形资源。GDI 与 Cocos 的字形栅格化实现不同，不能据静态参数相同声称逐像素一致。

## 头像、横幅与设定按钮

`avatar_frame.png` 是 APK 的 `assets/layouts/mainmenu/network/icon_back.png` 原件，SHA256 为 `ee16edf3a385ad6bf6ce72dc96c78ad317e28fdb52e557013c09cc7aacc0d17a`。账号页使用这份细边素材。深色底只铺在角色头像内部，按同一菱形裁切。

函数 `0x14b9a04` 将账号节点缩放到 0.7，放在屏幕中点、距顶部 20 的位置。节点为 150×150，锚点 `(0.5,0.5)`，图标 y=60；头像中心距顶部为 `20+(75-60)×0.7=30.5`。将 APK 的 720 高设计空间换算到本游戏的 2160 高空间，头像框为 315。PTT 的大小与位置由 `PotentialView.BadgeSize/BadgeCenter` 独立保持。

函数 `0x854078` 将横幅顶端裁去 4，保留 70 高，中心放在头像上方 7.5；没有课程数字装饰时，昵称右端距头像中心 85。以昵称前进宽度为 W，横幅宽度为 `clamp(250+W,404,514)`，从原图右侧裁切。昵称 y=64.8811035，头像 y=60。各阶梯横幅的图案不同，点阵数量不能用来推断共同的裁切高度。

设定按钮来自 `TopBar.csb`：按钮中心 x=762.9059、顶部 y=301，锚点 `(0.5,1)`，尺寸 98×51；齿轮中心 `(761.6654,272)`、31×31、透明度 99；文字中心 `(762.8451,270.9554)`、字号 16、描边 2、描边色 `#402D3F`。TopBar 的顶部基准 y=300，布局按三倍设计单位换算。`settings_button.png` 和 `settings_icon.png` 分别保留 APK 的 `assets/layouts/1080/topbar/top_button_settings.png`、`assets/layouts/topbar/settings.png` 原件；`PotentialBadges.DrawSettings` 按这些数据合成中文按钮。

“设定”的字体由 `0x1790da4` 识别 `Fonts/L2-Semibold.ttf` 后，以类型 2 调用 `0x1809ddc`；`zh-Hans` 分支选择 `Fonts/NotoSansCJKsc-Bold.otf`（全局指针 `0x1bccd58` 的重定位目标为 `0x5555ad`）。嵌入字体与 APK 原件逐字节相同，SHA256 为 `f32e8651b534178dfd21cbc3fac22861883c2105a8cb39a4f305170b715fe503`。GDI 使用字体族 `Noto Sans CJK SC Bold`、字重 700；先描边，再填充完整白色正文，避免居中描边盖住笔画。

`PotentialSelection` 创建账号后，将账号下全部 `Renderer`（包括暂时隐藏的昵称与 PTT）切换到原生 `TopBar.primaryNavigationButton` 所在的层。当前游戏中，曲绘模板在 `Lowiro_Default`（6），顶部黑条与导航在 `Lowiro_Layer2`（10）；`URP-Performant-Renderer` 用独立的 `RenderObjects` 绘制后者（`LowiroLayer2Transparent`，LayerMask 1024）。只改 Z 或材质队列无法跨越这两个绘制阶段。按钮与碰撞体保留原输入层。

`PotentialSelection.Render` 再按 `TopBar` 的 `CalculatedZFromCamera` 将账号容器放在其前方 0.2 深度单位；账号的二维布局、原生顶部栏与设置/弹窗的更上层绘制顺序保持不变。
