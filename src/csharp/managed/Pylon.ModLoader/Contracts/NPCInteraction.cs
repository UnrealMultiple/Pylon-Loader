using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;

namespace Pylon.ModLoader.Contracts;
/// <summary>城镇 NPC 对话里的一个交互按钮：作者在 RegisterChatButtons 里往列表加，前两个会显示。</summary>
public abstract class NPCInteraction
{
    public Player LocalPlayer => Main.LocalPlayer;

    public NPC? TalkNPC => (uint)LocalPlayer.talkNPC < 200u ? Main.npc[LocalPlayer.talkNPC] : null;

    public int TalkNPCType => TalkNPC?.type ?? 0;

    public virtual bool ShowExclamation => false;

    public abstract bool Condition();

    public abstract string GetText();

    public abstract void Interact();

    public virtual bool TryAddCoins(ref Color textColor, out int coins)
    {
        coins = 0;
        return false;
    }

    public virtual void TextColor(ref Color normal, ref Color mouseOver, bool hovering) { }
}
