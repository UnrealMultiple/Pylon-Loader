using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;

namespace Pylon.ModLoader;

/// <summary>自定义增益/减益；一个类型一个实例（buff 是"编号 + 时间"，不是实体）。</summary>
public abstract class ModBuff : ModType
{
    /// <summary>贴图相对 Mod 目录的路径（默认 assets/buffs/&lt;Name&gt;.png）。</summary>
    public override string TexturePath => $"assets/buffs/{Name}.png";

    /// <summary>显示名。</summary>
    public virtual string DisplayName { get; set; } = "";

    /// <summary>说明文字。</summary>
    public virtual string Description { get; set; } = "";

    /// <summary>从 Mod 菜单里藏起来。</summary>
    public virtual bool HideFromModMenu { get; set; } = false;

    /// <summary>只放共享数值（Main.debuff[Type]、buffNoSave、BuffID.Sets 等）。</summary>
    public override void SetStaticDefaults() { }

    /// <summary>SetStaticDefaults 之后一次。</summary>
    public virtual void PostStaticDefaults() { }

    /// <summary>所有内容装完之后一次。</summary>
    public virtual void PostSetupContent() { }

    /// <summary>每种语言一次：改 DisplayName。</summary>
    public virtual void ModifyDisplayName() { }

    /// <summary>每种语言一次：改 Description。</summary>
    public virtual void ModifyDescription() { }

    /// <summary>每帧，buff 在玩家身上时。</summary>
    public virtual void UpdatePlayer(Player player, int buffIndex) { }

    /// <summary>每帧，buff 在 NPC 身上时。</summary>
    public virtual void UpdateNPC(NPC npc, int buffIndex) { }

    /// <summary>buff 加到玩家身上时。</summary>
    public virtual void ApplyPlayer(Player player, int buffTime) { }

    /// <summary>buff 加到 NPC 身上时。</summary>
    public virtual void ApplyNPC(NPC npc, int buffTime) { }

    /// <summary>已经有时再加一次；false = 不续时间。</summary>
    public virtual bool ReApplyPlayer(Player player, int buffTime, int buffIndex) => true;

    /// <summary>已经有时再加一次；false = 不续时间。</summary>
    public virtual bool ReApplyNPC(NPC npc, int buffTime, int buffIndex) => true;

    /// <summary>点图标取消时；true/false 定，null = 交给游戏（debuff 不掉）。</summary>
    public virtual bool? CanRemove(Player player, int buffTime, int buffIndex, bool debuff) => null;

    /// <summary>点图标取消之后。</summary>
    public virtual void OnRemove(Player player, int buffTime, int buffIndex) { }

    /// <summary>条上的名字和说明（三个都是可写的引用）。</summary>
    public virtual void ModifyBuffText(ref string buffName, ref string tip, ref int rare) { }

    /// <summary>条上这个图标；false = 图标和时间文字都不画（见 BuffDrawParams 的限制）。</summary>
    public virtual bool PreDraw(SpriteBatch spriteBatch, int buffIndex, BuffDrawParams drawParams) => true;

    /// <summary>条上这个图标画完之后。</summary>
    public virtual void PostDraw(SpriteBatch spriteBatch, int buffIndex, BuffDrawParams drawParams) { }

    /// <summary>这个类型是不是 mod buff。</summary>
    public static bool IsModType(int type) => ModContent.IsCustom(ContentKind.Buff, type);

    /// <summary>这个类型的 ModBuff 实例；不是 mod buff 就是 null。</summary>
    public static ModBuff GetModBuff(int type) => (ModContent.Find(ContentKind.Buff, type) as ModBuff)!;
}
