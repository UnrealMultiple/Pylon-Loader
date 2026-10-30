using System;
using System.Numerics;
using Pylon.ImGui;

// Consumer-style smoke test: proves the ImGui.NET-style surface compiles and works.
internal static class Smoke
{
    public static int Main()
    {
        Run();
        return 0;
    }

    public static unsafe void Run()
    {
        // --- IO must work (the user's core complaint before the rewrite) ---
        ImGuiIOPtr io = ImGui.GetIO();
        io.DisplaySize = new Vector2(1280, 720);
        io.DisplayFramebufferScale = new Vector2(1f, 1f);
        io.DeltaTime = 1f / 60f;
        io.FontGlobalScale = 1.25f;
        io.MousePos = new Vector2(100, 100);
        NullTerminatedString ini = io.IniFilename;   // read-only, as in ImGui.NET
        _ = ini;
        io.AddMousePosEvent(100, 100);
        io.AddMouseButtonEvent(0, true);
        io.AddKeyEvent(ImGuiKey.A, true);
        io.AddInputCharactersUTF8("abc");

        ImGuiStylePtr style = ImGui.GetStyle();
        style.WindowRounding = 4f;
        style.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.1f, 0.1f, 0.1f, 0.9f);  // ref-indexer write

        // --- window / widget surface with ImGui.NET-style optional args ---
        bool open = true;
        bool toggle = false;
        if (ImGui.Begin("Test Window", ref open, ImGuiWindowFlags.NoDecoration))
        {
            ImGui.Text("hello world");
            ImGui.Button("Click");                       // default size
            ImGui.Checkbox("toggle", ref toggle);
            float f = 0.5f;
            ImGui.SliderFloat("slider", ref f, 0f, 1f);  // default format
            ImGui.DragFloat2("pos", ref f, 0.1f, 0f, 1f);
            int iv = 3;
            ImGui.DragInt("int", ref iv);
            string[] items = { "A", "B", "C" };
            int sel = 0;
            ImGui.Combo("combo", ref sel, items, items.Length);

            ImGui.Separator();
            ImGui.NewLine();
            ImGui.End();
        }

        ImGui.Render();
        ImDrawListPtr dl = ImGui.GetBackgroundDrawList();
        dl.AddRect(new Vector2(0, 0), new Vector2(10, 10), 0xFFFFFFFF);
        dl.AddLine(new Vector2(0, 0), new Vector2(1, 1), 0xFFFFFFFF, 1f);
        dl.AddText(new Vector2(0, 0), 0xFFFFFFFF, "hi");
        dl.PushClipRect(new Vector2(0, 0), new Vector2(100, 100), true);
        dl.PopClipRect();

        ImGui.SetNextWindowSize(new Vector2(400, 300), ImGuiCond.Once);
        ImGui.SetNextWindowPos(new Vector2(10, 10), ImGuiCond.FirstUseEver);

        // --- native/context layer (IntPtr in reference) ---
        IntPtr ctx = ImGui.CreateContext();
        ImGui.SetCurrentContext(ctx);
        ImGui.DestroyContext(ctx);

        // --- enums/structs/misc exist ---
        ImGuiCol col = ImGuiCol.Text;
        ImGuiKey key = ImGuiKey.A;
        ImGuiCond cond = ImGuiCond.Always;
        ImGuiMouseButton mb = ImGuiMouseButton.Left;
        ImGuiTabBarFlags tb = ImGuiTabBarFlags.None;
        _ = col; _ = key; _ = cond; _ = mb; _ = tb;
    }
}
