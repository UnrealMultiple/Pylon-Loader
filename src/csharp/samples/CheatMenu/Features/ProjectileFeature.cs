using System;
using System.Collections.Generic;
using System.Text.Json;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.ModLoader.ImGuiHost;
using Terraria;
using Terraria.DataStructures;
using CheatMenu.Infra;
using Game = CheatMenu.Infra.Game;

namespace CheatMenu.Features;

/// <summary>
/// 附加弹幕。1:1 对应原版 <c>ProjectileTool</c>（实体 · 附加弹幕）：
/// hook <c>Player.ItemCheck_StartActualUse</c>，使用物品且开启时按「手持武器 → 弹幕列表」
/// 配置在玩家位置朝光标方向额外发射弹幕，并同步 27 号包。
/// 配置按武器类型绑定（实时跟随手持切换），序列化为 JSON 存进 ModConfig。
/// </summary>
public sealed class ProjectileFeature : CheatFeature
{
    /// <summary>单条弹幕配置（默认：伤害 100、弹幕 ID 132、击退 10、速度 20；玩家位置、光标方向勾选）。</summary>
    public sealed class Bullet
    {
        public int id = 132;
        public int damage = 100;
        public int knockback = 10;
        public float fireSpeed = 20f;
        public bool useCursorPosition = true;
        public bool usePlayerPosition = true;
        public float dirX, dirY;
        public float spawnX, spawnY;
        public int ai1, ai2, ai3;
    }

    // 每个武器类型 → 弹幕列表；JSON 存 Config.ProjectileConfigJson
    private static Dictionary<int, List<Bullet>> _config = new();

    // 编辑对话框状态
    private static bool _dialogOpen;
    private static List<Bullet> _editList;
    private static int _editType = -1;
    private static bool _confirmDelete;

    public override string Id => "entity.projectile";
    public override string Title => "附加弹幕";
    public override CheatCategory Category => CheatCategory.Physical;
    public override string SubPage => "附加弹幕";

    public override void Install()
    {
        On.Terraria.Player.ItemCheck_StartActualUse += HookStartActualUse;
    }

    // ---------------- 序列化 ----------------

    private static void LoadFromConfig()
    {
        var json = CheatFeatureHost.Config?.ProjectileConfigJson;
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            _config = JsonSerializer.Deserialize<Dictionary<int, List<Bullet>>>(json) ?? new();
        }
        catch
        {
            _config = new Dictionary<int, List<Bullet>>();
        }
    }

    private static void SaveToConfig()
    {
        try
        {
            CheatFeatureHost.Config.ProjectileConfigJson =
                JsonSerializer.Serialize(_config);
            CheatFeatureHost.MarkDirty();
        }
        catch
        {
            // 序列化失败不动配置
        }
    }

    public static int ConfiguredWeapons => _config.Count;

    // ---------------- hook ----------------

    private static void HookStartActualUse(On.Terraria.Player.orig_ItemCheck_StartActualUse orig,
        Player self, Item sItem)
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is { ApplyProjectile: true } && self.controlUseItem && Game.IsLocal(self))
        {
            var heldItem = self.HeldItem;
            var itemType = heldItem?.type ?? 0;
            if (_config.TryGetValue(itemType, out var bullets))
            {
                foreach (var proj in bullets)
                {
                    Fire(self, proj);
                }
            }
        }
        orig(self, sItem);
    }

    private static void Fire(Player player, Bullet proj)
    {
        var pos = proj.usePlayerPosition
            ? player.Center
            : new Vector2(proj.spawnX, proj.spawnY);

        Vector2 vel;
        if (proj.useCursorPosition)
        {
            var center = player.Center;
            var mouse = Main.MouseWorld;
            var delta = mouse - center;
            if (delta.LengthSquared() < 0.001f) delta = new Vector2(0f, -1f);
            vel = Vector2.Normalize(delta) * proj.fireSpeed;
        }
        else
        {
            var dir = new Vector2(proj.dirX, proj.dirY);
            if (dir.LengthSquared() < 0.001f) dir = new Vector2(0f, -1f);
            vel = Vector2.Normalize(dir) * proj.fireSpeed;
        }

        // 代理里 EntitySource_ItemUse 不继承 IEntitySource：先造原生体，再用指针包一层
        // （与 Pylon.ModLoader 的 EntitySourcesTml 同一套写法）
        var itemUse = new EntitySource_ItemUse(
            new IEntitySourceTarget((nint)player.Pointer), player.HeldItem);
        IEntitySource source = new((nint)itemUse.Pointer);
        var index = Projectile.NewProjectile(source, pos, vel, proj.id, proj.damage,
            proj.knockback, Main.myPlayer, proj.ai1, proj.ai2, proj.ai3);
        if (Main.netMode == 1)
        {
            NetMessage.SendData(27, -1, -1, null, index, 0f, 0f, 0f, 0, 0, 0);
        }
    }

    // ---------------- UI ----------------

    public override void DrawMenu()
    {
        var cfg = CheatFeatureHost.Config;
        if (cfg is null) return;

        if (_config.Count == 0 && !string.IsNullOrWhiteSpace(cfg.ProjectileConfigJson)) LoadFromConfig();

        ModernUI.BeginCard("附加弹幕", "EXTRA PROJECTILES");

        RowToggle("##applyproj", "附加弹幕", cfg.ApplyProjectile, v => cfg.ApplyProjectile = v);
        ModernUI.DimText("使用物品时发射附加弹幕");
        ModernUI.BeginRow();
        ModernUI.RowLabel("已配置", $"{_config.Count} 个武器");
        ModernUI.EndRow();

        if (ModernUI.PrimaryButton("附加配置", default, ModernUI.Icon.None, rightAlign: true))
        {
            OpenDialog();
        }
        ModernUI.DimText("为当前手持武器添加/编辑附加弹幕");

        ModernUI.EndCard();

        if (_dialogOpen) DrawDialog();
    }

    private static void OpenDialog()
    {
        _editType = HeldWeaponType();
        LoadWeaponConfig();
        _confirmDelete = false;
        _dialogOpen = true;
    }

    private static int HeldWeaponType()
    {
        var held = Game.LocalPlayer?.HeldItem;
        return held?.type ?? 0;
    }

    private static void LoadWeaponConfig()
    {
        _editList = _config.TryGetValue(_editType, out var list)
            ? new List<Bullet>(list)
            : [new Bullet()];
    }

    private static void StoreWeaponConfig(int type, List<Bullet> list)
    {
        if (list is not { Count: > 0 }) _config.Remove(type);
        else _config[type] = list;
        SaveToConfig();
    }

    private static void DeleteWeaponConfig()
    {
        if (_editType > 0) StoreWeaponConfig(_editType, null);
        _editList = null;
    }

    /// <summary>实时跟随手持武器：切武器时把编辑保存回原武器再载入新武器。</summary>
    private static void FollowHeldWeapon()
    {
        var heldType = HeldWeaponType();
        if (heldType <= 0 || heldType == _editType) return;
        StoreWeaponConfig(_editType, _editList);
        _editType = heldType;
        _confirmDelete = false;
        LoadWeaponConfig();
    }

    /// <summary>配置对话框（原版 500x520 弹窗的内容，落在当前卡片里画）。</summary>
    private static void DrawDialog()
    {
        FollowHeldWeapon();

        ModernUI.BeginCard("附加弹幕配置", "EDIT", out var close, "关闭");
        if (close)
        {
            _dialogOpen = false;
            ModernUI.EndCard();
            return;
        }

        ModernUI.BeginRow();
        ModernUI.RowLabel("绑定武器", $"{_editType}（当前手持）· 共 {_editList.Count} 条");
        ModernUI.EndRow();

        for (var i = 0; i < _editList.Count; i++)
        {
            var b = _editList[i];
            ModernUI.SectionTitle($"弹幕 #{i + 1}  ID:{b.id}");

            var id = b.id;
            if (IntField($"##pid{i}", "弹幕ID", id, out id)) b.id = id;
            var damage = b.damage;
            if (IntField($"##pdmg{i}", "伤害", damage, out damage)) b.damage = damage;
            var knockback = b.knockback;
            if (IntField($"##pknock{i}", "击退", knockback, out knockback)) b.knockback = knockback;
            var speed = b.fireSpeed;
            if (ModernUI.RowSlider($"##speed{i}", "速度", "", ref speed, 0.1f, 100f, "0.0"))
                b.fireSpeed = Math.Clamp(speed, 0.1f, 100f);

            RowToggle($"##uppos{i}", "使用玩家位置", b.usePlayerPosition, v => b.usePlayerPosition = v);
            RowToggle($"##cursor{i}", "使用光标方向", b.useCursorPosition, v => b.useCursorPosition = v);

            if (!b.useCursorPosition)
            {
                var dx = b.dirX;
                if (FloatField($"##pdirx{i}", "方向X", dx, out dx)) b.dirX = dx;
                var dy = b.dirY;
                if (FloatField($"##pdiry{i}", "方向Y", dy, out dy)) b.dirY = dy;
            }
            if (!b.usePlayerPosition)
            {
                var sx = b.spawnX;
                if (FloatField($"##pspawnx{i}", "出生X", sx, out sx)) b.spawnX = sx;
                var sy = b.spawnY;
                if (FloatField($"##pspawny{i}", "出生Y", sy, out sy)) b.spawnY = sy;
            }

            var ai1 = b.ai1;
            if (IntField($"##pai1{i}", "AI1", ai1, out ai1)) b.ai1 = ai1;
            var ai2 = b.ai2;
            if (IntField($"##pai2{i}", "AI2", ai2, out ai2)) b.ai2 = ai2;
            var ai3 = b.ai3;
            if (IntField($"##pai3{i}", "AI3", ai3, out ai3)) b.ai3 = ai3;

            if (_editList.Count > 1 && ModernUI.GhostButton($"删除弹幕 #{i + 1}"))
            {
                _editList.RemoveAt(i);
                break;
            }
        }

        ModernUI.BeginRow();
        if (ModernUI.GhostButton("添加弹幕")) _editList.Add(new Bullet());
        if (ModernUI.GhostButton("应用配置"))
        {
            StoreWeaponConfig(_editType, _editList);
            _dialogOpen = false;
        }
        if (ModernUI.GhostButton("重置默认")) _editList = [new Bullet()];
        ModernUI.EndRow();

        if (_confirmDelete)
        {
            ModernUI.BeginRow();
            if (ModernUI.GhostButton("确认删除该武器配置"))
            {
                DeleteWeaponConfig();
                _confirmDelete = false;
            }
            if (ModernUI.GhostButton("取消")) _confirmDelete = false;
            ModernUI.EndRow();
        }
        else if (ModernUI.GhostButton("删除该武器配置"))
        {
            _confirmDelete = true;
        }

        ModernUI.EndCard();
    }

    // ---------------- 小控件（数字输入走 TextInput + 解析，语义同原版 InputInt/Float） ----------------

    private static bool IntField(string id, string label, int value, out int result)
    {
        result = value;
        var text = value.ToString();
        ModernUI.BeginRow();
        ModernUI.RowLabel(label);
        var changed = ModernUI.TextInput(id, ref text);
        ModernUI.EndRow();
        if (!changed || !int.TryParse(text, out var parsed)) return false;
        result = parsed;
        return true;
    }

    private static bool FloatField(string id, string label, float value, out float result)
    {
        result = value;
        var text = value.ToString("0.##");
        ModernUI.BeginRow();
        ModernUI.RowLabel(label);
        var changed = ModernUI.TextInput(id, ref text);
        ModernUI.EndRow();
        if (!changed || !float.TryParse(text, out var parsed)) return false;
        result = parsed;
        return true;
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
