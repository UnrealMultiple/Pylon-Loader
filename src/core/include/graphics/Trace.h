// 层：内核层 / graphics —— 一帧 GL 调用序列（排"效果画在哪一帧"的问题）
#pragma once

namespace pylon::graphics::trace {

/// 请下一帧。游戏线程。
void RequestFrame();

/// 渲染线程：请求还在就开头。
void Begin();

/// 记一条：kind 是两三个字母的标记，参数是它的整数值。
void Record(const char* kind, int a = 0, int b = 0, int c = 0, int d = 0);

/// 一次绘制（连续绘制合并成 D<n>）。
void CountDraw();

/// 帧末：有内容就整行打出去。
void Flush();

}  // namespace pylon::graphics::trace
