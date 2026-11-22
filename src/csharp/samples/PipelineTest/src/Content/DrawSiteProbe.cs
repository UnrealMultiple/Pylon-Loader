using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Pylon.ModLoader;
using Pylon.ModLoader.Content;
using Terraria;
using Terraria.GameContent.Drawing;

namespace PipelineTest.Content;

/// <summary>
/// 绘制站点探针（验证脚手架）：统计原生绘制法的真实调用次数与参数形状，
/// 并反射读注册实例的真实 plan 位集——区分「钩子没触发」「被 bail 条件挡住」「plan 位没映射上」
/// （TILE-DRAW30 / PROJ-W2 全局绘制钩 0 次定位）。挂载点：PipelineTestMod.Load。计数只增不清，读数走 [PT] Info。
/// </summary>
public static class DrawSiteProbe
{
    public static int DrawTiles, DrawTilesSolid, DrawTilesIntoTargets, DrawTilesLive;
    public static int PostDrawTiles, PostDrawSolid, PostDrawIntoTargets, PostDrawGameMenu, PostDrawLive;
    public static int DrawProjectiles, DrawProjectilesWorld;
    public static int DrawProjDirect, DrawProjDirectWorld;
    public static int DrawProjDirectA, DrawProjDirectB;
    public static int GetAlphaCalls;

    public static void Install()
    {
        On.Terraria.Main.DrawTiles += (orig, self, solidLayer, forRenderTargets, intoRenderTargets, waterStyleOverride) =>
        {
            orig(self, solidLayer, forRenderTargets, intoRenderTargets, waterStyleOverride);
            DrawTiles++;
            if (solidLayer) DrawTilesSolid++;
            if (intoRenderTargets) DrawTilesIntoTargets++;
            if (!solidLayer && !intoRenderTargets && !Main.gameMenu) DrawTilesLive++;
        };

        On.Terraria.GameContent.Drawing.TileDrawing.PostDrawTiles += (orig, self, solidLayer, forRenderTargets, intoRenderTargets) =>
        {
            orig(self, solidLayer, forRenderTargets, intoRenderTargets);
            PostDrawTiles++;
            if (solidLayer) PostDrawSolid++;
            if (intoRenderTargets) PostDrawIntoTargets++;
            if (Main.gameMenu) PostDrawGameMenu++;
            if (!solidLayer && !intoRenderTargets && !Main.gameMenu) PostDrawLive++;
        };

        On.Terraria.Main.DrawProjectiles += (orig, self) =>
        {
            orig(self);
            DrawProjectiles++;
            if (!Main.gameMenu) DrawProjectilesWorld++;
        };

        On.Terraria.Main.DrawProjDirect += (orig, self, proj, overridePlayer) =>
        {
            DrawProjDirectA++;
            orig(self, proj, overridePlayer);
            DrawProjDirect++;
            if (!Main.gameMenu) DrawProjDirectWorld++;
        };

        On.Terraria.Main.DrawProjDirect += (orig, self, proj, overridePlayer) =>
        {
            DrawProjDirectB++;
            orig(self, proj, overridePlayer);
        };

        On.Terraria.Projectile.GetAlpha += (orig, self, newColor) =>
        {
            GetAlphaCalls++;
            return orig(self, newColor);
        };

        InstallBuffRemovalProbe();
    }

    public static string Report() =>
        $"[probe] 绘制站点：DrawTiles={DrawTiles}(solid={DrawTilesSolid} into={DrawTilesIntoTargets} live={DrawTilesLive})" +
        $" PostDrawTiles={PostDrawTiles}(solid={PostDrawSolid} into={PostDrawIntoTargets} menu={PostDrawGameMenu} live={PostDrawLive})" +
        $" DrawProjectiles={DrawProjectiles}(world={DrawProjectilesWorld}) DrawProjDirect={DrawProjDirect}(world={DrawProjDirectWorld})" +
        $" 双订阅A/B={DrawProjDirectA}/{DrawProjDirectB} GetAlpha={GetAlphaCalls}";

    public static readonly List<string> BuffRemovals = [];
    private static int _vanillaRemovals;

    public static void InstallBuffRemovalProbe()
    {
        On.Terraria.Player.DelBuff += (orig, self, b) =>
        {
            var t = self.buffType[b];
            if (t >= 401) BuffRemovals.Add($"DelBuff[{b}] type={t}(mod) time={self.buffTime[b]}");
            else if (BuffRemovals.Count < 400) { BuffRemovals.Add($"DelBuff[{b}] type={t}"); _vanillaRemovals++; }
            orig(self, b);
        };

        On.Terraria.Player.ClearBuff += (orig, self, type) =>
        {
            if (type >= 401) BuffRemovals.Add($"ClearBuff type={type}(mod)");
            else if (BuffRemovals.Count < 400) { BuffRemovals.Add($"ClearBuff type={type}"); _vanillaRemovals++; }
            orig(self, type);
        };

        On.Terraria.Mount.Dismount += (orig, self, mountedPlayer, ignoreEffect) =>
        {
            if (BuffRemovals.Count < 400)
                BuffRemovals.Add($"Mount.Dismount active={self.Active} type={self.Type}");
            orig(self, mountedPlayer, ignoreEffect);
        };
    }

    public static string BuffRemovalReport()
    {
        var mod = BuffRemovals.Where(r => r.Contains("(mod)")).TakeLast(12);
        return $"原版摘除计 {_vanillaRemovals}；mod 摘除：{(mod.Any() ? string.Join(" | ", mod) : "（无）")}";
    }

    private static readonly string[] HookNames =
    [
        "PreDraw", "PostDraw", "GetAlpha", "PreDrawExtras", "PreAI", "AI", "PostAI", "OnSpawn", "PostDrawInWorld",
    ];

    public static string RegistryReport()
    {
        try
        {
            var asm = typeof(Pylon.ModLoader.Content.ModType).Assembly;
            var regType = asm.GetType("Pylon.ModLoader.Hooks.GlobalProjectileRegistry");
            var registry = regType?.GetField("Registry", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            if (registry is null) return "[probe] registry 反射失败：实例没拿到";

            var rt = registry.GetType();
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var templates = (System.Collections.IEnumerable)rt.GetField("_templates", flags)!.GetValue(registry)!;
            var count = 0;
            foreach (var _ in templates) count++;
            var needsPer = rt.GetField("_needsPerEntity", flags)!.GetValue(registry);

            Projectile? live = null;
            for (var i = 0; i < Main.projectile.Length; i++)
            {
                var p = Main.projectile[i];
                if (p is { active: true }) { live = p; break; }
            }

            var forResult = "无活弹幕";
            if (live is not null)
            {
                var hookIdType = asm.GetType("Pylon.ModLoader.Pipeline.HookId");
                var forMethod = rt.GetMethod("For", flags | BindingFlags.Public);
                var id = Enum.Parse(hookIdType!, "PreDraw");
                var list = (System.Collections.IEnumerable)forMethod!.Invoke(registry, [live, id])!;
                var n = 0;
                foreach (var _ in list) n++;
                forResult = $"For(弹幕type={live.type}, PreDraw).Count={n}";
            }

            return $"[probe] 弹幕注册表：_templates={count} needsPerEntity={needsPer} {forResult}";
        }
        catch (Exception e)
        {
            return "[probe] registry 反射异常：" + e.GetType().Name + " " + e.Message;
        }
    }

    public static string PlanReport()
    {
        try
        {
            var modType = typeof(Pylon.ModLoader.Content.ModType);
            var planProp = modType.GetProperty("Plan", BindingFlags.NonPublic | BindingFlags.Instance);
            var asm = modType.Assembly;
            var hookIdType = asm.GetType("Pylon.ModLoader.Pipeline.HookId");
            var maskType = asm.GetType("Pylon.ModLoader.Pipeline.HookMask");
            var hasMethod = maskType?.GetMethod("Has", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (planProp is null || hookIdType is null || hasMethod is null)
                return "[probe] plan 反射失败：Plan/HookId/HookMask 没找齐";

            var parts = new List<string>();
            foreach (var type in new ModType?[]
                     {
                         ModContent.GetInstance<TestProjectileGlobal>(),
                         ModContent.GetInstance<TestTileDrawProbe>(),
                         ModContent.GetInstance<TestItem>(),
                     })
            {
                if (type is null) { parts.Add("实例缺"); continue; }
                var mask = planProp.GetValue(type);
                var bits = HookNames.Select(n =>
                {
                    var id = Enum.Parse(hookIdType, n);
                    return $"{n}={(bool)hasMethod.Invoke(mask, [id])}";
                });
                parts.Add(type.GetType().Name + "[" + string.Join(" ", bits) + "]");
            }

            return "[probe] plan 位：" + string.Join(" | ", parts);
        }
        catch (Exception e)
        {
            return "[probe] plan 反射异常：" + e.GetType().Name + " " + e.Message;
        }
    }
}
