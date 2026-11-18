using Terraria.DataStructures;

namespace Pylon.ModLoader;

/// <summary>基线 SpriteFrame.TML.cs 的等价扩展成员；构造函数不能扩展，用 Create 静态扩展。</summary>
public static class SpriteFrameTml
{
    extension(SpriteFrame)
    {
        public static SpriteFrame Create(byte columns, byte rows, byte currentColumn, byte currentRow)
        {
            var frame = new SpriteFrame(columns, rows)
            {
                CurrentColumn = currentColumn,
                CurrentRow = currentRow,
                PaddingX = 2,
                PaddingY = 2,
            };

            return frame;
        }
    }
}
