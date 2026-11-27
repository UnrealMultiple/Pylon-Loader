using System;
using System.Numerics;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Pylon.NaviteAPI.ImGui;
using Pylon.ModLoader.ImGuiHost;
using Terraria;
using CheatMenu.Infra;
using XnaVector2 = Il2CppMicrosoft.Xna.Framework.Vector2;

namespace CheatMenu.Features;

/// <summary>
/// 弹道预测。1:1 对应原版 <c>ProjectilePredictionTool</c>（视觉，移植 TerraAngel）：
/// 直线弹幕画到第一个实体方块；箭矢类逐帧模拟重力（v.y += 0.1）；
/// 弹跳弹幕（流星弹 36 / 脉冲弹 357）走 Collision.TileCollision + SlopeCollision 反弹模拟。
/// 绘制走 ImGui 背景绘制列表（<see cref="ModOverlay"/>，等价原版
/// <c>ImGui::GetBackgroundDrawList()</c>），**不再**用游戏 SpriteBatch 界面层。
/// 坐标换算：模拟点始终是世界像素，落笔时经 <see cref="WorldEdit.WorldToScreen"/>
/// （XNAUnityRunner.AdjustWorldForRealScreen）换算成覆盖层物理像素。
/// </summary>
public sealed class ProjectilePredictionFeature : CheatFeature
{
    private const string OverlayKey = "CheatMenu/prediction";

    /// <summary>预测线粗细（覆盖层物理像素，与原版 AddLine 的 1.2f 一致）。</summary>
    private const float LineThickness = 1.2f;

    public override string Id => "visual.prediction";
    public override string Title => "弹道预测";
    public override CheatCategory Category => CheatCategory.Visual;
    public override string SubPage => "主要";

    public override void Install() => RegisterOverlay(OverlayKey, DrawPrediction);

    public override void DrawMenu()
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is null) return;

        ModernUI.BeginCard("弹道预测", "PREDICTION");

        RowToggle("##predenabled", "启用弹幕预测", cfg.PredEnabled, v => cfg.PredEnabled = v);
        if (cfg.PredEnabled)
        {
            RowToggle("##predfriendly", "绘制友方弹幕", cfg.PredDrawFriendly, v => cfg.PredDrawFriendly = v);
            RowToggle("##predhostile", "绘制敌方弹幕", cfg.PredDrawHostile, v => cfg.PredDrawHostile = v);

            var steps = (float)cfg.PredMaxStepCount;
            if (ModernUI.RowSlider("##predsteps", "最大预测步数", "100-5000", ref steps, 100f, 5000f, "0"))
            {
                cfg.PredMaxStepCount = (int)Math.Clamp(steps, 100f, 5000f);
                CheatFeatureHost.MarkDirty();
            }
        }

        ModernUI.EndCard();
    }

    // ==================== 常驻绘制（ImGui 背景层） ====================

    /// <summary>
    /// 绘制体：每帧 ImGui 帧内调用。热路径约定——开关关闭时立即返回；
    /// 全程不分配（无委托/闭包、无容器、无字符串）。
    /// </summary>
    private static void DrawPrediction(ImDrawListPtr dl)
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is not { PredEnabled: true }) return;        // 开关关闭：第一行就返回
        if (Main.gameMenu || Main.mapFullscreen) return;      // 主菜单 / 全屏地图：不绘制

        var tileSolid = Main.tileSolid;
        var tileSolidTop = Main.tileSolidTop;
        if (tileSolid is null || tileSolidTop is null) return;
        var maxTX = Main.maxTilesX;
        var maxTY = Main.maxTilesY;

        var friendlyCol = ToImU32(cfg.PredFriendlyColor);
        var hostileCol = ToImU32(cfg.PredHostileColor);
        var maxSteps = cfg.PredMaxStepCount;

        var projectiles = Main.projectile;
        if (projectiles is null) return;

        foreach (var pr in projectiles)
        {
            if (pr is not { active: true }) continue;

            var hostile = pr.hostile;
            var friendly = pr.friendly;
            if (hostile && !cfg.PredDrawHostile) continue;
            if (friendly && !cfg.PredDrawFriendly) continue;

            var col = hostile ? hostileCol : friendlyCol;
            var pos = pr.position;
            var vel = pr.velocity;
            var w = pr.width;
            var h = pr.height;
            var type = pr.type;
            var timeLeft = pr.timeLeft;
            var center = new XnaVector2(pos.X + w * 0.5f, pos.Y + h * 0.5f);

            if (pr.arrow)
            {
                var steps = Math.Min(timeLeft, maxSteps);
                if (steps > 0)
                    ArrowPrediction(dl, center, vel, steps, col, tileSolid, tileSolidTop, maxTX, maxTY);
            }
            else if (type == 36 || type == 357)   // MeteorShot / PulseBolt
            {
                var steps = Math.Min(timeLeft, maxSteps);
                if (steps > 0)
                    BouncingPrediction(dl, pos, vel, w, h, pr.penetrate, steps, col, maxTX, maxTY);
            }
            else
            {
                StraightPrediction(dl, center, vel, timeLeft, col, tileSolid, tileSolidTop, maxTX, maxTY);
            }
        }
    }

    /// <summary>实体方块判定：范围内 + 图格 active + tileSolid 且非 tileSolidTop（与原版同义）。</summary>
    private static bool IsSolid(int tx, int ty, Il2CppStructArray<bool> tileSolid,
        Il2CppStructArray<bool> tileSolidTop, int maxTX, int maxTY)
    {
        if (tx < 0 || ty < 0 || tx >= maxTX || ty >= maxTY) return false;
        if (!Game.TileActive(tx, ty)) return false;
        var t = Game.TileType(tx, ty);
        if (t < 0 || t >= tileSolid.Length || t >= tileSolidTop.Length) return false;
        return tileSolid[t] && !tileSolidTop[t];
    }

    /// <summary>世界像素 → 覆盖层物理像素。</summary>
    private static Vector2 ToScreen(XnaVector2 world) => ImVec(WorldEdit.WorldToScreen(world));

    // ---- 直线弹幕 ----
    private static void StraightPrediction(ImDrawListPtr dl, XnaVector2 center, XnaVector2 vel,
        int timeLeft, uint col, Il2CppStructArray<bool> tileSolid, Il2CppStructArray<bool> tileSolidTop,
        int maxTX, int maxTY)
    {
        var speed = vel.Length();
        if (speed < 0.5f) return;
        var maxDist = speed * timeLeft;
        if (maxDist > 5000f) maxDist = 5000f;
        var dir = vel / speed;

        var hitDist = maxDist;
        const float step = 8f;
        for (var d = step; d < maxDist; d += step)
        {
            var p = center + dir * d;
            if (p.X < 0f || p.Y < 0f || p.X >= maxTX * 16f || p.Y >= maxTY * 16f)
            {
                hitDist = d;
                break;
            }
            if (IsSolid((int)(p.X / 16f), (int)(p.Y / 16f), tileSolid, tileSolidTop, maxTX, maxTY))
            {
                hitDist = d;
                break;
            }
        }

        dl.AddLine(ToScreen(center), ToScreen(center + dir * hitDist), col, LineThickness);
    }

    // ---- 箭矢类（重力逐帧模拟） ----
    private static void ArrowPrediction(ImDrawListPtr dl, XnaVector2 pos, XnaVector2 vel,
        int maxSteps, uint col, Il2CppStructArray<bool> tileSolid, Il2CppStructArray<bool> tileSolidTop,
        int maxTX, int maxTY)
    {
        for (var i = 0; i < maxSteps; i++)
        {
            var last = pos;
            vel.Y += 0.1f;   // 重力
            pos += vel;

            dl.AddLine(ToScreen(last), ToScreen(pos), col, LineThickness);

            if (pos.X < 0f || pos.Y < 0f || pos.X >= maxTX * 16f || pos.Y >= maxTY * 16f)
                break;
            if (IsSolid((int)(pos.X / 16f), (int)(pos.Y / 16f), tileSolid, tileSolidTop, maxTX, maxTY))
                break;
        }
    }

    // ---- 弹跳弹幕（TileCollision + SlopeCollision 反弹） ----
    private static void BouncingPrediction(ImDrawListPtr dl, XnaVector2 position, XnaVector2 velocity,
        int width, int height, int penetrate, int maxSteps, uint col, int maxTX, int maxTY)
    {
        var startPosition = position;
        var flag = false;
        var half = new XnaVector2(width * 0.5f, height * 0.5f);

        for (var i = 0; i < maxSteps; i++)
        {
            // 方块碰撞
            var tileVelocity = Collision.TileCollision(position, velocity, width, height,
                true, true, 1, false, false, true);
            if (tileVelocity != velocity)
            {
                var f = false;
                if (tileVelocity.Y != velocity.Y) { f = true; velocity.Y = -velocity.Y; }
                else if (tileVelocity.X != velocity.X) { f = true; velocity.X = -velocity.X; }
                if (f)
                {
                    DrawSegment(dl, startPosition + half, position + half, col);
                    startPosition = position;
                    penetrate--;
                    if (penetrate <= 0) flag = true;
                }
            }

            // 斜坡碰撞
            var slopeVelocity = Collision.SlopeCollision(position, velocity, width, height, 0f, true, false);
            if (position.X != slopeVelocity.X || position.Y != slopeVelocity.Y ||
                velocity.X != slopeVelocity.Z || velocity.Y != slopeVelocity.W)
            {
                var f = false;
                if (slopeVelocity.W != velocity.Y) { f = true; velocity.Y = -velocity.Y; }
                else if (slopeVelocity.Z != velocity.X) { f = true; velocity.X = -velocity.X; }
                if (f)
                {
                    DrawSegment(dl, startPosition + half, position + half, col);
                    startPosition = position;
                    penetrate--;
                    if (penetrate <= 0) flag = true;
                }
            }

            velocity.X = slopeVelocity.Z;
            velocity.Y = slopeVelocity.W;
            position += velocity;

            if (position.X < 0f || position.Y < 0f || position.X >= maxTX * 16f || position.Y >= maxTY * 16f)
                flag = true;

            if (flag) break;
        }

        if (!flag) DrawSegment(dl, startPosition + half, position + half, col);
    }

    /// <summary>弹跳弹幕线段：模拟点在弹幕左上角，画的时候补半宽高到中心。</summary>
    private static void DrawSegment(ImDrawListPtr dl, XnaVector2 from, XnaVector2 to, uint col)
    {
        dl.AddLine(ToScreen(from), ToScreen(to), col, LineThickness);
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