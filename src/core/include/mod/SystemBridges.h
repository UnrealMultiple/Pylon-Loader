// 层：内核层 / mod —— SystemStages 中体桥（P1-11 剩余）
// 游戏把每帧更新的子阶段内联在 DoUpdateInWorld / DoDraw / UpdateTime 的方法体中部，
// 入口钩拆不出 Begin/End；跳板由 ShadowHook 出（HookApply 本就支持任意地址），
// 这里只补托管做不了的桥体（全寄存器保存 → 分派 → 恢复 → 尾跳，裸汇编）。
#pragma once

#include <cstdint>

namespace pylon::mod {

/// 阶段下标：0..6 是 DoUpdateInWorld 的七个子阶段，7 = DoDraw 的 Transform，
/// 8 = UpdateTime 的时钟，9 = Player.GetDyeTraderReward 中部的染料商奖励池。
/// 与托管侧 SystemStages.cs 同表，增删要两边同步。
constexpr int32_t kSystemStageCount = 10;
constexpr int32_t kSystemStageClock = 8;
constexpr int32_t kSystemStageDyeReward = 9;

/// 阶段回调：`value` 随阶段而定——时钟阶段（8）是可改的 double，游戏随后读它推进昼夜；
/// 染料商阶段（9）是 DyeFrame*（玩家 x19 / 奖励池 List x21），池改写在托管侧完成。
using SystemStageFn = void (*)(int32_t stage, void* value);

/// 指纹核对（偏移处 16 字节必须逐条等于 expected）+ 挂桥 + 跳板登记，一步完成。
/// @return 0 成功；-1 参数非法；-2 指纹不符；-3 挂钩失败；-4 stage 非法。
int32_t SystemBridgeInstall(int32_t stage, void* body, int32_t byte_offset,
                            const uint32_t* expected, void** trampoline);

/// 注册阶段回调（每阶段一个槽，mod 扇出在托管侧做）；null = 退订。
void SystemBridgeSubscribe(int32_t stage, SystemStageFn callback);

}  // namespace pylon::mod
