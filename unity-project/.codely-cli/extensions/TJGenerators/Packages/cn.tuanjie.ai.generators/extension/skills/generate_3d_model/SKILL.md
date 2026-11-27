---
name: unity-3d-model-generation
description: "Generate static 3D models, props, or rigged/animated Humanoid 3D characters in Unity. Generation, rigging, and motion run on the MCP backend (generate_image / generate_3d_model / unirig_rig / unimate_motion_generate + check_task); Unity import, texture extraction, Humanoid setup, multi-state AnimatorController, grounding, and Prefab binding run via import_3d_model_from_url. Use whenever the user asks for any 3D object, prop, weapon, vehicle, building, NPC, or animated character — e.g. \"生成3D模型\", \"生成一个带待机、行走、攻击动画的角色\", \"给模型绑骨\", \"create a 3D prop\", \"animated character\". DO NOT use for terrain (use generate_terrain) or 2D sprites."
---

> **执行前读取**：[共享执行约定](../../experience/templates/generator-async-pattern.md)。
> 主 agent 按需委托 `general-purpose`；已在子代理中则直接执行本 skill，不再委托。已提交阶段从原任务续接，不重复生成。

> ⛔ **场景放置规则**
> - 调用 `activate_skill("unity-place-assets-in-scene")` → 用 `exec_editor_script` 按 §4a `PrefabUtility.InstantiatePrefab` 放置（不要用 `unity_gameobject` 放 Prefab）。
> - `static` / `rigged` 导入提交后**立即放置一次占位 Prefab**；`query_local_task` 完成后占位子节点原地替换，**不要再放第二份**。`motion` 模式无占位。

# Unity 3D Model & Animated Character（MCP 生成 + Unity 导入）

所有参数与默认引擎以**实时 MCP tool schema** 为准，不覆写默认 `provider` / `tier` / `engine`（除非用户明确指定）。

## 统一线性流程

```text
[1. 网格] generate_3d_model（复杂/风格化角色先 generate_image 出设定图再图生3D）
   ↓ 检视预览图，取带 PBR 贴图的 FBX URL（result.model 或 base_basic_pbr，勿用裸 base）
[2. 绑骨与动作]（仅角色）先查 list_rodin_rigging_animations：
   - 能匹配预设：在 unirig_rig(animation_presets=[...]) 中一步完成绑骨与动作（默认 engine=rodin，自带贴图回挂）
   - 预设库找不到匹配的定制动作：对绑骨 FBX URL 并行调用 unimate_motion_generate（每动作一次）补齐
   ↓
[3. 导入] import_3d_model_from_url（static / rigged；若有独立动作 FBX 再追加 motion）→ 放置占位 → query_local_task
   ↓
[4. 验收] EditMode 量化校验 + 实拍渲染图检查，通过后再交付
```

---

## Step 1：生成 3D 网格（已有本地 FBX 可跳过，先 `file_upload`）

- **普通道具 / 家具 / 建筑**：直接调 `generate_3d_model(mode="text_to_model", prompt=...)`。
- **风格化 / 特定题材角色（如修仙修士、武将、动漫/奇幻角色）**：为保证脸型、服饰与可绑骨结构一次过关，**先调 `generate_image` 生成全身正面设定图，检视合格后再调 `generate_3d_model(mode="image_to_model", image_url=<图URL>, face_limit=20000)`**（主角/人形角色传 `face_limit=20000` 以保证五官、发饰与手指轮廓清晰，默认 4,000 面仅适合普通道具）：
  - 画面要求：纯浅灰/白底、全身头脚完整居中、正面站立对称 **A-pose**（双臂自然斜下张开约 40° 远离躯干、五指自然微张、双脚略分开平踩地面）。
  - **可绑骨服饰设计（自动落实，不教导用户）**：袖口收窄贴合手臂（避免大水袖与躯干粘连）；长袍/道袍采用**前开叉衣摆或及膝战袍 + 清晰分开的长裤与靴子**（避免封闭长筒裙把双腿粘死导致行走/攻击穿模）；双手空手张开便于绑骨（佩剑等武器收于背后或腰间，不握在手中）。
- **预览门禁**：`generate_3d_model` 完成后先看预览图；若四肢粘连、形体严重畸形或风格偏离，立即重出网格，不在坏网格上浪费绑骨与动作积分。
- **选带贴图 FBX URL**：优先 `result.model` 或 `result.base_basic_pbr`，禁止取无贴图裸 `base` 或 `.glb`。

## Step 2：绑骨与动作（仅带动画 / 需绑骨角色）

1. **优先查预设一步绑骨 + 动作**：需要动画时，先调一次 `list_rodin_rigging_animations(category="human")`（四足用 `canine` 等）即可一次看到全部可用预设的永久 ID（如 `human_000090`）、名称与描述。找到语义匹配的预设后，直接把 Step 1 的带贴图 FBX CDN URL 与匹配的 `animation_presets` 传给 `unirig_rig(model_url=..., animation_presets=[...], in_place=true)`（含行走/跑步等循环位移预设时传 `in_place=true` 导出原地循环；默认 `engine="rodin"`，自动输出 Mixamo 骨架并回挂原贴图；本地文件先 `file_upload`）。
2. **预设找不到匹配时走原路线（UniMate 补齐）**：若全部或部分需求动作在预设库中找不到合适匹配（例如特定招式/施法/武器攻击），对 `unirig_rig` 产出的绑骨 FBX URL，按每个缺失动作**并行**调用 `unimate_motion_generate(model_url=<rigged_fbx_url>, prompt=...)`：
   - 每次只描述**一个**清晰区分的动作，采用英文短句（`"An object ..."`），例如：
     - Idle: `"An object stands upright calmly in a relaxed idle pose with gentle breathing and slight weight shift."`
     - Walk: `"An object walks forward steadily at a natural pace with swinging arms."`
     - Attack: `"An object performs a dynamic forward melee slash attack with the right arm, stepping forward and returning to stance."`
   - **不要对 Mixamo / Rodin 绑骨角色用 `generate_motion`（混元）**，除非用户点名混元或视频驱动动作（SMPL-H 重定向常导致双臂僵直）。

## Step 3：导入 Unity（`import_3d_model_from_url`）

> ⛔ `add_motion=true` 已废弃，禁止传给 `import_3d_model_from_url`。

- **静态物件**：
  ```json
  { "model_url": "<fbx_url>", "output_type": "static", "prefab_output_path": "Assets/Models/<Name>", "prompt": "..." }
  ```
- **绑骨角色**（`source_model_path` 可省略，自动提取 `.fbm` 并按 URP/Standard 合成 Metallic+Smoothness PBR 材质、归一化身高到 `1.75m`；若 FBX 内嵌动作会自动建多状态控制器，核对 `controller_states`）：
  ```json
  { "model_url": "<rigged_fbx_url>", "output_type": "rigged", "prefab_output_path": "Assets/Characters/<Name>", "target_size": 1.75, "prompt": "..." }
  ```
  提交后立即用 `unity-place-assets-in-scene` 实例化占位 Prefab 到场景目标位置，前台等待（≤30s）后 `query_local_task` 一次（未完继续等待查询）直到 `completed`，记下 `model_path` 与 `prefab_path`。
- **追加独立动作 FBX**（仅当 `unirig_rig` 返回多个独立动作 FBX 或有 UniMate 补充动作时；互不覆盖文件与状态，自动开启 Humanoid `heightFromFeet` 脚底接地，`idle` 自动成为默认状态）：
  ```json
  {
    "model_url": "<motion_fbx_url>",
    "output_type": "motion",
    "rigged_model_path": "<model_path>",
    "target_prefab_path": "<prefab_path>",
    "clip_name": "idle",
    "loop": true
  }
  ```
  `loop`: `idle` / `walk` / `run` 用 `true`；单次 `attack` / `cast` / `hit` 用 `false`。`query_local_task` 返回的 `controller_states` 会列出当前控制器全部状态。

## Step 4：强制验收（量化 + 视觉，不通过不交付）

交付前在 EditMode 执行一次 `exec_editor_script` 验收，并用临时 `Camera` + `RenderTexture` 输出 PNG 实看：
1. **贴图与材质**：所有 `Renderer` 无缺失材质/洋红错误 Shader，主贴图（`_BaseMap` / `mainTexture`）非空且无塑料油光。
2. **骨架与尺寸接地**（角色）：`Animator.avatar` 为有效 Humanoid，世界身高约 `1.6–2.0m`，静姿与动画采样时脚底均贴合地面（`LeftFoot`/`RightFoot` 世界高度约 `0.05–0.22m`，不浮空、不穿地），正面朝向合理。
3. **动画状态与真实形变**（带动画角色）：`AnimatorController` 含全部需求状态（如 `idle` / `walk` / `attack`），每段剪辑时长 `> 0.3s`；用 `AnimationMode.SampleAnimationClip`（或采样后置 `smr.forceMatrixRecalculationPerRender = true` 再 `cam.Render()`，避免 EditMode GPU 蒙皮未刷新）采样各状态关键帧，确认肩/臂/腿相对绑定姿态有真实转动（行走/攻击最大关节变化 `> 15°`，且三个动作姿态明显区分）。
4. **视觉检查**：查看正面全身/近景与 `idle` / `walk` / `attack` 采样帧渲染图；若出现白模、浮空、严重穿模、僵硬不动或背对相机，直接定位修复后再汇报完成。
