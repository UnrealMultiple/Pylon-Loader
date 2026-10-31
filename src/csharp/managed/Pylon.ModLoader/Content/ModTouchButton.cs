using Terraria.Localization;

namespace Pylon.ModLoader;

/// <summary>一个 mod 的触屏按钮：玩家在游戏自己的控制编辑里放置它，mod 像读 tModLoader 的 ModKeybind 一样读它。</summary>
public sealed class ModTouchButton
{
    private readonly TouchButtonEntry _entry;
    private readonly LocalizedText _displayName;

    internal ModTouchButton(TouchButtonEntry entry, string name)
    {
        _entry = entry;
        _displayName = new LocalizedText(entry.TextKey ?? ("TouchButtons." + name + ".DisplayName"), name);
    }

    /// <summary>注册条目（管线内部用；注册后由 TouchButtonLoader 填）。</summary>
    internal void __SetEntry(TouchButtonEntry entry) { }

    /// <summary>按钮名（注册时那个，Mod 内唯一）。</summary>
    public string Name => _entry.Name;

    /// <summary>显示名；<c>Value</c> 是当前语言里的名字。</summary>
    public LocalizedText DisplayName => _displayName;

    // 状态由 TouchButtonLoader.Tick 按 Main.GameUpdateCount 每帧算一次，读之前先 Tick。

    /// <summary>正被按住。</summary>
    public bool Current
    {
        get
        {
            TouchButtonLoader.Tick();
            return _entry.Current;
        }
    }

    /// <summary>这次更新里按下了（一次触摸只算一次）。</summary>
    public bool JustPressed
    {
        get
        {
            TouchButtonLoader.Tick();
            return _entry.JustPressed;
        }
    }

    /// <summary>这次更新里松开了。</summary>
    public bool JustReleased
    {
        get
        {
            TouchButtonLoader.Tick();
            return _entry.JustReleased;
        }
    }

    /// <summary>上一次更新里是按住的。</summary>
    public bool Old
    {
        get
        {
            TouchButtonLoader.Tick();
            return _entry.Old;
        }
    }

    /// <summary>Current，但聊天、对话或牌子开着时为 false（本版暂与 Current 同）。</summary>
    public bool RetroCurrent => Current;

    /// <summary>这个按钮在不在当前布局里（给提示用："去 Controls 里加按钮"）。</summary>
    public bool IsBound => TouchButtonLoader.IsBound(_entry);

    /// <summary>tModLoader 风格的键名表：按钮在布局里给 {Name}，否则空。</summary>
    public IEnumerable<string> GetAssignedKeys() =>
        IsBound ? new[] { Name } : Array.Empty<string>();

    /// <summary>布局保存用的完整键（mod/Name），别改名字，否则保存的位置找不回来。</summary>
    public string FullName => _entry.Key;

    /// <summary>当字符串用：ModTouchButton(名字)。</summary>
    public override string ToString() => "ModTouchButton(" + Name + ")";
}
