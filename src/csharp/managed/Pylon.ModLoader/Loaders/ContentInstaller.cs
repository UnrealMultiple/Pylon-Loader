using System;
using System.Collections.Generic;
using System.Reflection;
using Pylon.ModLoader.Textures;
using Terraria;

namespace Pylon.ModLoader
{
    internal static class ContentInstaller
    {
        private static bool _armed;
        private static bool _installed;
        private static readonly List<Mod> _mods = new();   // 等扩表完成后调 SetupContent 的 Mod
        /// <summary>首个 Mod 的日志（内容安装是全局一次，日志归属首个 Mod 即可）。</summary>
        private static ModLogger Logger => _mods[0]?.Logger;

        public static void InstallTextures(Mod mod, IReadOnlyList<ModType> types)
        {
            PylonTexture.Logger = mod.Logger;

            // ① 贴图先加载缓存（type.Texture），扩表后写表槽用。
            int loaded = 0;
            foreach (var type in types)
            {
                if (string.IsNullOrEmpty(type.TexturePath)) continue;
                if (type is ModMount) continue;   // 坐骑贴图是 _Back/_Front 多层，由 MountLoader 装
                try
                {
                    if (type.Texture == null)
                    {
                        type.Texture = mod.LoadAsset(type.TexturePath);
                        loaded++;
                    }
                }
                catch (Exception e)
                {
                    mod.Logger.Warn($"{type.Name} 的贴图跳过（{type.TexturePath}）：{e.Message}");
                }
            }
            mod.Logger.Info($"贴图：缓存 {loaded}（扩表+写槽由每帧 tick 在表定型后做）");

            // ② 记下来：扩表完成后回调 SetupContent 系（时机与扩表一致，都在表定型那帧）。
            _mods.Add(mod);
            Arm(mod);
        }

        private static void Arm(Mod mod)
        {
            if (_armed) return;
            _armed = true;
            try
            {
                On.Terraria.Main.DoUpdate += Tick;
            }
            catch (Exception e)
            {
                mod.Logger.Warn("武装每帧 tick 失败：" + e.Message);
            }
        }

        /// <summary>每帧：表定型后**一步一帧**地把内容装进游戏（延迟加载 / 摊帧）。</summary>
        private static void Tick(On.Terraria.Main.orig_DoUpdate orig, Main self,
                                 Il2CppMicrosoft.Xna.Framework.GameTime gameTime)
        {
            orig(self, gameTime);
            var frame = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (!_armed || _mods.Count == 0) return;

                // 每帧先维护表：装的过程中游戏也可能建出新表、或把表重建回原版长度。
                MaintainTables();

                if (_installed) return;

                if (_step < 0)
                {
                    // 表定型后才开始。只判一次——扩表后这些判据会变，每帧重判会把自己挡在门外。
                    if (!GameLooksReady()) return;
                    EnsureSteps();
                    _step = 0;
                    _mods[0].Logger.Info($"[install] 表已定型，开始按帧安装（共 {_steps.Count} 步）");
                }

                if (_step >= _steps.Count) return;

                // 一帧只走一步：单帧做完 1.5s 的活会在主菜单前卡住（真机实测 1571ms）。
                var index = _step++;
                Timed(_labels[index], _steps[index]);

                if (_step >= _steps.Count)
                {
                    _installed = true;
                    _mods[0].Logger.Info("[install] 内容安装完成（全部步骤已按帧摊开）");
                }
            }
            catch (Exception e)
            {
                if (_mods.Count > 0) _mods[0].Logger.Warn($"内容安装帧异常：{e}");
            }
            finally
            {
                if (_mods.Count > 0 && frame.ElapsedMilliseconds >= 100)
                    _mods[0].Logger.Warn($"[install] 这一帧的安装步骤耗时 {frame.ElapsedMilliseconds}ms（单步太慢，要再拆）");
            }
        }

        /// <summary>装完之后每帧的表维护：新表建出来就补扩；表被重建回原版长度就再扩并重写贴图/名字。</summary>
        private static void MaintainTables()
        {
            try
            {
                var grown = TypeTables.Maintenance(m => Logger?.Info(m),
                    _ => Timed("表重建后重写贴图", WriteTextures));
                if (grown > 0) Logger?.Info($"[typetables] 维护：补扩 {grown} 张表");

                // 方块/墙壁的实例表与 TileObjectData：每 60 帧补一次（新玩家、新 SceneMetrics）。
                if (++_maintainAge >= 60)
                {
                    _maintainAge = 0;
                    TileLoader.Maintain();
                    WallLoader.Maintain();
                }
            }
            catch (Exception e)
            {
                Logger?.Warn($"表维护异常：{e.Message}");
            }
        }

        private static int _maintainAge;

        // ===================== 安装步骤表（一步一帧） =====================
        // 步骤队列可续：扩表每帧只扩一批，没扩完就排回队尾再接后续步骤。

        private static readonly List<Action> _steps = new();
        private static readonly List<string> _labels = new();
        private static int _step = -1;

        private static void EnsureSteps()
        {
            if (_steps.Count > 0) return;
            void Add(string label, Action action) { _labels.Add(label); _steps.Add(action); }

            var first = _mods[0];

            // 扩表：原生指针级，一个种类一帧；一张表一次 memcpy，预算只是保险。
            Action grow = null;   // 闭包自引用：没扩完时把自己再排到队尾，下一帧继续
            grow = () => GrowTablesAcrossFrames(first, grow);
            Add("扩表（分帧）", grow);

            // buff 的静态默认值（Main.debuff、BuffID.Sets 等）＋贴图与名字：要在 WriteTextures 之前。
            Add("buff 静态默认值", () => InitializeBuffStaticDefaults(first));
            // Player/NPC 的 buffImmune 是每实例数组，mod buff 要它变长才加得上。
            Add("buffImmune 扩长", BuffLoader.GrowImmuneArrays);
            // 坐骑：扩 Mount.mounts/MountID.Sets + 建 MountData + SetStaticDefaults。
            Add("坐骑安装", () => MountLoader.Setup(first.Logger));
            Add("前缀安装", () => PrefixLoader.Setup(first.Logger));
            Add("稀有度安装", () => RarityLoader.Setup(first.Logger));
            Add("配方收尾登记", () => Ready.Add(() => RecipeLoader.Finish(first.Logger), ReadyPhase.PostRecipes));

            // 扩表后写贴图。
            Add("写贴图", WriteTextures);
            // ContentSamples 样例：原版按 type 查字典做 tooltip/排序/研究，mod 物品不进会 KeyNotFound。
            Add("物品样例", RegisterSamples);
            // 方块/墙壁：tileMerge、TileObjectData、实例表、静态默认值与编译期上限。
            Add("墙壁安装", () => WallLoader.Setup(first.Logger));
            Add("方块安装", () => TileLoader.Setup(first.Logger));
            // mod 弹幕模板：new Projectile + SetDefaults(type)，以它为 this.Projectile 跑静态默认。
            Add("弹幕模板", () => InitializeProjectileTemplates(first));
            // NPC 的静态默认值要在样例之前：作者在 SetStaticDefaults 里写 Main.npcFrameCount[Type]。
            Add("NPC 静态默认值", () => InitializeNpcStaticDefaults(first));
            Add("NPC 样例", RegisterNpcSamples);
            Add("城镇 NPC 商店", () => TownNpcLoader.Setup(first.Logger));

            // 各 mod 的 SetupContent 系与每个内容的 PostSetupContent，都排进 Ready 的阶段队列。
            Add("SetupContent 系登记", () => RegisterModTasks(first));
            // Ready 的八阶段流（resize→…→finish）一次跑完：每阶段自带的活很小（真机 15ms）。
            Add("Ready 阶段流", Ready.Fire);
        }

        /// <summary>每帧扩一批表，全扩完才接后续步骤；一张表一次原生 memcpy，预算够一个种类。</summary>
        private static readonly TypeTables.TableKind[] GrowOrder =
        {
            TypeTables.TableKind.Item, TypeTables.TableKind.Projectile,
            TypeTables.TableKind.Npc, TypeTables.TableKind.Buff,
            TypeTables.TableKind.Tile, TypeTables.TableKind.Wall,
        };
        private const int TableBudget = 256;

        private static TypeTables.TableGrower _grower;
        private static int _growIndex;
        private static readonly int[] GrownCounts = new int[6];
        private static bool _grownLogged;

        private static void GrowTablesAcrossFrames(Mod first, Action self)
        {
            if (_grower == null)
            {
                var kind = GrowOrder[_growIndex];
                var capacity = ModContent.NextType(KindOf(kind));
                _grower = new TypeTables.TableGrower(kind, capacity);
            }

            GrownCounts[_growIndex] += _grower.NextBatch(TableBudget, m => first.Logger.Info(m));

            if (!_grower.Done)
            {
                // 插到下一个待执行位置（Insert 不是 Add），否则后续步骤会抢在扩表前跑，贴图写不进表槽。
                _steps.Insert(_step, self);
                _labels.Insert(_step, "扩表（分帧）");
                return;
            }

            _grower = null;
            _growIndex++;
            if (_growIndex < GrowOrder.Length)
            {
                _steps.Insert(_step, self);
                _labels.Insert(_step, "扩表（分帧）");
                return;
            }

            if (!_grownLogged)
            {
                _grownLogged = true;
                first.Logger.Info($"内容表扩容：物品 {GrownCounts[0]}、弹幕 {GrownCounts[1]}、" +
                                  $"NPC {GrownCounts[2]}、Buff {GrownCounts[3]}、方块 {GrownCounts[4]}、" +
                                  $"墙壁 {GrownCounts[5]}");
            }
        }

        private static ContentKind KindOf(TypeTables.TableKind kind) => kind switch
        {
            TypeTables.TableKind.Item => ContentKind.Item,
            TypeTables.TableKind.Projectile => ContentKind.Projectile,
            TypeTables.TableKind.Npc => ContentKind.Npc,
            TypeTables.TableKind.Buff => ContentKind.Buff,
            TypeTables.TableKind.Tile => ContentKind.Tile,
            _ => ContentKind.Wall,
        };

        /// <summary>把各 mod 的 SetupContent/PostSetupContent 排进 Ready（配方/世界时机由 ModLifecycle 在加载时排）。</summary>
        private static void RegisterModTasks(Mod first)
        {
            for (int i = 0; i < _mods.Count; i++)
            {
                var m = _mods[i];
                try
                {
                    m.Logger.Info("内容表已定型：SetupContent");
                    Ready.Add(() => m.SetupContent(), ReadyPhase.Setup);
                    Ready.Add(() => m.PostSetupContent(), ReadyPhase.Mods);
                }
                catch (Exception e)
                {
                    m.Logger.Warn($"SetupContent 系异常（注册进队列）：{e.Message}");
                }
            }
            Ready.Log = first.Logger;
            // 每个内容自己的 PostSetupContent——之前一条都没接，作者写了也不跑。
            AddContentPostSetup();
            AddGlobalTasks();
        }

        private static string SafeGameMenu()
        {
            try { return Main.gameMenu ? "是" : "否"; } catch { return "?"; }
        }

        /// <summary>每个内容自己的 PostSetupContent：排进 Ready 的 content 阶段；跨内容引用写在这里。</summary>
        private static void AddContentPostSetup()
        {
            foreach (var type in ModContent.GetContent<ModType>())
            {
                switch (type)
                {
                    case ModItem item: Ready.Add(item.PostSetupContent, ReadyPhase.Content); break;
                    case ModNpc npc: Ready.Add(npc.PostSetupContent, ReadyPhase.Content); break;
                    case ModBuff buff: Ready.Add(buff.PostSetupContent, ReadyPhase.Content); break;
                    case ModProjectile projectile: Ready.Add(projectile.PostSetupContent, ReadyPhase.Content); break;
                    case ModTile tile: Ready.Add(tile.PostSetupContent, ReadyPhase.Content); break;
                    case ModWall wall: Ready.Add(wall.PostSetupContent, ReadyPhase.Content); break;
                    case GlobalBuff globalBuff: Ready.Add(globalBuff.PostSetupContent, ReadyPhase.Content); break;
                    case GlobalNpc globalNpc: Ready.Add(globalNpc.PostSetupContent, ReadyPhase.Content); break;
                    case GlobalTile globalTile: Ready.Add(globalTile.PostSetupContent, ReadyPhase.Content); break;
                    case GlobalRecipe globalRecipe: Ready.Add(globalRecipe.PostSetupContent, ReadyPhase.Content); break;
                    case GlobalBossBar globalBossBar: Ready.Add(globalBossBar.PostSetupContent, ReadyPhase.Content); break;
                }
            }
        }

        /// <summary>跑一步并计时：≥40ms 就单独记一条（找出卡顿来源用）。</summary>
        private static void Timed(string label, Action action)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try { action(); }
            finally
            {
                if (watch.ElapsedMilliseconds >= 40 && _mods.Count > 0)
                    _mods[0].Logger.Warn($"[install] {label} {watch.ElapsedMilliseconds}ms");
            }
        }

        /// <summary>全部自定义内容：贴图写表槽 + 名字/介绍写 Lang 缓存。</summary>
        private static void WriteTextures()
        {
            int written = 0;
            foreach (var type in ModContent.GetContent<ModType>())
            {
                // 名字/说明与贴图无关：没有贴图也要注册，否则 buff 条上显示的是宿主（原版 buff）的名字。
                if (type is ModItem item)
                {
                    try { ApplyName(item); }
                    catch (Exception e) { Logger?.Warn($"{item.Name} 名字写入失败：{e.Message}"); }
                    try { ApplyTooltip(item); }
                    catch (Exception e) { Logger?.Warn($"{item.Name} 介绍写入失败：{e.Message}"); }
                }
                if (type is ModBuff buff)
                {
                    try { ApplyBuffText(buff); }
                    catch (Exception e) { Logger?.Warn($"{buff.Name} 的 buff 文本写入失败：{e.Message}"); }
                }

                if (type.Texture == null) continue;
                var table = TableFor(type);
                if (table == null) continue;
                try
                {
                    if (TextureSlots.TryWrite(table.Value, type.Type, type.Texture)) written++;
                    else Logger?.Warn($"{type.Name} 的贴图没写进 {table}[{type.Type}]" +
                                          $"（表长 {TextureSlots.Length(table.Value)}）");
                }
                catch (Exception e)
                {
                    Logger?.Warn($"{type.Name} 的贴图写槽失败：{e.Message}");
                }

            }
            Logger?.Info($"贴图：写入表槽 {written}");
        }

        /// <summary>mod 弹幕模板初始化：建 Template（new Projectile + SetDefaults(type)）作 this.Projectile，
        /// 跑 AutoStaticDefaults → SetStaticDefaults → PostStaticDefaults。</summary>
        private static void InitializeProjectileTemplates(Mod first)
        {
            var count = 0;
            foreach (var type in ModContent.GetContent<ModProjectile>())
            {
                try
                {
                    ProjectileSetDefaultsPatch.SuppressMod = true;
                    try
                    {
                        var template = new Projectile();
                        template.SetDefaults(type.Type);
                        type.Template = template;
                    }
                    finally { ProjectileSetDefaultsPatch.SuppressMod = false; }

                    type.AutoStaticDefaults();
                    type.SetStaticDefaults();
                    type.PostStaticDefaults();
                    count++;
                }
                catch (Exception e)
                {
                    first?.Logger.Warn($"{type.Name} 弹幕模板初始化失败：{e.Message}");
                }
            }
            if (count > 0) first?.Logger.Info($"弹幕模板初始化：{count} 个 mod 弹幕");
        }

        /// <summary>每个 mod 物品在 ContentSamples.ItemsByType 放一份样例（新 Item + SetDefaults），并登记持久 ID；
        /// 没有会 KeyNotFound（tooltip/排序/研究崩）；SetDefaults 会走我们的补丁真的跑一遍。</summary>
        private static void RegisterSamples()
        {
            var itemsByType = Terraria.ID.ContentSamples.ItemsByType;
            if (itemsByType == null) return;
            foreach (var type in ModContent.GetContent<ModItem>())
            {
                try
                {
                    var sample = new Item();
                    sample.SetDefaults(type.Type, null);
                    itemsByType[type.Type] = sample;

                    var persistentId = (type.Mod?.Name ?? "Mod") + "/" + type.Name;
                    if (Terraria.ID.ContentSamples.ItemPersistentIdsByNetIds != null)
                        Terraria.ID.ContentSamples.ItemPersistentIdsByNetIds[type.Type] = persistentId;
                    if (Terraria.ID.ContentSamples.ItemNetIdsByPersistentIds != null)
                        Terraria.ID.ContentSamples.ItemNetIdsByPersistentIds[persistentId] = type.Type;
                }
                catch (Exception e)
                {
                    Logger?.Warn($"{type.Name} 的 ContentSamples 样例注册失败：{e.Message}");
                }
            }
            Logger?.Info($"ContentSamples 样例已注册：{ModContent.GetContent<ModItem>().Count} 个 mod 物品");
        }

        /// <summary>mod NPC 静态默认值：表定型、能写 Main.npcFrameCount[Type] 与 NPCID.Sets 时跑一次。</summary>
        private static void InitializeNpcStaticDefaults(Mod first)
        {
            var count = 0;
            foreach (var type in ModContent.GetContent<ModNpc>())
            {
                try
                {
                    type.SetStaticDefaults();
                    type.PostStaticDefaults();
                    // 掉落表一次注册；少了它 npcLoot.Add 永远不生效（作者写了也没掉落）。
                    var npc = type;
                    Safe.Run(type.Name + ".ModifyNPCLoot", () => npc.ModifyNPCLoot(new NPCLoot(npc.Type)));
                    count++;
                }
                catch (Exception e)
                {
                    first?.Logger.Warn($"{type.Name} 的 NPC 静态默认值失败：{e.Message}");
                }
            }
            if (count > 0) first?.Logger.Info($"NPC 静态默认值：{count} 个 mod NPC");
        }

        /// <summary>mod buff 与 GlobalBuff 的静态默认值 + DisplayName/Description 的本地化钩子。</summary>
        private static void InitializeBuffStaticDefaults(Mod first)
        {
            var count = 0;
            foreach (var type in ModContent.GetContent<ModBuff>())
            {
                try
                {
                    type.SetStaticDefaults();
                    type.PostStaticDefaults();
                    type.ModifyDisplayName();
                    type.ModifyDescription();
                    count++;
                }
                catch (Exception e)
                {
                    first?.Logger.Warn($"{type.Name} 的 buff 静态默认值失败：{e.Message}");
                }
            }
            foreach (var global in GlobalBuffRegistry.All)
            {
                try { global.SetStaticDefaults(); }
                catch (Exception e) { first?.Logger.Warn($"{global.Name} 的 GlobalBuff 静态默认值失败：{e.Message}"); }
            }
            if (count > 0) first?.Logger.Info($"buff 静态默认值：{count} 个 mod buff");
        }

        /// <summary>GlobalNpc 的 AddRecipeGroups（groups 阶段）与 SetStaticDefaults→AddRecipes→PostSetupContent（content 阶段）；
        /// 其余 Global 的这几个方法还没进 API。</summary>
        private static void AddGlobalTasks()
        {
            foreach (var global in GlobalNpcRegistry.All)
            {
                var npc = global;
                Ready.Add(npc.AddRecipeGroups, ReadyPhase.Groups);
                Ready.Add(() =>
                {
                    Safe.Run(npc.Name + ".SetStaticDefaults", npc.SetStaticDefaults);
                    Safe.Run(npc.Name + ".AddRecipes", npc.AddRecipes);
                    Safe.Run(npc.Name + ".PostSetupContent", npc.PostSetupContent);
                }, ReadyPhase.Content);
            }
        }

        /// <summary>mod NPC 必须在 ContentSamples 六张表都有条目：只填 NpcsByNetId 会在图鉴填充时 KeyNotFound，
        /// 主菜单画不出来（真机黑屏）；建样例会走我们的 SetDefaults 分支真跑一遍。</summary>
        private static void RegisterNpcSamples()
        {
            var npcs = Terraria.ID.ContentSamples.NpcsByNetId;
            if (npcs == null) return;

            var persistentById = Terraria.ID.ContentSamples.NpcPersistentIdsByNetIds;
            var idByPersistent = Terraria.ID.ContentSamples.NpcNetIdsByPersistentIds;
            var creditById = Terraria.ID.ContentSamples.NpcBestiaryCreditIdsByNpcNetIds;
            var sorting = Terraria.ID.ContentSamples.NpcBestiarySortingId;
            var rarity = Terraria.ID.ContentSamples.NpcBestiaryRarityStars;

            foreach (var type in ModContent.GetContent<ModNpc>())
            {
                try
                {
                    var sample = new NPC();
                    sample.SetDefaults(type.Type, new Terraria.NPCSpawnParams());
                    npcs[type.Type] = sample;

                    var persistentId = (type.Mod?.Name ?? "Mod") + "/" + type.Name;
                    if (persistentById != null) persistentById[type.Type] = persistentId;
                    if (idByPersistent != null) idByPersistent[persistentId] = type.Type;
                    // 击杀计数器/图鉴解锁按这个 id 记账；缺了就是 KeyNotFound。
                    if (creditById != null) creditById[type.Type] = persistentId;
                    if (sorting != null) sorting[type.Type] = type.Type;

                    var stars = type.BestiaryRarityStars;
                    if (stars <= 0) stars = Math.Clamp(1 + sample.lifeMax / 250, 1, 5);
                    if (rarity != null) rarity[type.Type] = stars;

                    ApplyNpcName(type);
                }
                catch (Exception e)
                {
                    Logger?.Warn($"{type.Name} 的 NPC 样例注册失败：{e.Message}");
                }
            }
            Logger?.Info($"ContentSamples NPC 样例已注册：{ModContent.GetContent<ModNpc>().Count} 个 mod NPC");
        }

        /// <summary>名字与说明写进 Lang 的两张 buff 缓存 + 语言键表。</summary>
        private static void ApplyBuffText(ModBuff buff)
        {
            var name = string.IsNullOrEmpty(buff.DisplayName) ? buff.Name : buff.DisplayName;
            var description = buff.Description ?? "";

            var names = Terraria.Lang._buffNameCache;
            if (names != null && buff.Type >= 0 && buff.Type < names.Length)
            {
                var key = "BuffName." + buff.Name;
                var text = new Terraria.Localization.LocalizedText(key, name);
                names[buff.Type] = text;
                RegisterLanguageKey(key, text);
            }

            var descriptions = Terraria.Lang._buffDescriptionCache;
            if (descriptions != null && buff.Type >= 0 && buff.Type < descriptions.Length)
            {
                var key = "BuffDescription." + buff.Name;
                var text = new Terraria.Localization.LocalizedText(key, description);
                descriptions[buff.Type] = text;
                RegisterLanguageKey(key, text);
            }
        }

        /// <summary>NPC 名字写进 Lang._npcNameCache + 语言键表。</summary>
        private static void ApplyNpcName(ModNpc npcType)
        {
            var cache = Terraria.Lang._npcNameCache;
            if (cache == null || npcType.Type < 0 || npcType.Type >= cache.Length) return;
            var text = string.IsNullOrEmpty(npcType.DisplayName) ? npcType.Name : npcType.DisplayName;
            var key = "NPCName." + npcType.Name;
            var localized = new Terraria.Localization.LocalizedText(key, text);
            cache[npcType.Type] = localized;
            RegisterLanguageKey(key, localized);
        }

        /// <summary>自定义物品名写进 Lang._itemNameCache + 语言键表，否则显示空白名。</summary>
        private static void ApplyName(ModItem item)
        {
            var cache = Terraria.Lang._itemNameCache;
            if (cache == null || item.Type < 0 || item.Type >= cache.Length)
            {
                Logger?.Warn($"ApplyName：缓存不可用 type={item.Type} len={cache?.Length ?? -1}");
                return;
            }
            var key = "ItemName." + item.Name;
            var text = new Terraria.Localization.LocalizedText(key, item.DisplayName);
            cache[item.Type] = text;
            RegisterLanguageKey(key, text);
        }

        /// <summary>Tooltip 拆行 → ModifyTooltipLines 改 → 写进 Lang._itemTooltipCache + 语言键表。</summary>
        private static void ApplyTooltip(ModItem item)
        {
            // Mod 在 ModifyTooltipLines 里改 TooltipLines，合成回字符串再进缓存。
            item.TooltipLines.Clear();
            if (!string.IsNullOrEmpty(item.Tooltip))
                item.TooltipLines.AddRange(item.Tooltip.Split('\n'));
            try { item.ModifyTooltipLines(); }
            catch (Exception e) { Logger?.Warn($"{item.Name} 的 ModifyTooltipLines 异常：{e.Message}"); }
            if (item.TooltipLines.Count == 0) return;

            var cache = Terraria.Lang._itemTooltipCache;
            if (cache == null || item.Type < 0 || item.Type >= cache.Length)
            {
                Logger?.Warn($"ApplyTooltip：缓存不可用 type={item.Type} len={cache?.Length ?? -1}");
                return;
            }
            // 自定义前缀，避免与 ItemTooltip.* 原版键冲突/覆盖。
            var key = "Pylon.ItemTooltip." + (item.Mod != null ? item.Mod.Name : "Mod") + "." + item.Name;
            var text = string.Join("\n", item.TooltipLines);
            RegisterLanguageKey(key, new Terraria.Localization.LocalizedText(key, text));
            cache[item.Type] = Terraria.UI.ItemTooltip.FromLanguageKey((short)item.Type, key);
        }

        /// <summary>把 key → LocalizedText 注册进 LanguageManager._localizedTexts（Lang.GetItemName 的查询来源）。</summary>
        private static void RegisterLanguageKey(string key, Terraria.Localization.LocalizedText text) =>
            LocalizationKeys.Register(key, text);

        /// <summary>就绪判据：异步加载完成、贴图表/名字缓存建好、ContentSamples 字典存在；
        /// 缺一样就是扩早，游戏重建表会把自定义槽覆盖（贴图退成 "nothing"）。</summary>
        private static bool GameLooksReady()
        {
            try
            {
                // 异步加载完成前绝不碰（早了贴图小）。
                if (!Main._isAsyncLoadComplete) return false;
                // 槽位表建好 + 掉落库/图鉴库存在 + 原版配方已注册；少了 numRecipes>0 会早于 Recipe 表建立（真机踩过）。
                if (Main.ItemDropsDB == null || Main.BestiaryDB == null) return false;
                if (Terraria.Recipe.numRecipes <= 0) return false;

                // 贴图表建好且还是原版长度（未被扩过）。
                int texLen = TextureSlots.Length(TextureTable.Item);
                if (texLen != Terraria.ID.ItemID.Count) return false;

                // 语言缓存建好（Lang._itemNameCache，LocalizedText[]）。
                var names = Terraria.Lang._itemNameCache;
                if (names == null || names.Length < Terraria.ID.ItemID.Count) return false;

                // ContentSamples.ItemsByType（Dictionary<int,Item>）：游戏内容初始化完成后才建。
                return Terraria.ID.ContentSamples.ItemsByType != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>内容种类 → 游戏表；System/Global 不占游戏 ID，也就没有表。</summary>
        private static TextureTable? TableFor(ModType type)
        {
            switch (type)
            {
                case ModItem _: return TextureTable.Item;
                case ModTile _: return TextureTable.Tile;
                case ModWall _: return TextureTable.Wall;
                case ModProjectile _: return TextureTable.Projectile;
                case ModNpc _: return TextureTable.Npc;
                case ModBuff _: return TextureTable.Buff;
                default: return null;
            }
        }
    }
}