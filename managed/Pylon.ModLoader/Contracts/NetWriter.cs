using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;

namespace Pylon.ModLoader
{
    /// <summary>写值容器（ModPacket 的基类）：mod 侧按顺序塞值，Send 时整包序列化；读侧对应 NetReader。</summary>
    public class NetWriter
    {
        /// <summary>空容器：之后塞的值按写入顺序进 Values。</summary>
        public NetWriter()
        {
            Values = new List<object>();
        }

        /// <summary>按写入顺序存的值（数字、文本、bool、数组、Vector2…）；序列化成载荷的事归管道。</summary>
        public List<object> Values { get; }

        /// <summary>塞一个值；是 Vector2 就存成它的 X/Y（游戏里"带 X/Y 的对象"就是 Vector2）。</summary>
        public void Write(object value)
        {
            if (value is Vector2 v) Values.Add(new Vector2(v.X, v.Y));
            else Values.Add(value);
        }

        /// <summary>把几个 bool 拷一份成数组塞进去。</summary>
        public void WriteFlags(params bool[] flags) => Values.Add((bool[])flags.Clone());

        /// <summary>塞一个 Vector2（存成 X/Y，和 Write 的向量分支一样）。</summary>
        public void WriteVector2(Vector2 v) => Values.Add(new Vector2(v.X, v.Y));

        // 下面 12 个都是 Write 的别名：名字照抄，参数收窄成对应 C# 类型（同 BinaryWriter 命名）。
        // 值序列化成什么形态是 ModNet 的事（ModPacket.cs:6-9）。

        /// <summary>Write 的 byte 别名（存原值）。</summary>
        public void WriteByte(byte value) => Write(value);

        /// <summary>Write 的 sbyte 别名（存原值）。</summary>
        public void WriteSByte(sbyte value) => Write(value);

        /// <summary>Write 的 short 别名（存原值）。</summary>
        public void WriteInt16(short value) => Write(value);

        /// <summary>Write 的 ushort 别名（存原值）。</summary>
        public void WriteUInt16(ushort value) => Write(value);

        /// <summary>Write 的 int 别名（存原值）。</summary>
        public void WriteInt32(int value) => Write(value);

        /// <summary>Write 的 uint 别名（存原值）。</summary>
        public void WriteUInt32(uint value) => Write(value);

        /// <summary>Write 的 long 别名（存原值）。</summary>
        public void WriteInt64(long value) => Write(value);

        /// <summary>Write 的 float 别名（存原值）。</summary>
        public void WriteSingle(float value) => Write(value);

        /// <summary>Write 的 double 别名（存原值）。</summary>
        public void WriteDouble(double value) => Write(value);

        /// <summary>Write 的 bool 别名（存原值）。</summary>
        public void WriteBoolean(bool value) => Write(value);

        /// <summary>Write 的 string 别名（存原值）。</summary>
        public void WriteString(string value) => Write(value);

        /// <summary>Write 的 7 位编码 int 别名（存原值）。</summary>
        public void Write7BitEncodedInt(int value) => Write(value);
    }
}