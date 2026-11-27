# Generator MCP 调用手册

> **本 extension（Codely + Unity）请优先读 [generator-unity.md](generator-unity.md)。**
> 下文保留原 skill 对纯 MCP 宿主的整理，供对照；切图本身仍只走本地 `slice_ui_sprites` / `ui_sprites.py`。

按当前工具 schema 执行；本手册是 2026-09-29 会话内 Generator MCP 工具定义的整理，不是永远不变的 API。不同宿主显示 `generate_image` 或带 server 前缀的名称均可，匹配原名。不要为了用此 skill 另装 Generator CLI、读取凭证或自己拼生成 HTTP API。


## 1. 路由表：只选符合本次请求的一条

| 用户需求 | MCP 工具 | 下一步 |
|---|---|---|
| 从需求生成可复用游戏 UI 部件，含整套 UI（**主路线**） | `generate_image`，高细节美术可选 `frontier_sunburst` | 下载、inspect、测量、slice，最后组合 UI |
| 整页效果图（布局参考） | `generate_image`，高细节美术可选 `frontier_sunburst` | 一般不抠图；勿当可驱动控件成品 |
| 用户指定其它单图 provider | `generate_image`，严格按支持值 | 不把单图 provider 传入 UI kit |
| 已有整张界面图，要求分层 | `generate_image_layers` | 检查返回层；不是无损 PSD 还原 |
| 本地参考/待合图 PNG | `file_upload` | PUT 字节，再用 file_url |
| 已验收的独立 PNG 要打包为图集 | `generate_sprite_atlas` | 同步返回 atlas + layout，无轮询 |
| MCP 创建的异步任务需确认最终结果 | `check_task` | shell 等待完成后查一次实际状态 |

`generate_sprite_animation` 是逐帧动画生成，不是静态 UI 切图入口。图集打包、AI 分层、矩形裁切是三个不同操作。

## 2. 本地参考上传

1. 先看参考图。已知生成返回的 CDN file_url 可直接复用；不把任意网页 URL 当作图片地址。
2. 对本地文件执行 `python "SKILL_DIR/scripts/ui_sprites.py" stage-upload "WORK/reference.png" --out "WORK/upload-inputs"`，得到带完整 SHA256 的真实副本路径。
3. 调用 `file_upload({"file_path":"上一步实际副本绝对路径"})`。
4. 返回 `already_uploaded=false` 时，按返回 `upload_url` PUT **实际文件字节**，检查 HTTP 200；true 则跳过 PUT。
5. 给生成工具的是返回的 `file_url`，不是 `upload_url`、本地路径或 base64。不要给 CDN 附带 MCP Authorization。

PowerShell 可按真实返回值执行（以下两变量必须来自工具，不得编造）：

```powershell
$localFile = '实际带哈希的本地路径'
$putUrl = '实际返回的 upload_url'
$httpCode = & curl.exe --silent --show-error --output NUL --write-out '%{http_code}' --request PUT --upload-file $localFile $putUrl
if ($LASTEXITCODE -ne 0 -or $httpCode -ne '200') { throw 'Upload did not return HTTP 200' }
```

当前 `file_upload` 按完整路径去重。同路径改了内容会误复用旧 CDN，所以使用带哈希的新文件名。工具未来改成内容去重时按新 schema 调整。签名上传 URL、poll URL/票据不放进公开文档、最终压缩包或日志摘要。

## 3. 已有整页效果图

复用截图的实际 URL。需要独立图层时，按[分层流程](../../generate_image_layers/SKILL.md)调用 MCP `generate_image_layers`。导入层后测量安全区和边距；可变数值、文字和填充在引擎内渲染。

## 4. 单图生成与独立分层

### generate_image

单件使用 prompts.md 的 B/C/D 模板。**高细节游戏 UI 可显式传 `provider=frontier_sunburst`**（全能日辉）；用户点名其它模型才换。支持值包括 `seedream`、`seedream_pro`、`frontier`、`frontier_flare`、`frontier_sunburst`、`frontier_lite`、`hy_image_v3`、`qwen`，以当前 schema 为准。

```json
{
  "provider":"frontier_sunburst",
  "output_format":"png",
  "is_segmentation":true,
  "prompt":"已填好的单部件或小图册提示词",
  "image_size":"square_hd"
}
```

有参考图再加 `image_urls:"file_url1,file_url2"`（逗号分隔字符串）。自定义尺寸格式是 JSON 编码字符串，例如 `image_size:"{\"width\":1024,\"height\":768}"`，不是 JSON 对象。尺寸、`resolution`、`output_format` 是否适用取决于 provider；无须时省略。不要用已弃用的 `images` base64 字段，不存在的 `transparent_background` 也不要传。生产 UI 分件默认显式传 `is_segmentation=true`（智能抠图）；整页效果图一般 `false`。仍须 inspect 验证——抠图不等于可靠 alpha 或自动正确切片。禁止仅靠 prompt「透明背景」代替该参数。

### generate_image_layers

普通分层明确用 Qwen，补充实际图片描述：

```json
{
  "provider":"qwen",
  "image_url":"实际上传返回的 file_url",
  "prompt":"一张正面游戏商店 UI，包含底板、商品槽、按钮和货币图标；请拆分主要独立层",
  "num_layers":4,
  "output_format":"png"
}
```

当前图像要求至少 512×512，最大 6000×6000；`num_layers` 对 Qwen 为 1–8。Pro 仅在用户明确要求或同意切换能力时使用，自动输出底图和最多 16 层；`num_layers` 对 Pro 无效。Pro 的 `size` 为 `1K/1.5K/2K/auto`。层数不是按 UI 控件逐个识别的保证，不足 512 的图不要静默拉大后冒充原始质量。

`generate_image(layer_decomposition=true)` 也可走 Pro 分层，但本 skill 默认使用独立分层工具，减少参数误用。没有明确 Pro 需求时不切换。

## 5. 异步等待：保存任务，不重复提交

1. 创建返回 task_id 和任务专属 poll 元数据后，保存到本次工作记录。queued/running 都不是失败。
2. 按返回的 `poll_command_powershell` 或 `poll_command` 及 interval/attempts 执行等待。支持并获准委派的宿主用**每任务一个后台等待代理**；无子代理能力时用一个后台进程。Windows 启动后台进程隐藏窗口。不要另造固定 sleep 时间冒充任务完成。
3. 等待脚本退出后，包括授权错误、超时或 poll_error，调用一次 `check_task({"task_id":"原 task_id"})` 获取真实状态、产物和完成指南。
4. completed：下载实际返回文件。failed：记录错误；部分文件标为待检查。仍在运行：保留同一 ID，按返回机制继续等待/恢复，不能紧循环 check_task 或重新调用生成工具。
5. UI kit 第二阶段是一个新的阶段任务；第一阶段完成后才提交。一个阶段不能同时通过 MCP 和 CLI 提交两次。

任务清单（job_create/todo 等）不会执行轮询；远端 task_id 也不是本地等待者句柄。必须有真实的后台工具/进程调用记录，才能声称等待已启动。一次性 CLI 若在产物落地前退出，应从保存的同一任务恢复，不能把退出 0 当完整交付。

Windows 把返回的 `poll_command_powershell` 原文保存成 .ps1 执行，不重新手打其中的转义。尤其 curl 的 `-w '\n%{http_code}'` 使用 curl 自己的反斜杠换行语法，不能改成 PowerShell 单引号内的反引号 n。普通顺序命令不要用 `&` 串接，PowerShell 中它可能把前一条变成后台任务；拆为独立命令并检查结果。轮询解析报错后按第 3 项 check_task，不能误判为生成失败。

收费确认按服务端实际返回执行：首次不传 `confirm_cost=true`；低于门槛直接继续，返回报价才询问本次具体费用。用户同意后用返回 confirmation_arguments 或同样参数加 true。报价单次有效且会过期，失败不能反复 true。两个 UI kit 阶段分别判断，不因第一步已同意而越过第二步报价。

## 6. 接收结果与可选图集

下载原件后实际解码、计算哈希，再 `inspect`。HTML 错误页保存成 .png 不算图片；下载失败只重下同一 URL/查询同一任务。保存源层元数据，确定 bounding_box 的单位、原点与参考画布：某些层已经是完整画布，不能再按该框重复裁；某些层是局部图，需保留原布局偏移。

来源记录使用 [task-record-template.json](../assets/task-record-template.json)。本地切片 manifest 自动存源/派生哈希；手工来源记录关联到它。模板中 null 表示未知，不能编造。

只有用户需要图集才上传已验收独立 PNG，然后调用：

```json
{
  "image_urls":"实际切片 CDN file_url1,实际切片 CDN file_url2",
  "output_format":"png",
  "max_size":2048,
  "padding":4,
  "power_of_two":true
}
```

当前 `generate_sprite_atlas` 同步返回 URL 和 layout；不要 check_task。输入用 PNG，当前不支持 WebP。atlas packing 的 `padding` 是图片之间距离，不等于脚本切片留白，也不保证边缘像素扩展。Unity 自带 SpriteAtlas 能完成合图时可直接用项目现有工作流，不重复生成/打包。合图后再次检视边缘与纹理过滤；保留独立 PNG 和打包前几何。
