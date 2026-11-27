# 离线脚本的命令与数据格式

这些参数只属于 `ui_sprites.py`，不传入 Generator MCP。无网络调用、无凭证、无图像生成，只做测量辅助与确定性像素处理。所有操作保留原件；除 `stage-upload` 可复用同一哈希副本，其他输出目录必须不存在。

## inspect：确认像素与透明度

```text
python "SKILL_DIR/scripts/ui_sprites.py" inspect "WORK/source.png" --out "WORK/inspect-v1" --grid 128
```

`report.json` 记录真实尺寸、原模式、alpha 统计、源 SHA256。`coordinates.png` 按原分辨率加坐标格，未缩放；看图工具若缩放显示，仍按图上原像素标注测量，不直接拿屏幕坐标。`regions.template.json` 自带实际 source_size/source_sha256，只需填写 regions。全透明图不能切出有效精灵；全不透明图不证明纯色底，必须看原件。

## slice：regions.json

下面只是自测图的格式示例。复制自己的 inspect 模板，不能复用这里的 SHA 或矩形。

```json
{
  "schema_version": 1,
  "origin": "top-left",
  "source_size": [512, 256],
  "source_sha256": "从本次 inspect/report.json 复制完整哈希",
  "regions": [
    {
      "name": "button_normal",
      "rect": [0, 0, 256, 128],
      "background": {"mode": "alpha"},
      "trim": false,
      "padding": 4,
      "allow_edge_contact": false,
      "state_group": "button_primary"
    },
    {
      "name": "button_pressed",
      "rect": [256, 0, 256, 128],
      "background": {"mode": "alpha"},
      "trim": false,
      "padding": 4,
      "allow_edge_contact": false,
      "state_group": "button_primary"
    }
  ]
}
```

| 字段 | 规则 |
|---|---|
| name | 唯一小写英文，首字符字母，后续可数字/下划线/连字符，最多 64 字符；不能是系统保留名 |
| rect | `[x,y,w,h]` 整数，左上原点，覆盖 `[x,x+w)` 与 `[y,y+h)`，不越界 |
| background | 每项明确选择一种，见下文 |
| trim | true 仅删除 alpha=0 的外围空白；状态组必须 false |
| padding | 最终输出四周增加的透明像素，0–256；不等于点击区或图集间隔 |
| allow_edge_contact | 默认填 false；主体接触裁剪边界即报错，不能靠 padding 补回缺失内容。只有实际确认完整的不透明矩形/合法贴边图层才填 true |
| state_group | 无状态关联填 null；同组必须同源裁剪宽高、padding，且不紧裁 |

三种 background：

```json
{"mode":"alpha"}
```

保留已有透明度，RGB/索引 PNG 会解码为 RGBA。不会假装替不透明图去掉背景。

```json
{"mode":"opaque"}
```

用于不需要抠色的完整背景/实心矩形；输入裁剪必须全不透明。

```json
{"mode":"key","key_hex":"#FF00FF","tolerance":12,"softness":12,"scope":"edge"}
```

这里的 12/12 只是保守起点，不是所有图的正确值。距离定义为三个 RGB 通道差的最大值（不是欧氏距离）。距离不超过 tolerance 则 alpha=0；到 tolerance+softness 间线性过渡；更远则保留。原有 alpha 乘过渡权重，不直接替换。

`edge` 只处理通过四邻接连接到裁剪边缘的候选背景，保留被主体包围的同色区域；`all` 会处理所有匹配色，可能把主体挖空。环形/镂空件如果内部应透明，观察确认后再选 all，或用独立 alpha 层。软阴影、玻璃和反射不能由这套距离算法精确恢复。

窄缝不一定与外部背景连通：叶片/羽毛分叉、绳结、卷曲花纹之间的背景，可能被黑描边围成小口袋。`edge_unmix` 不能自动识别这些口袋。若残留亮键色恰在这些空隙中，先对照原图与设计配色确认它是底色污染，再用 `scope="all"` 与足够覆盖实测混色带的 softness，并复查主体内的同色细节。不要仅因一个像素在外接矩形内部、或当前 alpha=255，就把它认定为应保留的美术细节；也不能未经看图就把所有紫色/绿色全删掉。

不做全图去洋红、不做模糊、不自动调色或假装抠色没有损失。RGB 保持原值，因此半透明边缘可能留底色污染；必须看深浅底，必要时取得原生透明层/人工遮罩，而不是无上限增大容差。

对于有明确不透明描边的按钮、面板、图标，若实际 1:1 检查发现细窄色键晕边，可显式增加 `"edge_unmix":2`。默认 0（不开启），只允许 0–4 像素。脚本仅在已移除背景旁的窄带内，用键色混合的近似反解修正 RGB/alpha，不改远离边缘的主体；同一图必须看开启前后的深浅底对比，不能用“色键像素数变少”代替观感。该近似不适用于玻璃、光效、软阴影或同色主体；这些素材优先真实透明层。原生 alpha 路线不接受该参数。

窄带以“已被色键影响的像素（包括部分透明）或完全透明背景”为起点，包含其中原本 alpha=255 的边缘污染像素；不是“只能修复半透明像素”。仍未被识别为背景且离窄带较远的色斑，需要先核对容差/softness 或已确认的封闭孔洞，不能仅扩大 edge_unmix 范围。

去混色还要求像素确实偏向键色的通道组合（如洋红为 R/B 高于 G，绿色为 G 高于 R/B），避免把紧贴透明边的正常金色/蓝色也改为半透明。该选项只支持通道差至少 64 的彩色色键；白/灰/黑底仍可用普通 key，但不能用此色偏反解选项冒充通用抠图。

脚本用附近不偏键色的实心像素估计前景；找不到可靠邻居时只抑制偏色。轻微中性灰通道差会保留，避免把灰色边缘误算成绿色。它仍是窄带近似修复，需要看图，不用于复杂透明材质。

```text
python "SKILL_DIR/scripts/ui_sprites.py" slice "WORK/source.png" "WORK/regions.json" --out "WORK/slices-v1"
```

源尺寸/哈希、字段拼写、坐标、空图、边缘、状态画布会在写文件前验证。裁剪框可能互相覆盖透明留白，脚本不把重叠直接当错；逐张检查不能含邻居。状态组检查只覆盖本次一个源图的切片；跨多张原图的同组状态仍须在交付/引擎中核对相同画布和布局。

`manifest.json` 记录源与各输出哈希、source_rect、trim_offset_xy、padding、最终尺寸、alpha 统计。最终 PNG 坐标映射回源图：

```text
source_x = source_rect.x + trim_offset_xy.x + final_x - padding
source_y = source_rect.y + trim_offset_xy.y + final_y - padding
```

新增 padding 区域不对应真实源像素。脚本保留常规 PNG 文本元数据和 ICC profile，但不能宣称保留全部私有元数据/嵌入式来源证明，原件必须单独保存。

## qa：layout.json

从本次 `layout.template.json` 复制后填写。null 的 border 表示未测量，qa 会拒绝；不需要九宫格明确填零。

```json
{
  "schema_version": 1,
  "sprites": [
    {
      "name": "button_normal",
      "sha256": "自动模板里的实际切片哈希，不手改",
      "size": [264, 136],
      "border_ltrb": [36, 32, 36, 32],
      "text_expected": true,
      "content_rect": [44, 44, 176, 48],
      "pivot_xy": [0.5, 0.5],
      "preview_scale": 1.0,
      "preview_sizes": [[264, 136], [400, 136], [160, 100]]
    }
  ]
}
```

以上数值仅示例，必须以当前最终 PNG 为准。所有几何都包括导出的透明 padding。`border_ltrb=[左,上,右,下]` 不等于 `[x,y,w,h]`，更不等于文字间距。

`pivot_xy` 使用最终 PNG 内的归一化左上原点 `[0..1,0..1]`。用于记录引擎锚点，本脚本不移动主体。居中通常为 `[0.5,0.5]`。

`text_expected=true` 必须有非空 content_rect；纯图标 false/null。所有 regions 都必须在 layout 中出现且只出现一次。文件变化会导致哈希不匹配；重新测量新文件，不能只改哈希绕过。

`preview_sizes` 1–8 项，每边 1–4096 像素。`preview_scale` 是一源像素在预览中占多少目标像素，默认 1，范围大于 0 且不超过 8。例如源图按 4 倍清晰度制作，实际显示时填 0.25。九宫格 border 仍按源 PNG 测量，不改源值；预览固定边缘变为 border×preview_scale（取整，非零边最少 1px），中段按目标尺寸伸缩。目标宽高需大于缩放后的两侧固定边之和。全零 border 等价普通整体缩放。这里的尺寸是预览像素，不直接等于引擎逻辑单位，须和引擎 PPU/Canvas 缩放对应。

例如 1600×480 的源按钮要显示为 320×96：可从 preview_scale=0.2 开始检查；还要检查 240×96 的最小尺寸。不能把源 border=200 错改成 40 来绕过校验，因为那会切到原图角花内部。目标预览中的绿色 content_rect 按九宫格分段转换，`report.json` 同时记录每个目标尺寸的实际文字框。

**控件改宽与整屏缩放是两种测试。** 同一个按钮从 320×96 改为 240×96，保持固定边视觉厚度，所有预览使用同一 `preview_scale`。整屏从 720×1280 缩为 360×640 时则连固定边也缩为一半：添加可选 `preview_scales` 数组，与 `preview_sizes` 等长并一一对应，覆盖该行的默认 scale。例如面板 `preview_sizes:[[640,1120],[320,560]]` 配 `preview_scales:[0.65,0.325]`。不提供数组时才会把同一个 preview_scale 用于所有尺寸。report 中每个预览都记录实际 scale 与映射后的内容区，便于核对。

```text
python "SKILL_DIR/scripts/ui_sprites.py" qa "WORK/slices-v1" "WORK/layout.json" --out "WORK/qa-v1"
```

输出：每张切片的 1:1 深底、浅底、棋盘格合成，蓝色九宫格线/绿色文字安全框，多个目标尺寸的拉伸预览。尺寸预览超过 1600×900 的部分会缩略并标注显示尺寸，检查细节仍打开原始切片。`report.json` 只声明 `geometry_checks=passed`，视觉始终 `required`，引擎 `not_performed`；实际人工/模型看图结果另记，不能篡改脚本报告冒充已验收。

## stage-upload：Generator 按路径去重的防护

```text
python "SKILL_DIR/scripts/ui_sprites.py" stage-upload "WORK/reference.png" --out "WORK/upload-inputs"
```

实际复制为 `ref-完整SHA256.原扩展名`，返回绝对 file_path；同内容可复用，内容修改得到新路径，不写回原件、不上传网络。然后调用 Generator `file_upload`。完整上传流程见 generator-mcp.md。

## 复现自测示例

```text
python "SKILL_DIR/scripts/test_ui_sprites.py"
python "SKILL_DIR/scripts/test_ui_sprites.py" --demo-out "WORK/demo-v1"
```

第一条运行离线行为测试；第二条创建脚本绘制的合成测试图及完整切片/QA 产物，不调用任何生图模型。它验证工序，不证明真实生成图的美术质量。
