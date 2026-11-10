# 沉钟镇持续刷怪实施计划

> **For agentic workers:** 使用 superpowers:executing-plans 逐项执行；本次用户已经明确要求实现、验证并推送。

**Goal:** 重写当前城镇生成逻辑为确定性的持续刷怪导演。

**Architecture:** 预算导演与现有 AI 组合；导演快照纳入 EldritchState；区域首次清理独立于敌人是否正在存活。

**Tech Stack:** C#、netstandard2.1、.NET 8 控制台验证、Unity 2022。

**Spec:** docs/superpowers/specs/2026-11-10-town-director-design.md

## Global Constraints

- 纯整数 30 Hz；核心框架独立；不改可靠 UDP 和输入格式。
- 保留城区池、隐藏港口、最多两个活跃城区及既有成体上限。
- 中文注释与提交；不包含用户的 Unity ProjectSettings 修改。

## Review Focus

- 买不起贵怪时保留选择；出生点被堵时不扣款且有重试冷却。
- 购物、全员倒地、BOSS 不积压预算；待选区域失活后重新选择。
- 回收尸体不能丢失未支付回执；子代槽位复用不改变母巢每代上限。
- 清理区域后再生不重付委托；恢复时不回退进度。
- 随机数、收入余数、冷却和待选卡恢复后与不中断运行逐帧一致。

### 1. 持续刷怪与首次清理

- [x] tools/HookIsland.Town.Tests/TownDirectorTests.cs：先验证已支付尸体可再生、已清区继续激活，运行看到旧逻辑失败。
- [x] 新建 TownDirectorState、TownDirectorRules、TownSpawnDirector；接口 `Advance(bool allowed,int participants,IReadOnlyList<bool> zones,IReadOnlyList<EldritchCreature> creatures,int maxAlive,Func<int,bool> spawn)`，快照 `Capture()`。
- [x] EldritchSystem 接入导演、尸体回收；HookGame.Town/HookGame.Eldritch 记录封顶击杀进度，保持奖励事务。
- [x] EDR3/TWN3 编码、字段边界和兼容版本；更新首次清理测试夹具。

### 2. 边界与回放

- [x] 导演数值与上限、卡片等待、堵门、休眠、子代、恢复与恶意快照测试。
- [x] 修改 TownNetworkTests：确认四客户端运行新导演并保留存档恢复验证。
- [x] 检查城区 UI 清理文字是否仍误导“永久安全”，仅修正受影响文案。

### 3. 验证与交付

- [x] `./scripts/verify-hook-town.sh`、`./scripts/verify-hook-eldritch.sh`，必要时针对失败定位并重跑。
- [x] `./scripts/sync-hook-island.sh`；Unity 运行时代码编译，记录可达到的验证边界。
- [x] 完成代码审查与文档结果记录，明确旧快照不兼容及未人工试玩。
- [x] 仅暂存本任务文件，中文 commit，push 当前分支并检查远端结果。


## 执行记录

- 设计独立检查：无需要用户补充的阻塞问题。
- RED：旧城镇已支付尸体不再生；新增预算接口在实现前缺失。GREEN：完成导演、进度和快照后通过。
- 独立整体验证发现城镇服务UI不发送旧Shopping位。裁定使用三米服务范围统一排除，而不是扩展输入格式；真实Step回归先失败、修复后通过。
- 最终：城镇73项、异怪27项，共100项通过（含15项新增导演测试、两组四客户端UDP）；Unity程序集同步且运行时/测试程序集编译通过。既有测试脚本有1条BakeMesh弃用警告，未在Unity Play人工试玩。
- 用户明确授权提交和push；仅提交本任务文件，保留现有ProjectSettings修改。
