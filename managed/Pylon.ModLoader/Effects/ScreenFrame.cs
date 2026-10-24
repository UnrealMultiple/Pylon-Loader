using System;
using System.Collections.Generic;

namespace Pylon.ModLoader.Effects
{
    /// <summary>离屏目标：盖满 viewport（Scale 0.5 = 半分辨率）。临时目标按名跨帧复用，可能带旧内容。</summary>
    public sealed class ScreenTarget
    {
        /// <summary>帧内唯一 id（供 ScreenEffect.Dest / Source 引用）。</summary>
        public int Id { get; set; }

        /// <summary>名字（跨帧复用的键）。</summary>
        public string Name { get; set; }

        /// <summary>分辨率系数 0&lt;..&lt;=1。</summary>
        public float Scale { get; set; } = 1f;

        /// <summary>保留内容（语义文档；临时目标按名复用）。</summary>
        public bool Persistent { get; set; }
    }

    /// <summary>一帧的效果：按顺序的步骤 + 用到的目标 + 是否要 History（上一帧世界）。</summary>
    public sealed class ScreenFrame
    {
        private readonly List<ScreenEffect> _draws = new();
        private readonly List<ScreenTarget> _targets = new();

        /// <summary>这帧要读上一帧世界（ScreenTexture.History）时开。</summary>
        public bool History { get; set; }

        /// <summary>按顺序执行的步骤（同一帧里后画的盖先画的）。</summary>
        public IReadOnlyList<ScreenEffect> Draws => _draws;

        /// <summary>这帧的离屏目标。</summary>
        public IReadOnlyList<ScreenTarget> Targets => _targets;

        public ScreenFrame Add(ScreenEffect effect)
        {
            if (effect == null) throw new ArgumentNullException(nameof(effect));
            _draws.Add(effect);
            return this;
        }

        public ScreenFrame AddTarget(ScreenTarget target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (target.Id <= 0) throw new ArgumentException("目标 id 必须 > 0", nameof(target));
            if (string.IsNullOrEmpty(target.Name)) throw new ArgumentException("目标名不能为空", nameof(target));
            if (target.Scale <= 0f || target.Scale > 1f) throw new ArgumentException("目标 Scale 必须在 (0,1]", nameof(target));
            _targets.Add(target);
            return this;
        }

        /// <summary>把这一帧交给渲染 hooks（游戏线程）。没步骤的帧会被忽略。</summary>
        public void Submit() => ScreenGraphics.Submit(this);
    }
}
