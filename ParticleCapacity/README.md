# ParticleCapacity · 粒子容量扩展

解除 In Falsus 粒子库存达到9999颗后停止发放普通粒子的门槛，将容量提高到 **65535颗**。安装后自动生效，无需操作按钮。

## 行为与边界

- 超过9999颗后，结算仍按原生掉落规则发放粒子；库存容量显示同步使用新上限。
- 不改变掉落概率、粒子种类、效能或技能，不补发安装前未获得的奖励。
- 保留游戏原生库存和存档格式；不添加额外存档文件。
- 原生数量和制卡材料编号使用16位整数，因此容量为65535颗，不是无限库存。同批奖励超过剩余容量时，仅接收可容纳的数量。
- 卸载后已有粒子不会由本模组删除；原版9999颗获得门槛恢复，超限库存需要消耗或融合后才能继续获得普通粒子。

## 安装与兼容

适用于 Windows IL2CPP 版 In Falsus 1.0.7，依赖 `BepInEx-BepInExPack_IL2CPP-6.0.755`。将 `ParticleCapacity.dll` 放入当前 profile 的 `BepInEx/plugins/MengLei-ParticleCapacity/`，下次启动游戏生效。

本模组可独立安装，也可与 ParticleFusion、SkillSelection、KeyBindingExtensions、PotentialSystem 同时使用。已完成1.0.7二进制静态核对和 Release 编译，实际游戏行为尚待确认。
