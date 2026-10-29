namespace Pylon.Mod
{
    public interface IPylonMod
    {
        void Load(IModContext context);

        void OnContentReady(IModContext context) { }
    }

    public interface IModContext
    {
        string Id { get; }
        string Version { get; }
        string Directory { get; }
        void Log(string message);

        void RegisterImGuiTab(IModImGuiTab tab);
    }

    public interface IModImGuiTab
    {
        string Title { get; }

        void Draw();
    }

    public static class ImGuiTabRegistry
    {
        private static readonly System.Collections.Generic.List<IModImGuiTab> Tabs = new();

        public static System.Collections.Generic.IReadOnlyList<IModImGuiTab> All => Tabs;

        public static void Register(IModImGuiTab tab)
        {
            if (tab == null) return;
            if (Tabs.Contains(tab)) return;
            Tabs.Add(tab);
        }
    }
}
