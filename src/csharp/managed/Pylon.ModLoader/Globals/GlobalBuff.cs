using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Content;
using Terraria;

namespace Pylon.ModLoader.Globals;
/// <summary>作用于所有 buff（游戏的与 mod 的）的横切类；一个类一个实例，钩子收到 buff 类型自己挑。</summary>
public abstract class GlobalBuff : ModType
{
    /// <summary>只放共享数值，不要碰游戏对象。</summary>
    public override void SetStaticDefaults() { }

    /// <summary>每帧，buff 在这个玩家身上时（buffIndex 是可写引用：移掉 buff 时减 1）。</summary>
    public virtual void Update(int type, Player player, ref int buffIndex) { }

    /// <summary>每帧，buff 在这个 NPC 身上时（buffIndex 是可写引用：移掉 buff 时减 1）。</summary>
    public virtual void Update(int type, NPC npc, ref int buffIndex) { }

    /// <summary>已经有时又加一次；true = 这一次不续时间（保持剩余时间）。</summary>
    public virtual bool ReApply(int type, Player player, int time, int buffIndex) => false;

    /// <summary>已经有时又加一次；true = 这一次不续时间（保持剩余时间）。</summary>
    public virtual bool ReApply(int type, NPC npc, int time, int buffIndex) => false;

    /// <summary>条上的名字与说明（三个都是可写的引用；rare 在手机版不改颜色）。</summary>
    public virtual void ModifyBuffText(int type, ref string buffName, ref string tip, ref int rare) { }

    /// <summary>自定义 buff 说明的尺寸：每段文字往 sizes 里追加一块。</summary>
    public virtual void CustomBuffTipSize(string buffTip, List<Vector2> sizes) { }

    /// <summary>自己画自定义 buff 说明；originX/originY 是绘制原点。</summary>
    public virtual void DrawCustomBuffTip(string buffTip, SpriteBatch spriteBatch, int originX, int originY) { }

    /// <summary>buff 条上这个图标；drawParams 可写，false = 不画（PostDraw 照旧跑）。</summary>
    public virtual bool PreDraw(SpriteBatch spriteBatch, int type, int buffIndex, ref BuffDrawParams drawParams) => true;

    /// <summary>buff 条上这个图标画完之后。</summary>
    public virtual void PostDraw(SpriteBatch spriteBatch, int type, int buffIndex, BuffDrawParams drawParams) { }

    /// <summary>点图标要取消这个 buff 时；false = buff 留着。</summary>
    public virtual bool RightClick(int type, int buffIndex) => true;
}
