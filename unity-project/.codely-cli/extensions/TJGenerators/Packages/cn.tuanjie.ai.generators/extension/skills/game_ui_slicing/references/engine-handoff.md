# 引擎交付与验收

本技能负责可复用图片及几何。引擎操作优先用项目已有连接/编辑器工具，先确认版本和 UI 系统；不要求另装 Bridge，不改无关场景。Generator 的完成/导入指南按当前任务结果保留并执行适用部分。

## 坐标统一

脚本中的 rect/content_rect 是**左上原点**的 `[x,y,w,h]`，border_ltrb 是 `[左,上,右,下]`，pivot_xy 是左上原点归一化坐标。源图裁片、最终 PNG、引擎 Sprite、UI 容器是四套坐标，转换时明确引用哪一套。

Unity Sprite 的底左原点矩形：对完整高为 H 的图片，把脚本 `[x,y,w,h]` 转成 `[x,H-y-h,w,h]`。独立导出 PNG 导入为 Single 时通常用完整图片，不再按 source_rect 裁一次。Unity border Vector4 顺序为 `[左,下,右,上]`，因此 `[L,T,R,B] → (L,B,R,T)`；pivot `[px,py] → (px,1-py)`。API 以目标项目实际版本为准，不直接混用旧 spritesheet API 与 Sprite Data Provider。

若图源一像素在屏幕中占 s 个像素，border 的固定段也要按同一比例理解。离线九宫格预览用 `preview_scale=s` 缩放固定段；引擎中的 PPU、Canvas 参考 PPU/缩放应对应这一视觉厚度，不可直接拿屏幕单位当 PNG 像素。

## Unity / 团结常见设置

- 独立 UI 图：Sprite (2D and UI)、Single、Clamp、关闭 mipmap，保留 alpha。先用无压缩或高质量设置验边，再根据目标平台压缩预算调整。普通 UI 通常使用 Full Rect mesh；九宫格不用 Tight mesh 裁掉边缘。已有 .meta/GUID 保留，避免断引用。
- 禁止导入时非预期改尺寸/宽高比。导入后核对实际 texture 宽高、max size、NPOT 缩放和平台 override。正常像素画按项目规则使用 Point；其他 UI 常用 Bilinear，不统一强制一种过滤。
- 需要九宫格的 Image 用 Sliced，设置实测 border；图标等比显示；不要把应为 Sliced 的面板整体 Stretch。四角固定不代表尺寸能无限小。
- border 负责纹理拉伸；content_rect 负责文字安全区域。不得把两者当同一 padding。文字、货币数值、动态名称单独用项目已有 Text/TMP/UI Toolkit 系统显示。
- 同一状态组共享尺寸、pivot、border 和逻辑文字区域。轻微按下偏移应在布局中明确做，不能来自失控紧裁。

整块等比显示时，文字区可按内容矩形归一化映射到图片内部。**九宫格显示时不能只用归一化锚点**：左右固定边缘与中段缩放不同，须按分段映射，或设逻辑固定边距并在目标尺寸下验证。透明 padding 和图片装饰都属于实测参考，不用统一“上下左右 20px”代替。

静态拼装预览与引擎实现应读取同一份 layout。使用 helper 的 `display_content_rect` 映射或等价逻辑来定位实时文字/商品占位，不能切图验收时有安全区、拼装时又忽略它。每个卡片上的图片占位、金币图标与最大价格组合都要按实际边界验收。

## 文字与交互验收

至少覆盖设计尺寸和最小窗口、最长实际文字、中文标点、最大数值、所有按钮状态。有多个目标比例就各覆盖一次。检查：

1. 字形是否完整，缺字/方框/fallback 字体的基线是否异常。
2. 实际可见字形是否越过 content_rect，是否压角花；容器未溢出并不代表没压装饰。
3. 是否有末字被裁掉、换行后高度不够。TMP 检查溢出与实际字形；传统 Text 还查 preferredHeight/Width，不能只检查已显示的字。
4. 打开/关闭/返回、点击区域、遮罩和装饰节点是否挡点击。
5. 实际场景截图与离线素材的一致性；源 PNG 好看不代表运行时引用了新图。

仅交付素材时附“建议导入参数，未做引擎验证”。完成接入时交付真实截图及实际测试的状态/分辨率，未测范围如实写出。

## 其他引擎

仍交付 RGBA PNG、最终几何、来源哈希和深浅底 QA。把九宫格固定边、content_rect、pivot 映射到目标引擎相应能力；查当前项目使用的版本/组件，不把 Unity 特有字段直接用于 Godot、Unreal 或 Web Canvas。无需九宫格时用原比例图片，不为使用某个工具强行改变 UI 系统。
