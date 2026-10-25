// ImGui 页：召唤羲和 / 移除 / 跳到阶段 / 时间轴快进（调试用，不打 Boss 也能看全招式）。
using System;
using Pylon.Mod;
using Pylon.ModLoader;
using Terraria;
using ImGuiApi = Pylon.ImGui.ImGui;

namespace XiheBoss
{
    public sealed class XiheBossImGuiTab : IModImGuiTab
    {
        public string Title => "羲和 Boss";

        private static string _status = "";

        public void Draw()
        {
            ImGuiApi.SeparatorText("羲和（MEAC.Bosses.EoL.XiHe 复刻）");
            ImGuiApi.Text("血量：普通 100000 / 专家 120000 / 大师 150000；DPS 上限按 14820 tick 分摊。");
            ImGuiApi.Text("全部攻击：EoLProj 射击、日轮激光环、长枪阵列、冲撞/旋冲、交叉光、");
            ImGuiApi.Text("准备（场地收缩 + 双色激光网）、终极技（大光柱 + 全屏弹幕雨）、离场。");

            ImGuiApi.SeparatorText("召唤 / 清除");

            if (ImGuiApi.Button("召唤羲和"))
                Spawn();

            ImGuiApi.SameLine();
            if (ImGuiApi.Button("移除羲和"))
                Remove();

            ImGuiApi.SeparatorText("调试");

            if (ImGuiApi.Button("跳到阶段 2"))
                XiheBossNpc.Current?.DebugSetPhase(2);

            ImGuiApi.SameLine();
            if (ImGuiApi.Button("跳到阶段 3"))
                XiheBossNpc.Current?.DebugSetPhase(3);

            if (ImGuiApi.Button("快进到终极技 (4450)"))
                XiheBossNpc.Current?.DebugSkipTo(4330);

            ImGuiApi.SameLine();
            if (ImGuiApi.Button("快进到阶段 2 终盘 (9120)"))
                XiheBossNpc.Current?.DebugSkipTo(9050);

            if (ImGuiApi.Button("传送到 Boss"))
                Teleport();

            ImGuiApi.SeparatorText("技能测试（直接点）");
            if (ImGuiApi.Button("光蛋环 (EoLProj)")) SpawnOrbRing();
            ImGuiApi.SameLine();
            if (ImGuiApi.Button("长枪 (EoLLance)")) SpawnLance();
            ImGuiApi.SameLine();
            if (ImGuiApi.Button("火舌 (EoLFire)")) SpawnFire();

            if (ImGuiApi.Button("光柱 A (蓄力)")) SpawnBigLaser(0f);
            ImGuiApi.SameLine();
            if (ImGuiApi.Button("光柱 B (跟随)")) SpawnBigLaser(1f);
            ImGuiApi.SameLine();
            if (ImGuiApi.Button("太阳 (SunProj)")) SpawnSun();

            ImGuiApi.Text("需要先「召唤羲和」；光柱 B / 火舌 会跟随 Boss。");

            ImGuiApi.SeparatorText("诊断");
            if (ImGuiApi.Button("生成太阳（测日珥/火舌）"))
                SpawnSun();
            ImGuiApi.SameLine();
            ImGuiApi.Checkbox("每步整屏 box（排查矩形裁剪；会很卡）", ref XiheFx.DebugFullScreenBox);
            ImGuiApi.Text("打开后如果那条矩形消失 → 是 pass 的 Box/屏幕拷贝；仍在 → 是 Boss 本体绘制。");

            var boss = XiheBossNpc.Current;
            if (boss != null && boss.NpcAlive)
            {
                ImGuiApi.SeparatorText("当前状态");
                ImGuiApi.Text($"state = {boss.BossState}   timerM = {boss.TimerM}   phase = {boss.Phase}");
                ImGuiApi.Text($"life = {boss.NPC.life}/{boss.NPC.lifeMax}   场地半径 = {boss.RealAreaRadius:0}");
            }

            if (!string.IsNullOrEmpty(_status)) ImGuiApi.Text(_status);
        }

        private static void Spawn()
        {
            try
            {
                var player = Main.player[Main.myPlayer];
                if (player == null || !player.active)
                {
                    _status = "需要先进世界。";
                    return;
                }

                var type = ModContent.NpcType<XiheBossNpc>();
                if (type <= 0)
                {
                    _status = "NPC 还没注册好（等进世界后再点）。";
                    return;
                }

                var idx = NPC.NewNPC(null, (int)player.Center.X, (int)player.Center.Y - 500, type);
                if (idx >= 0 && idx < Main.npc.Length)
                {
                    Main.npc[idx].target = player.whoAmI;
                    Main.npc[idx].GivenName = "羲和";
                    _status = $"已召唤（NPC #{idx}）。";
                }
                else _status = "召唤失败：NPC.NewNPC 返回 " + idx;
            }
            catch (Exception e)
            {
                _status = "召唤失败：" + e.Message;
                XiheBossMod.Log?.Warn("[tab] 召唤失败：" + e);
            }
        }

        private static void Remove()
        {
            try
            {
                var type = ModContent.NpcType<XiheBossNpc>();
                for (var i = 0; i < Main.npc.Length; i++)
                {
                    var npc = Main.npc[i];
                    if (npc != null && npc.active && npc.type == type) npc.active = false;
                }
                XiheBossNpc.Current = null;
                _status = "已移除。";
            }
            catch (Exception e) { _status = "移除失败：" + e.Message; }
        }

        private static Il2CppMicrosoft.Xna.Framework.Vector2 TargetPos()
        {
            var boss = XiheBossNpc.Current;
            if (boss != null && boss.NpcAlive) return boss.Center;
            var p = Main.player[Main.myPlayer];
            return p.Center - new Il2CppMicrosoft.Xna.Framework.Vector2(0f, 300f);
        }

        private static void SpawnOrbRing()
        {
            try
            {
                var c = TargetPos();
                var type = ModContent.ProjectileType<EoLProj>();
                for (var i = 0; i < 24; i++)
                {
                    var ang = i * Math.PI * 2.0 / 24.0;
                    var dir = new Il2CppMicrosoft.Xna.Framework.Vector2((float)Math.Cos(ang), (float)Math.Sin(ang));
                    Projectile.NewProjectile(null, c, dir * 6f, type, 50, 0f, Main.myPlayer, 0f, 0f, 0f);
                }
                _status = "已释放光蛋环（24 发）。";
            }
            catch (Exception e) { _status = "光蛋环失败：" + e.Message; }
        }

        private static void SpawnLance()
        {
            try
            {
                var p = Main.player[Main.myPlayer];
                var dir = XiheHelper.Normalize2(TargetPos() - p.Center);
                Projectile.NewProjectile(null, p.Center, dir * 14f, ModContent.ProjectileType<EoLLance>(),
                    50, 0f, Main.myPlayer, 0f, 0f, 0f);
                _status = "已发射长枪。";
            }
            catch (Exception e) { _status = "长枪失败：" + e.Message; }
        }

        private static void SpawnFire()
        {
            try
            {
                var boss = XiheBossNpc.Current;
                if (boss == null || !boss.NpcAlive) { _status = "先召唤羲和。"; return; }
                Projectile.NewProjectile(null, boss.Center, new Il2CppMicrosoft.Xna.Framework.Vector2(10f, 0f),
                    ModContent.ProjectileType<EoLFire>(), 50, 0f, Main.myPlayer, boss.NPC.whoAmI, -0.2f, 0f);
                _status = "已释放火舌。";
            }
            catch (Exception e) { _status = "火舌失败：" + e.Message; }
        }

        private static void SpawnBigLaser(float mode)
        {
            try
            {
                var boss = XiheBossNpc.Current;
                var owner = boss != null && boss.NpcAlive ? boss.NPC.whoAmI : 0;
                var m = mode > 0.5f && owner == 0 ? 0f : mode;
                Projectile.NewProjectile(null, TargetPos(), new Il2CppMicrosoft.Xna.Framework.Vector2(0f, -1f),
                    ModContent.ProjectileType<EoLBigLaser>(), 50, 0f, Main.myPlayer, owner, m, 0f);
                _status = m > 0.5f ? "已释放跟随光柱。" : "已释放蓄力光柱。";
            }
            catch (Exception e) { _status = "光柱失败：" + e.Message; }
        }

        private static void SpawnSun()
        {
            try
            {
                var player = Main.player[Main.myPlayer];
                if (player == null || !player.active) { _status = "需要先进世界。"; return; }
                var type = ModContent.ProjectileType<SunProj>();
                if (type <= 0) { _status = "SunProj 还没注册好。"; return; }
                Projectile.NewProjectile(null, player.Center + new Il2CppMicrosoft.Xna.Framework.Vector2(0f, -350f),
                    Il2CppMicrosoft.Xna.Framework.Vector2.Zero, type, 0, 0f, Main.myPlayer, 0f, 0f, 0f);
                _status = "已生成太阳。";
            }
            catch (Exception e) { _status = "生成太阳失败：" + e.Message; }
        }

        private static void Teleport()
        {
            var boss = XiheBossNpc.Current;
            if (boss == null || !boss.NpcAlive) { _status = "场上没有羲和。"; return; }
            var player = Main.player[Main.myPlayer];
            player.Center = boss.Center + new Il2CppMicrosoft.Xna.Framework.Vector2(0f, 400f);
            _status = "已传送到 Boss 下方。";
        }
    }
}



