using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader.Pipeline;
using Terraria;

namespace Pylon.ModLoader.Content;
/// <summary>自定义增益/减益；一个类型一个实例（buff 是"编号 + 时间"，不是实体）。</summary>
public abstract class ModBuff : ModType
{
    /// <summary>贴图相对 Mod 目录的路径（默认 assets/buffs/&lt;Name&gt;.png）。</summary>
    public override string TexturePath => $"assets/buffs/{Name}.png";

    /// <summary>本地化分类名。</summary>
    public virtual string LocalizationCategory => "Buffs";

    /// <summary>显示名。</summary>
    public virtual string DisplayName { get; set; } = "";

    /// <summary>说明文字。</summary>
    public virtual string Description { get; set; } = "";

    /// <summary>只放共享数值（Main.debuff[Type]、buffNoSave、BuffID.Sets 等）。</summary>
    public override void SetStaticDefaults() { }

    /// <summary>每帧，buff 在玩家身上时；把 buffIndex 往回拨一格 = 这一帧少跑一个后续槽位，清零槽位 = 摘掉。</summary>
    public virtual void Update(Player player, ref int buffIndex) { }

    /// <summary>每帧，buff 在 NPC 身上时；buffIndex 用法同玩家侧。</summary>
    public virtual void Update(NPC npc, ref int buffIndex) { }

    /// <summary>已经有时再加一次；true = 这一次不续时间（保持剩余时间）。</summary>
    public virtual bool ReApply(Player player, int time, int buffIndex) => false;

    /// <summary>已经有时再加一次；true = 这一次不续时间（保持剩余时间）。</summary>
    public virtual bool ReApply(NPC npc, int time, int buffIndex) => false;

    /// <summary>手动点图标取消时调用；false 否决，不影响到期或游戏逻辑删除。</summary>
    public virtual bool RightClick(int buffIndex) => true;

    /// <summary>条上的名字和说明（三个都是可写的引用）。</summary>
    public virtual void ModifyBuffText(ref string buffName, ref string tip, ref int rare) { }

    /// <summary>条上这个图标；drawParams 可写（可换贴图/颜色/位置），false 仅取消原版图标，时间文字、输入与 PostDraw 仍运行。</summary>
    public virtual bool PreDraw(SpriteBatch spriteBatch, int buffIndex, ref BuffDrawParams drawParams) => true;

    /// <summary>条上这个图标；false 仅取消原版图标，时间文字、输入与 PostDraw 仍运行。</summary>
    public virtual void PostDraw(SpriteBatch spriteBatch, int buffIndex, BuffDrawParams drawParams) { }

    /// <summary>相对 ModBuff 覆写了哪些钩子：安装时算一次挂在模板上，分派期只做位运算。</summary>
    internal HookMask Hooks { get; set; }

    /// <summary>这个钩子要不要分派给本实例。</summary>
    internal bool HookEnabled(HookId hook) => Hooks.Has(hook);
}
