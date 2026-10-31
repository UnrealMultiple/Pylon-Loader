using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using ImGuiApi = Pylon.ImGui.ImGui;
using Pylon.ImGui;

namespace Pylon.ModLoader.UI
{
    /// <summary>
    /// 中文输入的关键：输入框只做只读展示，真正的编辑交给系统软键盘（TouchScreenKeyboard），
    /// 每帧把键盘里的**整串文本**回写进 ImGui 的缓冲区。
    ///
    /// 这样拼音候选、选词、串中插入、选中替换都由系统输入法负责，ImGui 这边不需要模拟
    /// 逐字增量与退格——旧实现正是逐字补 igAddInputCharactersUTF8/退格才在中文下崩。
    /// 每个控件用按 ImGuiID 固定的字节缓冲（跨帧稳定，不能是每帧新建的栈缓冲）。
    /// </summary>
    internal static class ImGuiInputText
    {
        private const int Capacity = 4096;
        private const int CloseCooldownFrames = 30;
        private const int MaxOpenAttempts = 8;

        private static readonly Dictionary<uint, byte[]> Buffers = new();
        private static uint _activeId;
        private static byte[] _activeBuffer;
        private static string _pendingWrite;

        private static TouchScreenKeyboard _keyboard;
        private static string _lastKeyboardText;
        private static int _openAttempts;
        private static int _cooldown;
        private static bool _usedThisFrame;

        internal static bool KeyboardOpen => _keyboard != null && _keyboard.active;

        /// <summary>只读输入框（点击即弹系统键盘）。返回文本是否变化。</summary>
        internal static unsafe bool Draw(string label, ref string value)
        {
            value ??= string.Empty;
            uint id = ImGuiApi.GetID(label);
            if (!Buffers.TryGetValue(id, out var buf))
            {
                buf = new byte[Capacity];
                Buffers[id] = buf;
            }

            if (id != _activeId)
                WriteUtf8(buf, value);             // 非活动：外部值覆盖缓冲
            else if (_pendingWrite != null)
            {
                WriteUtf8(buf, _pendingWrite);      // 活动：键盘整串回写
                _pendingWrite = null;
            }

            bool changed;
            var labelBytes = Encoding.UTF8.GetBytes(label + "\0");
            fixed (byte* lp = labelBytes)
            fixed (byte* bp = buf)
            {
                changed = ImGuiNative.igInputText(lp, bp, (uint)buf.Length,
                    ImGuiInputTextFlags.ReadOnly | ImGuiInputTextFlags.NoUndoRedo,
                    IntPtr.Zero, null) != 0;
            }

            if (ImGuiNative.igIsItemActive() != 0)
            {
                _activeId = id;
                _activeBuffer = buf;
                _usedThisFrame = true;
            }

            var text = ReadUtf8(buf);
            if (!string.Equals(text, value, StringComparison.Ordinal))
            {
                value = text;
                changed = true;
            }
            return changed;
        }

        /// <summary>NewFrame 之后、绘制之前：把键盘文本同步成待回写。</summary>
        internal static void TickKeyboard()
        {
            if (!ImGuiContext.Created) return;
            if (_cooldown > 0) _cooldown--;

            if (_activeId == 0)
            {
                if (_keyboard != null) CloseKeyboard();
                return;
            }
            if (_keyboard == null)
            {
                if (_cooldown == 0 && _openAttempts < MaxOpenAttempts) OpenKeyboard();
                return;
            }
            if (!_keyboard.active)
            {
                // 用户/系统自行收起：结束本次编辑，允许重新点开
                _openAttempts = 0;
                CloseKeyboard();
                _activeId = 0;
                return;
            }

            string current;
            try { current = _keyboard.text ?? string.Empty; } catch { return; }
            if (!string.Equals(current, _lastKeyboardText, StringComparison.Ordinal))
            {
                _pendingWrite = current;
                _lastKeyboardText = current;
            }
        }

        /// <summary>绘制结束：本帧没有活动输入框就收尾。</summary>
        internal static void EndFrame()
        {
            if (!_usedThisFrame && _activeId != 0)
            {
                _activeId = 0;
                _activeBuffer = null;
                if (_keyboard != null) CloseKeyboard();
            }
            _usedThisFrame = false;
        }

        private static void OpenKeyboard()
        {
            var seed = _activeBuffer != null ? ReadUtf8(_activeBuffer) : string.Empty;
            try
            {
                _keyboard = TouchScreenKeyboard.Open(seed, TouchScreenKeyboardType.Default,
                    false, false, false, false, string.Empty, 0);
                _lastKeyboardText = seed;
                _openAttempts++;
                ImGuiNativeApi.SetKeyboardGuardMs(1500);   // 键盘动画期间别重建交换链
            }
            catch (Exception e)
            {
                ModUI.Logger?.Warn("打开软键盘失败：" + e.Message);
                _keyboard = null;
            }
        }

        private static void CloseKeyboard()
        {
            try { if (_keyboard != null) _keyboard.active = false; } catch { }
            _keyboard = null;
            _lastKeyboardText = null;
            _pendingWrite = null;
            _openAttempts = 0;
            _cooldown = CloseCooldownFrames;
        }

        private static void WriteUtf8(byte[] buffer, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
            var length = Math.Min(bytes.Length, buffer.Length - 1);
            Array.Copy(bytes, buffer, length);
            buffer[length] = 0;
        }

        private static string ReadUtf8(byte[] buffer)
        {
            int length = Array.IndexOf(buffer, (byte)0);
            if (length < 0) length = buffer.Length;
            return Encoding.UTF8.GetString(buffer, 0, length);
        }
    }
}
