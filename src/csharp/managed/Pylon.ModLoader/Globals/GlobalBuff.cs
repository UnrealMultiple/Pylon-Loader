using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>作用于所有 buff（游戏的与 mod 的）的横切类；一个类一个实例，钩子收到 buff 类型自己挑。</summary>
public abstract class GlobalBuff : ModType
{
    /// <summary>只放共享数值，不要碰游戏对象。</summary>
    public override void SetStaticDefaults() { }

    /// <summary>所有内容安装完成之后一次（跨 Mod 引用放这里）。</summary>
    public virtual void PostSetupContent() { }

    /// <summary>每帧，buff 在这个玩家身上时（buffIndex 是可写引用：移掉 buff 时减 1）。</summary>
    public virtual void UpdatePlayer(int type, Player player, ref int buffIndex) { }

    /// <summary>每帧，buff 在这个 NPC 身上时（buffIndex 是可写引用：移掉 buff 时减 1）。</summary>
    public virtual void UpdateNPC(int type, NPC npc, ref int buffIndex) { }

    /// <summary>已经有时又加一次；true = 游戏不续时间（与 ModBuff 的相反）。</summary>
    public virtual bool ReApplyPlayer(int type, Player player, int time, ref int buffIndex) => false;

    /// <summary>已经有时又加一次；true = 游戏不续时间（与 ModBuff 的相反）。</summary>
    public virtual bool ReApplyNPC(int type, NPC npc, int time, ref int buffIndex) => false;

    /// <summary>条上的名字与说明（三个都是可写的引用；rare 在手机版不改颜色）。</summary>
    public virtual void ModifyBuffText(int type, ref string buffName, ref string tip, ref int rare) { }

    /// <summary>buff 条上这个图标；false = 不画（PostDraw 照旧跑）。</summary>
    public virtual bool PreDraw(SpriteBatch spriteBatch, int type, int buffIndex, BuffDrawParams drawParams) => true;

    /// <summary>buff 条上这个图标画完之后。</summary>
    public virtual void PostDraw(SpriteBatch spriteBatch, int type, int buffIndex, BuffDrawParams drawParams) { }

    /// <summary>点图标要取消这个 buff 时；false = buff 留着。</summary>
    public virtual bool RightClick(int type, int buffIndex) => true;
}
