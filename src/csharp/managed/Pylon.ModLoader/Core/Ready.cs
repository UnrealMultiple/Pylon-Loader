// 层：Mod 框架 / Core —— 内容就绪的任务队列：内容入游戏后按 8 阶段排队，每阶段逐任务 Safe.Run 隔离：
//   resize → setup → groups → mods → content → recipes → postRecipes → finish
// 顺序：groups 最先（配方可引用别的 mod 的组）；mods=Mod.PostSetupContent 在 content 前（tModLoader 语义）；
//   setup 放"别人请求你先就绪"的内容（如 MountData）；recipes 后固定跑 Recipe/BestiaryLoader.Finish；finish 是最后可运行点。
// 各 Loader 注册期调 Ready.Add(fn, phase)，内容就绪时统一跑；一个挂了不拖垮别的。
using System;
using System.Collections.Generic;

namespace Pylon.ModLoader
{
    /// <summary>内容就绪的阶段名（Ready.Add 的 phase 参数）。</summary>
    internal static class ReadyPhase
    {
        public const string Resize = "resize";           // 数组扩容回调
        public const string Setup = "setup";             // 被"别人请求先就绪"的内容（如 MountData）
        public const string Groups = "groups";           // AddRecipeGroups
        public const string Mods = "mods";               // Mod.PostSetupContent
        public const string Content = "content";         // 各 Loader 的 PostSetupContent
        public const string Recipes = "recipes";         // AddRecipes
        public const string PostRecipes = "postRecipes"; // PostAddRecipes
        public const string Finish = "finish";           // 最后一个可运行点（PostSetupRecipes）
    }

    /// <summary>内容就绪的任务队列：注册期 Add，内容就绪时按阶段依次跑（Safe.Run 隔离）。</summary>
    internal static class Ready
    {
        private static readonly Dictionary<string, List<Action>> Tasks = new(StringComparer.Ordinal)
        {
            [ReadyPhase.Resize] = new(),
            [ReadyPhase.Setup] = new(),
            [ReadyPhase.Groups] = new(),
            [ReadyPhase.Mods] = new(),
            [ReadyPhase.Content] = new(),
            [ReadyPhase.Recipes] = new(),
            [ReadyPhase.PostRecipes] = new(),
            [ReadyPhase.Finish] = new(),
        };

        private static bool _fired;
        private static bool _running;

        /// <summary>排一个就绪任务；内容已就绪（已跑过）就直接执行（晚注册的 Loader 也要生效）。</summary>
        internal static void Add(Action task, string phase = ReadyPhase.Content)
        {
            if (task == null) return;
            if (!Tasks.TryGetValue(phase, out var list)) throw new ArgumentException($"未知阶段 '{phase}'");

            if (_fired)
            {
                // 就绪后注册的东西：立即跑（装在内容就绪 hook 之后的 Loader 用）。
                if (!_running) Safe.Run($"Ready.{phase}（晚注册）", task);
                else list.Add(task);   // 正在跑阶段流：排到当前阶段末尾，本阶段循环可能已过则靠下次
                return;
            }
            list.Add(task);
        }

        /// <summary>内容就绪：按阶段顺序跑全部任务（幂等，只一次）。由 ContentInstaller 在表定型后调。</summary>
        internal static void Fire()
        {
            if (_fired) return;
            _fired = true;
            _running = true;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                RunPhase(ReadyPhase.Resize);
                RunPhase(ReadyPhase.Setup);
                RunPhase(ReadyPhase.Groups);
                RunPhase(ReadyPhase.Mods);
                RunPhase(ReadyPhase.Content);
                RunPhase(ReadyPhase.Recipes);
                RunPhase(ReadyPhase.PostRecipes);
                // recipes 之后固定收尾（Bestiary/RecipeLoader.Finish），Pylon 对应物在各管线里注册到 finish。
                RunPhase(ReadyPhase.Finish);
                Log?.Info($"[ready] 阶段总耗时 {watch.ElapsedMilliseconds}ms");
            }
            finally
            {
                _running = false;
            }
        }

        internal static ModLogger Log;

        private static void RunPhase(string phase)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var list = Tasks[phase];
            for (var i = 0; i < list.Count; i++)
            {
                var task = list[i];
                try { Safe.Run("Ready." + phase, task); }
                catch { /* Safety.Run 内部处理，这里兜底 */ }
            }
            list.Clear();   // 本阶段跑完清空：晚注册的走 Add 的"已就绪"立即跑分支
            if (list.Count == 0 && watch.ElapsedMilliseconds >= 50)
                Log?.Info($"[ready] {phase} 阶段耗时 {watch.ElapsedMilliseconds}ms");
        }
    }
}