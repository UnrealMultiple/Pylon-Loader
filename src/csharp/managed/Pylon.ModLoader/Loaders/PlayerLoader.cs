using System;
using System.Collections.Generic;
using System.IO;
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

        internal static void Install(ModLogger log)
        {
            if (_installed || Templates.Count == 0) return;
            _installed = true;

            if (Has("ResetEffects") || Has("ModifyMaxStats"))
                On.Terraria.Player.ResetEffects += ResetEffectsPostfix;
            if (Has("PreUpdate") || Has("PostUpdate"))
                On.Terraria.Player.Update += Update;
            if (Has("PreUpdateBuffs") || Has("PostUpdateBuffs"))
                On.Terraria.Player.UpdateBuffs += UpdateBuffs;
            if (Has("UpdateEquips") || Has("PostUpdateEquips"))
                On.Terraria.Player.UpdateEquips += UpdateEquipsPostfix;
            if (Has("UpdateBadLifeRegen") || Has("UpdateLifeRegen"))
                On.Terraria.Player.UpdateLifeRegen += LifeRegen;
            if (Has("UpdateManaRegen"))
                On.Terraria.Player.UpdateManaRegen += ManaRegenPostfix;
            if (Has("UpdateDead"))
                On.Terraria.Player.UpdateDead += UpdateDeadPostfix;
            if (Has("UpdateMovement"))
                On.Terraria.Player.BordersMovement += MovementPrefix;
            // Player.EnterWorld 在 1.4.5.8.6 代理里不存在（无 On.* 钩子），OnEnterWorld 暂不可用。
            if (Has("OnRespawn"))
                On.Terraria.Player.Spawn += SpawnPostfix;
            if (Has("CanUseItem"))
                On.Terraria.Player.ItemCheck_CheckCanUse_Inner += CanUsePrefix;
            if (Has("PreKill") || Has("Kill"))
                On.Terraria.Player.KillMe += Kill;
            if (Has("PreModifyLuck") || Has("ModifyLuck"))
                On.Terraria.Player.RecalculateLuck += Luck;
            if (Has("SaveData") || Has("LoadData") || Has("PreSavePlayer") || Has("PostSavePlayer") ||
                Has("PreSaveCustomData"))
            {
                On.Terraria.Player.InternalSavePlayerFile += Save;
                On.Terraria.Player.LoadPlayer += LoadPostfix;
            }
        }

        // ===================== 生命周期 =====================

        private static void ResetEffectsPostfix(On.Terraria.Player.orig_ResetEffects orig, Player self)
        {
            orig(self);
            Each(self, "ResetEffects", m => m.ResetEffects(self));

            var life = 0;
            var mana = 0;
            Each(self, "ModifyMaxStats", m =>
            {
                m.ModifyMaxStats(self);
                life += m.CumulativeHealth;
                mana += m.CumulativeMana;
            });
            if (life != 0) self.statLifeMax2 = Math.Max(1, self.statLifeMax2 + life);
            if (mana != 0) self.statManaMax2 = Math.Max(0, self.statManaMax2 + mana);
        }

        private static void Update(On.Terraria.Player.orig_Update orig, Player self, int i)
        {
            Each(self, "PreUpdate", m => m.PreUpdate(self));
            orig(self, i);
            Each(self, "PostUpdate", m => m.PostUpdate(self));
        }

        private static void UpdateBuffs(On.Terraria.Player.orig_UpdateBuffs orig, Player self, int i)
        {
            Each(self, "PreUpdateBuffs", m => m.PreUpdateBuffs(self));
            orig(self, i);
            Each(self, "PostUpdateBuffs", m => m.PostUpdateBuffs(self));
        }

        private static void UpdateEquipsPostfix(On.Terraria.Player.orig_UpdateEquips orig, Player self, int i)
        {
            orig(self, i);
            Each(self, "UpdateEquips", m => m.UpdateEquips(self));
            Each(self, "PostUpdateEquips", m => m.PostUpdateEquips(self));
        }

        private static void LifeRegen(On.Terraria.Player.orig_UpdateLifeRegen orig, Player self)
        {
            Each(self, "UpdateBadLifeRegen", m => m.UpdateBadLifeRegen(self));
            orig(self);
            Each(self, "UpdateLifeRegen", m => m.UpdateLifeRegen(self));
        }

        private static void ManaRegenPostfix(On.Terraria.Player.orig_UpdateManaRegen orig, Player self)
        {
            orig(self);
            Each(self, "UpdateManaRegen", m => m.UpdateManaRegen(self));
        }

        private static void UpdateDeadPostfix(On.Terraria.Player.orig_UpdateDead orig, Player self)
        {
            orig(self);
            Each(self, "UpdateDead", m => m.UpdateDead(self));
        }

        private static void MovementPrefix(On.Terraria.Player.orig_BordersMovement orig, Player self)
        {
            Each(self, "UpdateMovement", m => m.UpdateMovement(self));
            orig(self);
        }

        private static void SpawnPostfix(On.Terraria.Player.orig_Spawn orig, Player self, PlayerSpawnContext context)
        {
            orig(self, context);
            if (context != PlayerSpawnContext.ReviveFromDeath) return;
            Each(self, "OnRespawn", m => m.OnRespawn(self));
        }

        private static bool CanUsePrefix(On.Terraria.Player.orig_ItemCheck_CheckCanUse_Inner orig,
                                         Player self, Item sItem, bool ignoreCursed)
        {
            if (Any(self, "CanUseItem", m => m.CanUseItem(self, sItem))) return false;
            return orig(self, sItem, ignoreCursed);
        }

        private static PlayerDeathReason _killReason;
        private static double _killDamage;
        private static int _killDir;
        private static bool _killPvp;
        private static bool _wasAlive;

        private static void Kill(On.Terraria.Player.orig_KillMe orig, Player self,
                                 PlayerDeathReason damageSource, double dmg, int hitDirection, bool pvp)
        {
            _killReason = damageSource;
            _killDamage = dmg;
            _killDir = hitDirection;
            _killPvp = pvp;
            _wasAlive = !self.dead && !self.creativeGodMode;
            if (_wasAlive && Any(self, "PreKill", m => m.PreKill(self, damageSource, dmg, hitDirection, pvp)))
            {
                return;   // 被某个 Mod 拒死：跳过原版
            }
            orig(self, damageSource, dmg, hitDirection, pvp);
            if (_wasAlive && self.dead)
                Each(self, "Kill", m => m.Kill(self, damageSource, dmg, hitDirection, pvp));
        }

        private static void Luck(On.Terraria.Player.orig_RecalculateLuck orig, Player self)
        {
            orig(self);
            _luckBefore = self.luck;
            var veto = Any(self, "PreModifyLuck", m => m.PreModifyLuck(self, ref _luckBefore));
            var luck = _luckBefore;
            if (veto)
            {
                Each(self, "ModifyLuck", m => m.ModifyLuck(self, ref luck));
                self.luck = luck;
            }
            else
            {
                // 原版算完之后再跑一次 ModifyLuck：无 Pre 拒改时仍允许改
                Each(self, "ModifyLuck", m => m.ModifyLuck(self, ref luck));
                self.luck = luck;
            }
        }

        // ===================== 侧车存档 =====================

        private static string DataFile(PlayerFileData fileData)
        {
            if (fileData == null || fileData.IsCloudSave) return null;
            var path = fileData.Path;
            return string.IsNullOrEmpty(path) ? null : path + ".plr.bl.json";
        }

        private static void Save(On.Terraria.Player.orig_InternalSavePlayerFile orig, PlayerFileData playerFile)
        {
            Each(playerFile?.Player, "PreSavePlayer", m => m.PreSavePlayer(playerFile.Player));
            orig(playerFile);

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

        private static PlayerFileData LoadPostfix(On.Terraria.Player.orig_LoadPlayer orig,
                                                  string playerPath, bool cloudSave)
        {
            var result = orig(playerPath, cloudSave);
            var player = result?.Player;
            var file = DataFile(result);
            if (player == null || file == null || !File.Exists(file)) return result;

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

            return result;
        }

        private static string KeyOf(ModPlayer template) =>
            (template.Mod?.Name ?? "Mod") + "/" + template.GetType().Name;

        internal static bool Loading => _loadingSave;
    }
}