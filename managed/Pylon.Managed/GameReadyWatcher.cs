using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Terraria;
using Terraria.GameContent;

namespace Pylon.Managed
{
    internal static class GameReadyWatcher
    {
        /// <summary>物品类型表建好后应有的长度（1.4.5.8.6 原版数量）。</summary>
        private const int VanillaItemCount = 6196;

        /// <summary>兜底：探测一直不成立时的最长等待（帧）。</summary>
        private const int ArmFrames = 120;   // 约 2 秒：够游戏跑起来，又不拖太久

        private static Harmony _harmony;
        private static bool _fired;
        private static int _frames;

        /// <summary>武装探测（幂等）。</summary>
        public static void Arm()
        {
            if (_harmony != null) return;

            var update = FindUpdate();
            if (update == null)
            {
                Entrypoint.Log("找不到 Terraria.Main::Update(GameTime)，延迟阶段无法触发（内容注入会缺席）");
                return;
            }

            try
            {
                _harmony = PylonHarmony.NewHarmony("pylon.host.game-ready");
                _harmony.Patch(update, postfix: new HarmonyMethod(
                    typeof(GameReadyWatcher).GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic)));
                Entrypoint.Log($"已武装内容就绪探测：每帧检查 {update.DeclaringType?.Name}::{update.Name}");
            }
            catch (Exception e)
            {
                Entrypoint.Log("武装内容就绪探测失败：" + PylonHarmony.Flatten(e));
            }
        }

        private static void Postfix()
        {
            if (_fired) return;
            _frames++;

            // 两条判据先到先触发：① 游戏类型表已建好（内容注入需要的时机）；② 已跑够帧数（晚阶段只要游戏在跑）。
            var tablesReady = GameLooksReady();
            if (!tablesReady && _frames < ArmFrames) return;

            _fired = true;
            Entrypoint.Log($"进入晚阶段（第 {_frames} 帧，类型表{(tablesReady ? "已" : "未")}就绪）→ 触发延迟注册");
            ModLoader.RunContentReadyOnGameThread();
        }

        /// <summary>就绪判据：游戏自己的物品类型表已建好（长度达到原版数量）。</summary>
        private static bool GameLooksReady()
        {
            try
            {
                var array = TextureAssets.Item;
                if (array == null)
                {
                    DiagnoseOnce("TextureAssets.Item 还是 null（内容表尚未建立）");
                    return false;
                }
                if (array.Length < VanillaItemCount)
                {
                    DiagnoseOnce($"TextureAssets.Item 长度 {array.Length} < {VanillaItemCount}");
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                DiagnoseOnce("就绪探测异常：" + e.GetType().Name + ": " + e.Message);
                return false;
            }
        }

        private static bool _diagnosed;

        private static void DiagnoseOnce(string message)
        {
            if (_diagnosed) return;
            _diagnosed = true;
            Entrypoint.Log("就绪探测：首次未通过 -> " + message);
        }

        /// <summary>找 Main.Update（恰好一个 GameTime 参数的那个重载）。</summary>
        private static MethodInfo FindUpdate()
        {
            foreach (var candidate in typeof(Main).GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                              BindingFlags.Static | BindingFlags.Instance))
            {
                if (candidate.Name != "Update") continue;
                if (candidate.GetParameters().Length == 1) return candidate;
            }
            return null;
        }

        /// <summary>在已加载程序集里按全名找类型（游戏类型由 TPA 提供，此时必然已加载）。</summary>
        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName, throwOnError: false);
                if (type != null) return type;
            }
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .FirstOrDefault(t => t.FullName == fullName);
        }
    }
}
