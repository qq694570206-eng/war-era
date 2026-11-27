# 离线修复与验证记录

日期：2026-11-10。无需 Unity 包下载；本轮仅实施源码静态修复与离线数据检查。

## 已修改

- 将 Core、Data、Simulation 三个相互耦合的程序集边界收敛为 Scripts 根目录的 `WarEra.Runtime`，显式引用 UI 和 TMP。此为过渡方案，未宣称完成分层重构。
- GameManager 提供只读 `IsGameOver`，修复 SaveSystem 对 private 字段的访问。
- 实体创建统一调用 Initialize，拒绝空定义；注册去重；死亡幂等、立即退出查询索引，销毁时补清理。
- 超武伤害遍历目标快照，避免死亡注销导致集合枚举失效。
- 建筑放置时重新校验电力；生成失败退回预付资金且不产生采矿科技点；生产缺少科技系统时按 T1 校验；拒绝负数扣款和负数采矿。
- 地图出生点访问拒绝负索引。
- 河流障碍分为三段，在 x=800、1600 的控制点位置留出两条各宽 160 的通道；十字路口中央障碍拆为两段，释放中央矿点和控制点。

## 可重复检查

在仓库根目录运行，Python 3 标准库即可：

```sh
python3 tools/offline_check.py --json docs/offline-check-report.json
python3 -m unittest discover -s tools -p 'test_offline_check.py' -v
```

检查地图 JSON 结构、坐标边界、数量、枚举、障碍范围、出生点重复、矿点/控制点落入障碍、出生点互通及资源可达性；同时检查临时单运行时程序集边界和 UI/TMP 引用声明。

本轮结果：6 张地图均为 0 错误；共 13 条警告保留。10 个回归测试通过，包括重建原始河流断路和十字路口堵塞后确认校验器会拒绝。机器可读结果见 [检查报告](offline-check-report.json)。

警告来源：6 张地图仍为原型无限矿、4 张地图为四人数据但当前运行时仅初始化双方，以及缺少包清单、Editor 版本和序列化单位资产。未擅自把四人地图转为双人，未直接改动全部经济规则。

## 验证边界与下一步

地图可达性采用 10 原型单位的保守栅格和点代理，不能证明坦克半径、动态建造后的通路或运行时流场正确。地图 JSON 尚没有接入运行时导入流程，本轮修改的是源数据。

本机未发现 PATH 中的 dotnet/csc/mcs；本轮未运行 C# 编译器或 Unity，不能据离线检查断言全部 C# 可编译。UGUI 引用名称核对了 [Unity 官方源码](https://github.com/Unity-Technologies/uGUI/blob/main/com.unity.ugui/Runtime/UGUI/UnityEngine.UI.asmdef)；最终仍需在固定 Editor 与实际安装包组合下验证。

仍未解决：JsonUtility 字典存档及恢复空实现、流场生成与多目标接入、游戏启动生命周期与场景配置、免费 AI 出兵、迷雾授权和电力完整生命周期。这些不能算本轮已完成。

包恢复后先做 Unity 编译与场景冒烟，检查 UI/TMP 依赖、死亡注销和超武多目标伤害，再建立 M0 工程基线；期间可以继续将存档 DTO 和核心规则拆成独立可验证逻辑。
