namespace Pylon.ModLoader.Content;
/// <summary>Lang.TML.cs 的等价扩展成员；时长本地化直走 interop 静态调用（无原生依赖）。</summary>
public static class LangTml
{
    extension(Terraria.Lang)
    {
        /// <summary>
        /// 把一段时长按游戏当前语言本地化成「X 分 X 秒」类文案（tML <c>Lang.LocalizedDuration</c>）。
        /// abbreviated = 短格式；showAllAvailableUnits = 把所有可用单位都展示出来。
        /// </summary>
        public static string LocalizedDuration(TimeSpan time, bool abbreviated, bool showAllAvailableUnits) =>
            Terraria.Lang.LocalizedDuration(new Il2CppSystem.TimeSpan(time.Ticks), abbreviated, showAllAvailableUnits) ?? "";
    }
}
