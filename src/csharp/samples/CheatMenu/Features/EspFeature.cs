using System;
using Pylon.NaviteAPI.ImGui;
using Pylon.ModLoader.ImGuiHost;
using Terraria;
using CheatMenu.Infra;
using Game = CheatMenu.Infra.Game;
using XnaVector2 = Il2CppMicrosoft.Xna.Framework.Vector2;

namespace CheatMenu.Features;

/// <summary>
/// 绘制 (ESP)。1:1 对应原版 <c>ESPTool</c>（视觉）：玩家/NPC/弹幕碰撞箱与连线。
/// 绘制走 ImGui 背景绘制列表（<see cref="ModOverlay"/> 的 background 通道，
/// 等价原版 <c>ImGui::GetBackgroundDrawList()</c>），**不再**用游戏 SpriteBatch 界面层。
/// 坐标换算：世界像素 → <see cref="WorldEdit.WorldToScreen"/>
/// （内部 XNAUnityRunner.AdjustWorldForRealScreen）→ 覆盖层物理像素，
/// 与 ImGui <c>DisplaySize</c> 同一空间，可直接作为 AddRect/AddLine 坐标。
/// 颜色以 0xAARRGGBB 持久化，绘制时重排成 ImGui 的 0xAABBGGRR 布局。
/// </summary>
public sealed class EspFeature : CheatFeature
{
    private const string OverlayKey = "CheatMenu/esp";

    /// <summary>碰撞箱描边粗细（覆盖层物理像素，与原版 AddRect 的 1.5f 一致）。</summary>
    private const float BoxThickness = 1.5f;

    public override string Id => "visual.esp";
    public override string Title => "绘制 (ESP)";
    public override CheatCategory Category => CheatCategory.Visual;
    public override string SubPage => "主要";

    public override void Install() => RegisterOverlay(OverlayKey, DrawEsp);

    public override void DrawMenu()
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is null) return;

        ModernUI.BeginCard("绘制 (ESP)", "ESP");

        RowToggle("##espenabled", "启用绘制", cfg.EspEnabled, v => cfg.EspEnabled = v);
        if (cfg.EspEnabled)
        {
            RowToggle("##espplayer", "玩家碰撞箱", cfg.EspPlayerBoxes, v => cfg.EspPlayerBoxes = v);
            RowToggle("##espnpc", "NPC碰撞箱", cfg.EspNpcBoxes, v => cfg.EspNpcBoxes = v);
            RowToggle("##espproj", "弹幕碰撞箱", cfg.EspProjectileBoxes, v => cfg.EspProjectileBoxes = v);
            RowToggle("##esptracerp", "玩家连线", cfg.EspPlayerTracers, v => cfg.EspPlayerTracers = v);
            RowToggle("##esptracern", "NPC连线", cfg.EspNpcTracers, v => cfg.EspNpcTracers = v);
            RowToggle("##esptown", "绘制城镇NPC", cfg.EspDrawTownNpcs, v => cfg.EspDrawTownNpcs = v);

            ModernUI.SectionTitle("颜色");
            ColorRow("本机玩家", "##espcollocal", cfg, nameof(CheatConfig.EspLocalPlayerColor));
            ColorRow("其他玩家", "##espcolother", cfg, nameof(CheatConfig.EspOtherPlayerColor));
            ColorRow("NPC", "##espcolnpc", cfg, nameof(CheatConfig.EspNpcColor));
            ColorRow("弹幕", "##espcolproj", cfg, nameof(CheatConfig.EspProjectileColor));
            ColorRow("玩家连线", "##espcolltp", cfg, nameof(CheatConfig.EspPlayerTracerColor));
            ColorRow("NPC连线", "##espcolltn", cfg, nameof(CheatConfig.EspNpcTracerColor));
        }

        ModernUI.EndCard();
    }

    /// <summary>
    /// 颜色块取色器。1:1 对应原版 <c>UI::ColorSwatch</c>（ModernWidgets.h）：
    /// 左侧标签 + 右缘 <c>ImGui::ColorEdit4</c>（NoInputs|NoLabel|NoBorder），点击弹原生取色器。
    /// 配置里存 <c>0xAARRGGBB</c>，取色器用 ImGui 的 <c>0xAABBGGRR</c>（IM_COL32）布局，两边互转。
    /// </summary>
    private static void ColorRow(string label, string id, CheatConfig cfg, string fieldName)
    {
        var field = typeof(CheatConfig).GetField(fieldName);
        if (field is null) return;

        ModernUI.BeginRow();
        ModernUI.RowLabel(label);
        // 色块宽度 = ImGui 的 FrameHeight（NoInputs 的 ColorEdit4 实际画的就是这个尺寸）。
        // 原版 ColorSwatch 用 fs*0.55 作位移，是因为它的主题把 frame padding 压掉了；
        // 这里的主题没有，用 fs*0.55 会让控件右缘探出卡片右边界。
        var sw = ImGui.GetFrameHeight();
        var avail = ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(avail - sw, 0f));
        var value = (uint)field.GetValue(cfg)!;
        var rgba = ImGui.ColorConvertU32ToFloat4(ToImU32(value));
        Colors[0] = rgba.X;
        Colors[1] = rgba.Y;
        Colors[2] = rgba.Z;
        Colors[3] = rgba.W;
        if (ImGui.ColorEdit4(id, ref Colors[0],
                ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.NoLabel | ImGuiColorEditFlags.NoBorder))
        {
            field.SetValue(cfg, FromImRgba(Colors[0], Colors[1], Colors[2], Colors[3]));
            CheatFeatureHost.MarkDirty();
        }
        ModernUI.EndRow();
    }

    /// <summary>取色器缓冲（ImGui 的 <c>ColorEdit4</c> 只收 <c>ref float</c>，必须是连续 4 个 float）。</summary>
    private static readonly float[] Colors = new float[4];

    /// <summary>ImGui 的 0..1 RGBA → 配置的 <c>0xAARRGGBB</c>。</summary>
    private static uint FromImRgba(float r, float g, float b, float a) =>
        ((uint)Math.Clamp((int)MathF.Round(a * 255f), 0, 255) << 24) |
        ((uint)Math.Clamp((int)MathF.Round(r * 255f), 0, 255) << 16) |
        ((uint)Math.Clamp((int)MathF.Round(g * 255f), 0, 255) << 8) |
        (uint)Math.Clamp((int)MathF.Round(b * 255f), 0, 255);

    // ==================== 常驻绘制（ImGui 背景层） ====================

    /// <summary>
    /// 绘制体：每帧 ImGui 帧内调用。热路径约定——开关关闭/不在世界内时立即返回，
    /// 全程不分配（无委托、无容器、无字符串拼接）。
    /// </summary>
    private static void DrawEsp(ImDrawListPtr dl)
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is not { EspEnabled: true }) return;        // 开关关闭：第一行就返回
        if (Main.gameMenu || Main.mapFullscreen) return;    // 主菜单 / 全屏地图：不绘制

        var localCol = ToImU32(cfg.EspLocalPlayerColor);
        var otherCol = ToImU32(cfg.EspOtherPlayerColor);
        var npcCol = ToImU32(cfg.EspNpcColor);
        var projCol = ToImU32(cfg.EspProjectileColor);
        var tracerP = ToImU32(cfg.EspPlayerTracerColor);
        var tracerN = ToImU32(cfg.EspNpcTracerColor);

        // 本机玩家中心（连线起点，世界像素）；?null 是可空结构体，不分配
        var localCenter = Game.LocalPlayer?.Center;
        var myPlayer = Main.myPlayer;

        // ---- 玩家：碰撞箱 + 连线 ----
        var playerCount = 0;
        if (cfg.EspPlayerBoxes || cfg.EspPlayerTracers)
        {
            var players = Main.player;
            if (players is not null)
            {
                foreach (var p in players)
                {
                    if (p is not { active: true }) continue;
                    playerCount++;

                    if (cfg.EspPlayerBoxes)
                        DrawBox(dl, p.position, p.width, p.height, p.whoAmI == myPlayer ? localCol : otherCol);

                    if (cfg.EspPlayerTracers && localCenter is not null && p.whoAmI != myPlayer)
                        DrawTracer(dl, localCenter.Value, p.Center, tracerP);
                }
            }
        }

        // ---- NPC：碰撞箱 + 连线（cfg.EspDrawTownNpcs 控制城镇/友好 NPC） ----
        var npcCount = 0;
        if (cfg.EspNpcBoxes || cfg.EspNpcTracers)
        {
            var npcs = Main.npc;
            if (npcs is not null)
            {
                foreach (var n in npcs)
                {
                    if (n is not { active: true }) continue;
                    if (!cfg.EspDrawTownNpcs && n.friendly) continue;
                    npcCount++;

                    if (cfg.EspNpcBoxes)
                        DrawBox(dl, n.position, n.width, n.height, npcCol);

                    if (cfg.EspNpcTracers && localCenter is not null)
                        DrawTracer(dl, localCenter.Value, n.Center, tracerN);
                }
            }
        }

        // ---- 弹幕：碰撞箱 ----
        var projCount = 0;
        if (cfg.EspProjectileBoxes)
        {
            var projectiles = Main.projectile;
            if (projectiles is not null)
            {
                foreach (var pr in projectiles)
                {
                    if (pr is not { active: true }) continue;
                    projCount++;
                    DrawBox(dl, pr.position, pr.width, pr.height, projCol);
                }
            }
        }

        // 首次真正画出内容时记一行：用于真机确认「回调在跑、坐标在屏内、颜色非透明」。
        if (!_reported && playerCount + npcCount + projCount > 0)
        {
            _reported = true;
            var sample = localCenter ?? XnaVector2.Zero;
            var sp = WorldEdit.WorldToScreen(sample);
            CheatMenuMod.Log?.Info($"[esp] 首帧绘制：玩家 {playerCount} / NPC {npcCount} / 弹幕 {projCount}；" +
                $"本机屏坐标 ({sp.X:0},{sp.Y:0})，覆盖层 {ImGui.GetIO().DisplaySize.X:0}x{ImGui.GetIO().DisplaySize.Y:0}，" +
                $"本机色 #{localCol:X8}");
        }
    }

    private static bool _reported;

    /// <summary>世界像素碰撞箱 → 覆盖层矩形描边（左上角 + 尺寸，position 是左上角）。</summary>
    private static void DrawBox(ImDrawListPtr dl, XnaVector2 position, int width, int height, uint col)
    {
        // 世界像素 → 覆盖层物理像素：两次独立换算，避免尺寸被缩放量污染
        var a = WorldEdit.WorldToScreen(position);
        var b = WorldEdit.WorldToScreen(new XnaVector2(position.X + width, position.Y + height));
        dl.AddRect(ImVec(a), ImVec(b), col, 0f, ImDrawFlags.None, BoxThickness);
    }

    /// <summary>世界像素连线（本机玩家中心 → 目标中心）。</summary>
    private static void DrawTracer(ImDrawListPtr dl, XnaVector2 from, XnaVector2 to, uint col)
    {
        var a = WorldEdit.WorldToScreen(from);
        var b = WorldEdit.WorldToScreen(to);
        dl.AddLine(ImVec(a), ImVec(b), col);   // 原版走 AddLine 默认粗细 1.0f
    }

    private static void RowToggle(string id, string label, bool value, Action<bool> set)
    {
        ModernUI.BeginRow();
        ModernUI.RowLabel(label);
        if (ModernUI.Toggle(id, value))
        {
            set(!value);
            CheatFeatureHost.MarkDirty();
        }
        ModernUI.EndRow();
    }
}