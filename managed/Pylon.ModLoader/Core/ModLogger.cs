using System;
using Pylon.Mod;

namespace Pylon.ModLoader
{
    /// <summary>宿主日志的 Mod 前缀包装（等级只影响前缀，便于 grep）。</summary>
    public sealed class ModLogger
    {
        private readonly IModContext _context;

        internal ModLogger(IModContext context) => _context = context;

        // 宿主已经给每行加了 `[<mod id>]` 前缀，这里不再重复。
        public void Info(string message) => _context.Log(message);
        public void Warn(string message) => _context.Log("警告：" + message);
        public void Error(string message) => _context.Log("错误：" + message);
    }
}
