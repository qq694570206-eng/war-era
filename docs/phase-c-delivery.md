# 阶段 C 交付报告 · 素材真正接线

**提交**：`0f615f4` 　**日期**：2026-11-27 　**本轮产出**：我本人 + 主程序（代码侧），未派新单

---

## 一句话结论

24 张 AI 素材**现在已经真的画到屏幕上了**。之前几轮我以为接好了，其实一直没接——所有改动都只写在 C# 代码里，从来没落到资产文件。这次用编辑器批处理跑完了，**40 项验收 0 失败**。

---

## 事实：之前"接好了"是什么状态

打开你工程时，摆在我面前的是这样：

| 项 | 我上轮以为 | 实际 |
|---|---|---|
| `UD_*.asset` 的 `sprite` | 已接线 | **15/15 全是 `{fileID: 0}`** |
| `UnitPrefab` | 四层结构 + UnitView | **单层、无 UnitView、材质 `Sprites-Default`** |
| `SpriteFlash.mat` | 已生成 | **文件不存在（预制体引用悬空）** |
| `powerSupply` | 10 | **0（电厂不发电）** |
| `spriteForwardDeg` | 已写入 | **字段根本不存在（坦克偏 26°）** |
| `producibleUnits` | 已填 | **`[]`（兵营造不出东西）** |

根因：编辑器一直开着，我一轮都没在**编辑器里**跑过引导脚本。代码是对的，产物从来没生成。

---

## 本轮做了什么

1. **20 张 `Art/Keyed/*.png.meta`**：`textureType` 0→8（Sprite）、PPU 逐张写入、pivot、`alphaIsTransparency`。
   `explosion` / `resource_icons` 走 Multiple 并切帧——explosion 切成 16 帧、resource_icons 4 帧，行优先，`explosion_00` 验证在左上角。
2. **15 个 `UD_*.asset`**：`sprite` 全部接上（含 sniper→rifleman、artillery→tank 复用）、`spriteForwardDeg` 终值、`powerSupply` 0→10、`producibleUnits` 补全。
3. **预制体四层结构**：`UnitEntity / RecoilPivot / ArtRoot(UnitView) / AnimPivot / Sprite`，`renderer.scale` 归 1，材质换 `SpriteFlash`。
4. **`SpriteFlash.mat` + meta 补齐**，引用链闭合，`isSupported=True`、`_HasProperty("_FlashAmount")` 为真。

---

## 我怎么验的（这次没让你点菜单）

你的编辑器全程开着（PID 13675 占着 `Temp/TuanjieLockfile`），我不能碰你的 Library。做法是：

- rsync 一份**不含 Library** 的副本到 `/tmp`
- 用 `Tuanjie -batchmode -quit -projectPath <副本> -executeMethod X.Run` 跑
- 副本跑通 → 按 guid 一致性同步回你的工程

判据全部走引擎 API，**不做 guid 文本比对**（团结引擎 meta 里 guid 是 56 字符 base64，与 `.asset` 里的 32 位 hex 无文本对应关系，我用 hex 猜了三次全错）。

**验收结果 40 项 0 失败**，关键几条：

```
def.sprite 15/15 + PPU 回读全部一致
UnitPrefab · material=SpriteFlash / shader=WarEra/SpriteFlash / isSupported=True / _FlashAmount ✅
tank · 朝向 19.0° = 45° − spriteForwardDeg 26°     ✅
rifleman/hq · 按设计不旋转                          ✅
renderer.scale 恒为 1.00（尺寸全交给 PPU）          ✅
sprite 已换成真图、color 复位为纯白（没被占位蓝染色）✅
```

---

## 我看到了什么（出帧）

`docs/shots/phase-c-battle-1080p.png`（1920×1080，orthographicSize=300，与你工程参数一致）：
草地绿底上排着步枪兵、坦克、重坦、采矿车、机枪塔、反坦克炮、HQ、兵营、战车工厂、电厂、精炼厂、雷达。坦克能看清负重轮和裙甲，炮管朝右——**不是色块了**。

`docs/shots/phase-c-tank-closeup.png`：单张放大，写实迷彩、金属反光都在。

---

## 必须告诉你的一件事：**画质被浪费了 89%**

这是我看帧时才发现的，不是代码问题，是**尺度设置问题**：

| 单位 | 世界尺寸 | 1080p 屏幕上 | 原图 | 精度利用率 |
|---|---|---|---|---|
| 步枪兵 | 15 u | **27 × 27 px** | 256 px | **11%** |
| 坦克 | 28 u | 50 × 50 px | 256 px | 20% |
| HQ | 80 u | 144 × 144 px | 256 px | 56% |

`orthographicSize = 300` 意味着屏幕全高 600 世界单位、每单位 1.8 px。AI 素材画的是 256px 的高精度写实图，但士兵只占 27px——**你花了工夫生成的细节，屏幕上根本看不见，看起来就像像素图。**

这不是 bug，是设计取舍：视野大 → 能看见更多战场 → 单位就小。两条路：

- **A. 保持现在的视野**（600 世界单位高），接受"宏观战争感"。优点：RTS 该有的战场规模在。
- **B. 缩小视野到 150–200**，单位变大 1.5~2 倍，画质立刻上来了。代价：一次能看见的战场变小，需要更频繁拖屏。

我倾向 **B**，但这是玩法尺度决定，不该我定。

---

## 占位：当前还是占位的东西

- **爆炸**：`ExplosionPrefab` 仍是 `placehold_circle` + 橙色染色，`scale=1.4`。16 帧序列已经切好躺在 `explosion.png` 里，**但没接线**（需要写一个序列帧播放组件）。
- **子弹 / 枪口闪光 / 移动指示 / 攻击指示**：全是占位图形。
- **地形**：没有任何地形图。`Tiles/` 下有 road/grass/desert/snow 四张，但要先给 `ThemeManager` 加 `terrainMaterial` 字段。
- **UI 三张**：`build_panel_bg` / `minimap_frame` / `resource_icons` 导入参数已就位，**未接线**。
- **nuke_cloud / smoke_column**：工程无调用点，未接线。
- **sniper / artillery**：无独立素材，复用 rifleman / tank 贴图。

---

## 下一步候选（我建议按这个顺序）

1. **把爆炸换成 16 帧序列**——现在是占位橙圆，一开打就露馅，性价比最高。
2. **定尺度**（上面 A/B 二选一）—— 决定后 PPU 全表要跟着重算，越晚改越贵。
3. **接线 UI 三张** —— 建造面板现在是纯色，占了屏幕一大块。
4. 铺地形 —— 目前地面是空的，画面缺"战场"感。

---

## 附：本轮踩到的四个假阴性（都是我的测试写错，不是你的代码错）

留档，免得下次重蹈：

1. `NewScene` 会卸载资产 → ScriptableObject 变 Unity fake-null（`== null` 为真但字段仍可读）→ `ApplyDefinitionSprite` 里 `if(def==null) return` 直接退出
2. 批处理下 `Time.deltaTime == 0` → `RotateTowards(..., rate*dt)` 一步不动
3. `UnitEntity.facing` 单位是**弧度**，我写了 `45f` 等于 2578°，卡在 32.31°
4. `UnitEntity.Update` 在无 `UnitRegistry` 的场景里必抛 NRE，把整个验证方法中断

这四次我一开始都报了"代码有 bug"，追到底才发现是测试环境问题。**但我没在没查清的情况下就改你的游戏代码**——这点我守住了。
