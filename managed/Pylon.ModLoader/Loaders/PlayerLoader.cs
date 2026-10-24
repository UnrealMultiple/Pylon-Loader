using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Terraria;
using Terraria.DataStructures;
using Terraria.IO;

namespace Pylon.ModLoader
{
    /// <summary>每玩家一份 ModPlayer 实例（按覆写分派生命周期钩子）；侧车存档 &lt;角色&gt;.plr.bl.json，键 "&lt;mod&gt;/&lt;类名&gt;"，没加载的 Mod 的键原样保留。</summary>
    internal static class PlayerLoader
    {
        private static readonly List<ModPlayer> Templates = new();
        private static readonly Dictionary<Type, OverridePlan> Plans = new();
        private static readonly Dictionary<string, List<ModPlayer>> ByMethod = new(StringComparer.Ordinal);

        private const int MaxPlayers = 256;
        private static readonly IntPtr[] OwnerPointers = new IntPtr[MaxPlayers];
        private static readonly ModPlayer[][] Instances = new ModPlayer[MaxPlayers][];

        private static bool _installed;
        private static bool _loadingSave;
        private static bool _vetoLuck;
        private static float _luckBefore;

        internal static void Register(ModPlayer template)
        {
            if (template == null || Templates.Contains(template)) return;
            Templates.Add(template);
            BuildMethodTable();
        }

        private static OverridePlan PlanOf(ModPlayer player)
        {
            var type = player.GetType();
            if (Plans.TryGetValue(type, out var plan)) return plan;
            plan = OverridePlan.Build(type, typeof(ModPlayer));
            Plans[type] = plan;
            return plan;
        }

        private static void BuildMethodTable()
        {
            ByMethod.Clear();
            foreach (var template in Templates)
            {
                foreach (var name in PlanOf(template).Names)
                {
                    if (!ByMethod.TryGetValue(name, out var list)) ByMethod[name] = list = new List<ModPlayer>();
                    if (!list.Contains(template)) list.Add(template);
                }
            }
        }

        private static List<ModPlayer> For(string method) =>
            ByMethod.TryGetValue(method, out var list) ? list : null;

        private static bool Has(string method) => ByMethod.ContainsKey(method);

        // ===================== 实例 =====================

        private static ModPlayer[] Of(Player player)
        {
            if (player == null) return Array.Empty<ModPlayer>();
            var index = player.whoAmI;
            if (index < 0 || index >= MaxPlayers) return Array.Empty<ModPlayer>();

            var pointer = player.Pointer;
            if (Instances[index] != null && OwnerPointers[index] == pointer &&
                Instances[index].Length == Templates.Count)
            {
                return Instances[index];
            }

            var fresh = new ModPlayer[Templates.Count];
            for (var i = 0; i < Templates.Count; i++)
            {
                var template = Templates[i];
                var instance = (ModPlayer)Activator.CreateInstance(template.GetType());
                instance.Mod = template.Mod;
                instance.Player = player;
                fresh[i] = instance;
            }

            OwnerPointers[index] = pointer;
            Instances[index] = fresh;

            foreach (var instance in fresh)
                Safe.Run(instance.GetType().Name + ".Initialize", instance.Initialize);
            return fresh;
        }

        internal static T Get<T>(Player player) where T : ModPlayer
        {
            var all = Of(player);
            foreach (var instance in all)
                if (instance is T typed) return typed;
            return null;
        }

        internal static void Clear(int index)
        {
            if (index < 0 || index >= MaxPlayers) return;
            Instances[index] = null;
            OwnerPointers[index] = IntPtr.Zero;
        }

        private static void Each(Player player, string method, Action<ModPlayer> call)
        {
            if (player == null) return;
            var templates = For(method);
            if (templates == null) return;
            var all = Of(player);
            for (var i = 0; i < Templates.Count; i++)
            {
                var template = Templates[i];
                if (!Plans.TryGetValue(template.GetType(), out var plan) || !plan.Has(method)) continue;
                try { call(all[i]); }
                catch (Exception e) { Safe.Report(template.GetType().Name + "." + method, e); }
            }
        }

        private static bool Any(Player player, string method, Func<ModPlayer, bool> call)
        {
            var templates = For(method);
            if (templates == null) return false;
            var all = Of(player);
            var result = false;
            for (var i = 0; i < Templates.Count; i++)
            {
                var template = Templates[i];
                if (!Plans.TryGetValue(template.GetType(), out var plan) || !plan.Has(method)) continue;
                try { if (!call(all[i])) result = true; }
                catch (Exception e) { Safe.Report(template.GetType().Name + "." + method, e); }
            }
            return result;
        }

        // ===================== 注册期：装 hook =====================

        internal static void Install(Harmony harmony, ModLogger log)
        {
            if (_installed || Templates.Count == 0) return;
            _installed = true;

            void Patch(Type type, string name, Type[] args, string prefix = null, string postfix = null) =>
                GameHooks.Patch(harmony, log, typeof(PlayerLoader), type, name, args, prefix, postfix);

            if (Has("ResetEffects") || Has("ModifyMaxStats"))
                Patch(typeof(Player), "ResetEffects", Type.EmptyTypes, postfix: nameof(ResetEffectsPostfix));
            if (Has("PreUpdate") || Has("PostUpdate"))
                Patch(typeof(Player), "Update", new[] { typeof(int) }, nameof(UpdatePrefix), nameof(UpdatePostfix));
            if (Has("PreUpdateBuffs") || Has("PostUpdateBuffs"))
                Patch(typeof(Player), "UpdateBuffs", new[] { typeof(int) }, nameof(UpdateBuffsPrefix), nameof(UpdateBuffsPostfix));
            if (Has("UpdateEquips") || Has("PostUpdateEquips"))
                Patch(typeof(Player), "UpdateEquips", new[] { typeof(int) }, postfix: nameof(UpdateEquipsPostfix));
            if (Has("UpdateBadLifeRegen") || Has("UpdateLifeRegen"))
                Patch(typeof(Player), "UpdateLifeRegen", Type.EmptyTypes, nameof(LifeRegenPrefix), nameof(LifeRegenPostfix));
            if (Has("UpdateManaRegen"))
                Patch(typeof(Player), "UpdateManaRegen", Type.EmptyTypes, postfix: nameof(ManaRegenPostfix));
            if (Has("UpdateDead"))
                Patch(typeof(Player), "UpdateDead", Type.EmptyTypes, postfix: nameof(UpdateDeadPostfix));
            if (Has("UpdateMovement"))
                Patch(typeof(Player), "BordersMovement", Type.EmptyTypes, nameof(MovementPrefix));
            if (Has("OnEnterWorld"))
                Patch(typeof(Player), "EnterWorld", new[] { typeof(int) }, postfix: nameof(EnterWorldPostfix));
            if (Has("OnRespawn"))
                Patch(typeof(Player), "Spawn", new[] { typeof(PlayerSpawnContext) }, postfix: nameof(SpawnPostfix));
            if (Has("CanUseItem"))
                Patch(typeof(Player), "ItemCheck_CheckCanUse_Inner", new[] { typeof(Item), typeof(bool) }, nameof(CanUsePrefix));
            if (Has("PreKill") || Has("Kill"))
                Patch(typeof(Player), "KillMe",
                    new[] { typeof(PlayerDeathReason), typeof(double), typeof(int), typeof(bool) },
                    nameof(KillPrefix), nameof(KillPostfix));
            if (Has("PreModifyLuck") || Has("ModifyLuck"))
                Patch(typeof(Player), "RecalculateLuck", Type.EmptyTypes, nameof(LuckPrefix), nameof(LuckPostfix));
            if (Has("SaveData") || Has("LoadData") || Has("PreSavePlayer") || Has("PostSavePlayer") ||
                Has("PreSaveCustomData"))
            {
                Patch(typeof(Player), "InternalSavePlayerFile", new[] { typeof(PlayerFileData) },
                    nameof(SavePrefix), nameof(SavePostfix));
                Patch(typeof(Player), "LoadPlayer", new[] { typeof(string), typeof(bool) },
                    postfix: nameof(LoadPostfix));
            }
        }

        // ===================== 生命周期 =====================

        private static void ResetEffectsPostfix(Player __instance)
        {
            Each(__instance, "ResetEffects", m => m.ResetEffects(__instance));

            var life = 0;
            var mana = 0;
            Each(__instance, "ModifyMaxStats", m =>
            {
                m.ModifyMaxStats(__instance);
                life += m.CumulativeHealth;
                mana += m.CumulativeMana;
            });
            if (life != 0) __instance.statLifeMax2 = Math.Max(1, __instance.statLifeMax2 + life);
            if (mana != 0) __instance.statManaMax2 = Math.Max(0, __instance.statManaMax2 + mana);
        }

        private static void UpdatePrefix(Player __instance) => Each(__instance, "PreUpdate", m => m.PreUpdate(__instance));
        private static void UpdatePostfix(Player __instance) => Each(__instance, "PostUpdate", m => m.PostUpdate(__instance));

        private static void UpdateBuffsPrefix(Player __instance) =>
            Each(__instance, "PreUpdateBuffs", m => m.PreUpdateBuffs(__instance));

        private static void UpdateBuffsPostfix(Player __instance) =>
            Each(__instance, "PostUpdateBuffs", m => m.PostUpdateBuffs(__instance));

        private static void UpdateEquipsPostfix(Player __instance)
        {
            Each(__instance, "UpdateEquips", m => m.UpdateEquips(__instance));
            Each(__instance, "PostUpdateEquips", m => m.PostUpdateEquips(__instance));
        }

        private static void LifeRegenPrefix(Player __instance) =>
            Each(__instance, "UpdateBadLifeRegen", m => m.UpdateBadLifeRegen(__instance));

        private static void LifeRegenPostfix(Player __instance) =>
            Each(__instance, "UpdateLifeRegen", m => m.UpdateLifeRegen(__instance));

        private static void ManaRegenPostfix(Player __instance) =>
            Each(__instance, "UpdateManaRegen", m => m.UpdateManaRegen(__instance));

        private static void UpdateDeadPostfix(Player __instance) =>
            Each(__instance, "UpdateDead", m => m.UpdateDead(__instance));

        private static void MovementPrefix(Player __instance) =>
            Each(__instance, "UpdateMovement", m => m.UpdateMovement(__instance));

        private static void EnterWorldPostfix(int playerIndex)
        {
            if (playerIndex < 0 || playerIndex >= MaxPlayers) return;
            var player = Main.player[playerIndex];
            if (player == null) return;
            Clear(playerIndex);
            Each(player, "OnEnterWorld", m => m.OnEnterWorld(player));
        }

        private static void SpawnPostfix(Player __instance, PlayerSpawnContext context)
        {
            if (context != PlayerSpawnContext.ReviveFromDeath) return;
            Each(__instance, "OnRespawn", m => m.OnRespawn(__instance));
        }

        private static bool CanUsePrefix(Player __instance, Item sItem, ref bool __result)
        {
            if (!Any(__instance, "CanUseItem", m => m.CanUseItem(__instance, sItem))) return true;
            __result = false;
            return false;
        }

        private static PlayerDeathReason _killReason;
        private static double _killDamage;
        private static int _killDir;
        private static bool _killPvp;
        private static bool _wasAlive;

        private static bool KillPrefix(Player __instance, PlayerDeathReason damageSource, double dmg, int hitDirection,
                                       bool pvp)
        {
            _killReason = damageSource;
            _killDamage = dmg;
            _killDir = hitDirection;
            _killPvp = pvp;
            _wasAlive = !__instance.dead && !__instance.creativeGodMode;
            if (!_wasAlive) return true;
            return !Any(__instance, "PreKill", m => m.PreKill(__instance, damageSource, dmg, hitDirection, pvp));
        }

        private static void KillPostfix(Player __instance, PlayerDeathReason damageSource, double dmg, int hitDirection,
                                        bool pvp)
        {
            if (!_wasAlive || !__instance.dead) return;
            Each(__instance, "Kill", m => m.Kill(__instance, damageSource, dmg, hitDirection, pvp));
        }

        private static bool LuckPrefix(Player __instance)
        {
            _luckBefore = __instance.luck;
            _vetoLuck = Any(__instance, "PreModifyLuck", m => m.PreModifyLuck(__instance, ref _luckBefore));

            if (!_vetoLuck) return true;
            var luck = _luckBefore;
            Each(__instance, "ModifyLuck", m => m.ModifyLuck(__instance, ref luck));
            __instance.luck = luck;
            return false;
        }

        private static void LuckPostfix(Player __instance)
        {
            if (_vetoLuck) return;
            var luck = __instance.luck;
            Each(__instance, "ModifyLuck", m => m.ModifyLuck(__instance, ref luck));
            __instance.luck = luck;
        }

        // ===================== 侧车存档 =====================

        private static string DataFile(PlayerFileData fileData)
        {
            if (fileData == null || fileData.IsCloudSave) return null;
            var path = fileData.Path;
            return string.IsNullOrEmpty(path) ? null : path + ".plr.bl.json";
        }

        private static void SavePrefix(PlayerFileData playerFile) =>
            Each(playerFile?.Player, "PreSavePlayer", m => m.PreSavePlayer(playerFile.Player));

        private static void SavePostfix(PlayerFileData playerFile)
        {
            var player = playerFile?.Player;
            var file = DataFile(playerFile);
            if (player == null || file == null) return;

            try
            {
                Each(player, "PreSaveCustomData", m => m.PreSaveCustomData(player));
                var all = TagJson.Read(file);
                foreach (var template in Templates)
                {
                    if (!Plans.TryGetValue(template.GetType(), out var plan) || !plan.Has("SaveData")) continue;
                    var instance = Of(player)[Templates.IndexOf(template)];
                    var tag = new TagCompound();
                    try { instance.SaveData(tag); }
                    catch (Exception e) { Safe.Report(template.GetType().Name + ".SaveData", e); }

                    var key = KeyOf(template);
                    if (tag.Count > 0) all[key] = tag;
                    else all.Remove(key);
                }

                if (all.Count > 0) TagJson.Write(file, all);
                else if (File.Exists(file)) File.Delete(file);
            }
            catch (Exception e)
            {
                Safe.Report("ModPlayer 存档", e);
            }
            finally
            {
                Each(player, "PostSavePlayer", m => m.PostSavePlayer(player));
            }
        }

        private static void LoadPostfix(string playerPath, bool cloudSave, ref PlayerFileData __result)
        {
            var player = __result?.Player;
            var file = DataFile(__result);
            if (player == null || file == null || !File.Exists(file)) return;

            _loadingSave = true;
            try
            {
                var all = TagJson.Read(file);
                var instances = Of(player);
                for (var i = 0; i < Templates.Count; i++)
                {
                    var template = Templates[i];
                    if (!Plans.TryGetValue(template.GetType(), out var plan) || !plan.Has("LoadData")) continue;
                    if (!all.TryGetValue(KeyOf(template), out var value)) continue;
                    var tag = TagCompound.From(value);
                    try { instances[i].LoadData(tag); }
                    catch (Exception e) { Safe.Report(template.GetType().Name + ".LoadData", e); }
                }
            }
            catch (Exception e)
            {
                Safe.Report("ModPlayer 读档", e);
            }
            finally
            {
                _loadingSave = false;
            }
        }

        private static string KeyOf(ModPlayer template) =>
            (template.Mod?.Name ?? "Mod") + "/" + template.GetType().Name;

        internal static bool Loading => _loadingSave;
    }
}
