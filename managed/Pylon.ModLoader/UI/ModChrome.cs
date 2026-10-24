using System;
using Pylon.ImGui;
using ImVec2 = Pylon.ImGui.ImVec2;
using ImGuiApi = Pylon.ImGui.ImGui;

namespace Pylon.ModLoader.UI
{
    /// <summary>Chrome 侧边栏导航项。</summary>
    public readonly struct ChromeNav(string key, string label, int icon)
    {
        public readonly string Key = key;    
        public readonly string Label = label;
        public readonly int Icon = icon;     
    }

    /// <summary>Chrome 窗口状态（每个窗口持有自己的实例）。</summary>
    public sealed class ChromeState
    {
        public bool Open = true;           
        public bool SmallSide;             
        public float SideAnim;
        public int Active;
        public float TabAnim = 1f;
        public float SlideDir;
        public int ManualMode = -1;        
        public float NavScroll;
        public float IndicatorY;           
        public float IndicatorVel;         
        public float[] HoverAnim = Array.Empty<float>();
        public ImVec2 CMin, CSize;
        public bool FpsInit;
        public float Fps = 60f;
        // 拖动（头部）
        public bool Dragging;
        public ImVec2 PressPos, GrabOffset;
        // 特效实例（背景 + 前景共用）
        public readonly BeamRain Beams = new();
        public readonly ParticleBurst Burst = new();
        public readonly FloatParticles Floats = new();
        public readonly TwinkleStars Twinkles = new();
        public readonly AuroraWave Aurora = new();
        public bool OpenedOnce;
        // 悬浮球（折叠态）
        public float BallScale = 1f;
        public bool BallDragging;
        public ImVec2 BallGrabOffset;
        public bool BallWasHeld;
    }

    /// <summary>Chrome 窗口（完整移植 ModernWindow.h 的 DrawWindow）。</summary>
    public static class Chrome
    {
        private const int RoundCornersNone = 0;
        private const int RoundCornersTopLeft = 1 << 4;
        private const int RoundCornersTopRight = 1 << 5;
        private const int RoundCornersBottomLeft = 1 << 6;
        private const int RoundCornersBottomRight = 1 << 7;
        private const int RoundCornersTop = RoundCornersTopLeft | RoundCornersTopRight;
        private const int RoundCornersBot = RoundCornersBottomLeft | RoundCornersBottomRight;
        private const int RoundCornersAll = 0x0F << 4;

        private static float U()
        {
            float fs = ImGuiApi.FontSize;
            return fs > 1f ? fs / 20f : 1f;
        }
        private static float SideWidthFull() => 172f * U();
        private static float SideWidthSmall() => 62f * U();
        private static float HeaderH() => 84f * U();
        private static float FxPad() => 24f * U();
        private static float Gap() => 34f * U();

        /// <summary>画一帧 Chrome（须在 ImGuiApi.Begin/End 内；调用方负责窗口 Begin）；返回点击的导航索引（-1 = 未点）。</summary>
        public static int Draw(ChromeState st, string title, string subtitle,
                               ChromeNav[] items)
        {
            if (!st.Open) return -1;   // 已折叠为悬浮球：不画 Chrome（调用方画球）
            var p = GlassPalette.Dark();
            float dt = Glass.Dt();
            var dl = DrawList.Window;
            if (!dl.Valid) return -1;

            var wMin = ImGuiApi.GetWindowPos();
            var wSize = ImGuiApi.GetWindowSize();
            var wMax = new ImVec2(wMin.X + wSize.X, wMin.Y + wSize.Y);
            float fs = ImGuiApi.FontSize;
            float u = U();
            float fxPad = FxPad();
            var fxMin = new ImVec2(wMin.X + fxPad, wMin.Y + fxPad);
            var fxMax = new ImVec2(wMax.X - fxPad, wMax.Y - fxPad);
            float winRad = ImGuiApi.StyleWindowRounding * (fs / 45f) * 0.44f;   // 近似 kPlan*0.44

            // ---- 背景特效层（铺满整窗，圆角避角）----
            DotGridGrid(dl, wMin, wMax, p.DotGrid, 22f * u, winRad);
            st.Aurora.Update(dt); st.Aurora.Draw(dl, wMin, wMax, 1f, winRad);
            st.Beams.Update(dt); st.Beams.Draw(dl, wMin, wMax, p.Accent, 1f);
            st.Twinkles.Update(dt); st.Twinkles.Draw(dl, wMin, wMax);
            st.Floats.Update(dt); st.Floats.Draw(dl, wMin, wMax, 1f);
            if (!st.OpenedOnce)
            {
                st.OpenedOnce = true;
                st.Burst.Emit(new ImVec2((fxMin.X + fxMax.X) * 0.5f, fxMin.Y + 60f * u), p.Accent, 36);
            }
            st.Burst.Update(dt);

            // ---- 头部 ----
            float headerH = HeaderH();
            var hMin = new ImVec2(fxMin.X, wMin.Y);
            var hMax = new ImVec2(fxMax.X, wMin.Y + headerH);
            float hMidY = (hMin.Y + hMax.Y) * 0.5f;
            float cbR = 19f * u;   // 关闭按钮半径

            // 头部拖动（NoMove 窗口，手动 SetWindowPos；Chrome.hpp 同款：8px 阈值判拖动）
            ImGuiApi.SetCursorScreenPos(hMin);
            ImGuiApi.InvisibleButton("##hdrDrag", hMax.X - hMin.X - cbR * 2f - 44f * u, hMax.Y - hMin.Y);
            bool hdrHeld = ImGuiApi.IsItemActive();
            if (ImGuiApi.IsItemActivated())
            {
                st.PressPos = ImGuiApi.MousePos;
                st.GrabOffset = new ImVec2(wMin.X - ImGuiApi.MousePos.X, wMin.Y - ImGuiApi.MousePos.Y);
                st.Dragging = false;
            }
            if (hdrHeld && !st.Dragging)
            {
                float dx = ImGuiApi.MousePos.X - st.PressPos.X;
                float dy = ImGuiApi.MousePos.Y - st.PressPos.Y;
                if (dx * dx + dy * dy > 14f * 14f) st.Dragging = true;
            }
            if (st.Dragging && hdrHeld)
                ImGuiApi.SetWindowPos(ImGuiApi.MousePos.X + st.GrabOffset.X, ImGuiApi.MousePos.Y + st.GrabOffset.Y);
            if (ImGuiApi.IsItemDeactivated()) st.Dragging = false;

            // logo：圆 + 四角星
            float lr = 23f * u;
            var lc = new ImVec2(hMin.X + 45f * u, hMidY);
            dl.AddCircleFilled(lc, lr, p.AccentSoft, 40);
            dl.AddCircleFilled(lc, lr * 0.78f, p.Accent, 40);
            dl.AddCircle(lc, lr, p.Accent, 40, 1.8f);
            DrawSpark(dl, lc, lr * 0.46f, 0xFFFFFFFF);

            // 标题/副标题（按比例字号）
            float titleX = hMin.X + 84f * u;
            float tFont = fs * 0.95f, sFont = fs * 0.55f;
            var tsz = ImGuiApi.CalcTextSizeA(tFont, title);
            var ssz = ImGuiApi.CalcTextSizeA(sFont, subtitle);
            float tTotal = tsz.Y + ssz.Y + 3f * u;
            float tTop = hMidY - tTotal * 0.5f;
            dl.AddTextFont(new ImVec2(titleX, tTop), p.Text, title, tFont);
            dl.AddTextFont(new ImVec2(titleX + 2f * u, tTop + tsz.Y + 3f * u), p.TextDim, subtitle, sFont);

            // FPS 徽标
            float bFont = fs * 0.85f;
            string fpsBuf = $"{st.Fps:0} FPS";
            var bs = ImGuiApi.CalcTextSizeA(bFont, fpsBuf);
            float dotR = 4.5f * u, gapDot = 8f * u, padX = 14f * u, padY = 6f * u;
            float badgeW = padX + dotR * 2f + gapDot + bs.X + padX;
            float badgeH = bs.Y + padY * 2f;
            float cbCX = hMax.X - 26f * u;
            var bMax = new ImVec2(cbCX - cbR - 12f * u, hMidY + badgeH * 0.5f);
            var bMin = new ImVec2(bMax.X - badgeW, bMax.Y - badgeH);
            dl.AddRectFilled(bMin, bMax, p.AccentSoft, badgeH * 0.5f);
            dl.AddRect(bMin, bMax, p.CardBorder, badgeH * 0.5f, 1f);
            dl.AddCircleFilled(new ImVec2(bMin.X + padX + dotR, (bMin.Y + bMax.Y) * 0.5f), dotR, p.Success, 0);
            dl.AddTextFont(new ImVec2(bMin.X + padX + dotR * 2f + gapDot, (bMin.Y + bMax.Y) * 0.5f - bs.Y * 0.5f),
                           p.Text, fpsBuf, bFont);

            // 关闭按钮（折叠为悬浮球）
            var cbC = new ImVec2(cbCX, hMidY);
            ImGuiApi.SetCursorScreenPos(new ImVec2(cbC.X - cbR, cbC.Y - cbR));
            ImGuiApi.InvisibleButton("##hdrClose", cbR * 2f, cbR * 2f);
            bool cbHover = ImGuiApi.IsItemHovered();
            if (ImGuiApi.IsItemClicked()) st.Open = false;
            dl.AddCircleFilled(cbC, cbR, cbHover ? p.Danger : p.CardBorder, 28);
            dl.AddCircle(cbC, cbR, cbHover ? p.Danger : p.CardBorder, 28, 1.6f);
            uint xCol = cbHover ? 0xFFFFFFFF : p.TextDim;
            float xl = cbR * 0.34f;
            dl.AddLine(new ImVec2(cbC.X - xl, cbC.Y - xl), new ImVec2(cbC.X + xl, cbC.Y + xl), xCol, 2f);
            dl.AddLine(new ImVec2(cbC.X + xl, cbC.Y - xl), new ImVec2(cbC.X - xl, cbC.Y + xl), xCol, 2f);

            // ---- 侧边栏 ----
            float labelFont = fs * 0.95f;
            float maxLabelW = 0f;
            for (int i = 0; i < items.Length; i++)
            {
                float w = ImGuiApi.CalcTextSizeA(labelFont, items[i].Label).X;
                if (w > maxLabelW) maxLabelW = w;
            }
            float sideFullDesign = SideWidthFull();
            float sideFullNeed = 20f * u + 19f * u + 12f * u + maxLabelW + 24f * u;
            float sideFull = Math.Max(sideFullDesign, sideFullNeed);
            float sideMax = ImGuiApi.WindowWidth * 0.26f;
            if (sideFull > sideMax) sideFull = sideMax;
            float sideSmall = SideWidthSmall();
            st.SideAnim = Anim.Approach(st.SideAnim, st.SmallSide ? 1f : 0f, dt, 12f);
            float sideW = sideFull + (sideSmall - sideFull) * st.SideAnim;

            var sbMin = new ImVec2(wMin.X - 2f, hMax.Y);
            var sbMax = new ImVec2(sbMin.X + sideW, wMax.Y);
            dl.AddRectFilledEx(sbMin, sbMax, Fx.Col(19, 19, 27, 170), 14f * u, RoundCornersBot);
            dl.AddRectEx(sbMin, sbMax, p.CardBorder, 14f * u, RoundCornersBot, 1f);

            // 头部渐变分隔线（贯穿整窗，侧栏之后画）
            dl.AddRectFilledMultiColor(new ImVec2(wMin.X - 2f, hMax.Y - 0.6f), new ImVec2(wMax.X + 2f, hMax.Y + 1.4f),
                                       Fx.Col(139, 124, 255, 40), Fx.Col(139, 124, 255, 150),
                                       Fx.Col(139, 124, 255, 150), Fx.Col(139, 124, 255, 40));

            // 品牌区
            float brandH = 56f * u;
            float mlr = 16f * u;
            bool brandVisible = sideW > sideFull * 0.6f;
            float iconXExp = sbMin.X + 12f * u + mlr;
            float iconXSm = (sbMin.X + sbMax.X) * 0.5f;
            var mlc = new ImVec2(iconXExp + (iconXSm - iconXExp) * st.SideAnim, sbMin.Y + 14f * u + mlr);
            dl.AddRectFilled(new ImVec2(mlc.X - mlr, mlc.Y - mlr), new ImVec2(mlc.X + mlr, mlc.Y + mlr), p.Accent, 10f * u);
            {
                float dh = mlr * 0.80f, dw = mlr * 0.64f;
                dl.AddQuadFilled(new ImVec2(mlc.X, mlc.Y - dh), new ImVec2(mlc.X + dw, mlc.Y),
                                 new ImVec2(mlc.X, mlc.Y + dh), new ImVec2(mlc.X - dw, mlc.Y), 0xFFFFFFFF);
            }
            if (brandVisible)
            {
                float bTitleFont = fs * 0.90f;
                float textX = mlc.X + mlr + 10f * u;
                var bts = ImGuiApi.CalcTextSizeA(bTitleFont, "功能");
                dl.AddTextFont(new ImVec2(textX, mlc.Y - bts.Y * 0.5f), p.Text, "功能", bTitleFont);
                // 折叠按钮
                float ccSz = 24f * u;
                var ccC = new ImVec2(sbMax.X - 12f * u - ccSz * 0.5f, mlc.Y);
                ImGuiApi.SetCursorScreenPos(new ImVec2(ccC.X - ccSz * 0.5f, ccC.Y - ccSz * 0.5f));
                ImGuiApi.InvisibleButton("##sideCollapse", ccSz, ccSz);
                bool ccHover = ImGuiApi.IsItemHovered();
                if (ImGuiApi.IsItemClicked()) st.SmallSide = true;
                dl.AddRectFilled(new ImVec2(ccC.X - ccSz * 0.5f, ccC.Y - ccSz * 0.5f),
                                 new ImVec2(ccC.X + ccSz * 0.5f, ccC.Y + ccSz * 0.5f),
                                 ccHover ? Fx.Col(139, 124, 255, 46) : Fx.Col(255, 255, 255, 13), 8f * u);
                dl.AddRect(new ImVec2(ccC.X - ccSz * 0.5f, ccC.Y - ccSz * 0.5f),
                           new ImVec2(ccC.X + ccSz * 0.5f, ccC.Y + ccSz * 0.5f),
                           ccHover ? p.Accent : p.CardBorder, 8f * u, 1f);
                float chev = 3.6f * u;
                uint ccCol = ccHover ? 0xFFFFFFFF : p.TextDim;
                dl.AddLine(new ImVec2(ccC.X + chev, ccC.Y - chev), new ImVec2(ccC.X - chev, ccC.Y), ccCol, 1.6f);
                dl.AddLine(new ImVec2(ccC.X - chev, ccC.Y), new ImVec2(ccC.X + chev, ccC.Y + chev), ccCol, 1.6f);
            }
            else
            {
                // 收起状态：点击品牌图标展开
                ImGuiApi.SetCursorScreenPos(new ImVec2(mlc.X - mlr, mlc.Y - mlr));
                ImGuiApi.InvisibleButton("##sideExpand", mlr * 2f, mlr * 2f);
                bool expHover = ImGuiApi.IsItemHovered();
                if (ImGuiApi.IsItemClicked()) st.SmallSide = false;
                if (expHover)
                    dl.AddRect(new ImVec2(mlc.X - mlr, mlc.Y - mlr), new ImVec2(mlc.X + mlr, mlc.Y + mlr),
                               0xFFFFFFFF, 10f * u, 1.6f);
            }

            // ---- 导航项（弹簧指示器 + 图标 + 标签）----
            float navTop = sbMin.Y + brandH + 10f * u;
            float navBottom = wMax.Y - 6f * u;
            float navH = navBottom - navTop;
            float itemH = Math.Max(46f * u, fs * 1.5f);
            float itemGap = 3f * u;
            float step = itemH + itemGap;
            float totalNav = items.Length * step;
            float maxScroll = totalNav - navH;
            if (ImGuiApi.IsMouseHoveringRect(sbMin, new ImVec2(sbMax.X, navBottom)))
                st.NavScroll -= ImGuiApi.MouseWheelY * fs * 1.4f;
            if (st.NavScroll < 0) st.NavScroll = 0;
            if (st.NavScroll > maxScroll) st.NavScroll = maxScroll > 0 ? maxScroll : 0;
            if (st.HoverAnim.Length != items.Length) st.HoverAnim = new float[items.Length];

            float indX = sbMin.X + 8f * u;
            float indW = sbMax.X - sbMin.X - 16f * u;
            float pillH = itemH - 6f * u;
            float targetY = navTop + st.Active * step - st.NavScroll + (itemH - pillH) * 0.5f;
            // 弹簧指示器：独立状态朝目标项平滑移动（二阶弹簧：刚度 300 / 阻尼 30）
            st.IndicatorY = SpringPos(st.IndicatorY, ref st.IndicatorVel, targetY, dt, 300f, 30f);
            float indY = Math.Clamp(st.IndicatorY, navTop - pillH, navBottom);

            // 选中指示器（胶囊 + 左边条）
            dl.AddRectFilled(new ImVec2(indX, indY), new ImVec2(indX + indW, indY + pillH),
                             Fx.WithAlpha(p.Accent, 0.20f), 11f * u);
            dl.AddRect(new ImVec2(indX, indY), new ImVec2(indX + indW, indY + pillH), p.Accent, 11f * u, 1.2f);
            dl.AddRectFilled(new ImVec2(indX, indY + 8f * u), new ImVec2(indX + 3f * u, indY + pillH - 8f * u),
                             p.Accent, 1.5f * u);

            int clicked = -1;
            for (int i = 0; i < items.Length; i++)
            {
                float y0 = navTop + i * step - st.NavScroll;
                if (y0 + itemH < navTop || y0 > navBottom) continue;
                float y1 = y0 + itemH;
                bool sel = i == st.Active;
                ImGuiApi.SetCursorScreenPos(new ImVec2(indX, y0));
                ImGuiApi.PushID(i);
                ImGuiApi.InvisibleButton("##nav", indW, itemH);
                ImGuiApi.PopID();
                if (ImGuiApi.IsItemClicked()) clicked = i;
                bool hovered = ImGuiApi.IsItemHovered();
                st.HoverAnim[i] = Anim.Approach(st.HoverAnim[i], hovered ? 1f : 0f, dt, 14f);
                if (st.HoverAnim[i] > 0.01f)
                {
                    float barH = itemH * 0.55f * st.HoverAnim[i];
                    dl.AddRectFilled(new ImVec2(indX, y0 + (itemH - barH) * 0.5f),
                                     new ImVec2(indX + 3f * u, y0 + (itemH + barH) * 0.5f), p.Accent, 1.5f * u);
                }
                float icy = (y0 + y1) * 0.5f;
                uint icCol = sel ? Fx.Col(185, 175, 255, 255) : (hovered ? p.Text : p.TextDim);
                SideIcon(dl, items[i].Icon, new ImVec2(indX + 20f * u, icy), 19f * u, icCol, 1.7f * u);
                if (sideW > sideFull * 0.6f)
                {
                    var ts = ImGuiApi.CalcTextSizeA(labelFont, items[i].Label);
                    dl.AddTextFont(new ImVec2(indX + 38f * u, icy - ts.Y * 0.5f),
                                   sel ? p.Text : (hovered ? p.Text : p.TextDim), items[i].Label, labelFont);
                }
            }
            if (clicked >= 0 && clicked != st.Active)
            {
                st.TabAnim = 0f;
                st.SlideDir = clicked > st.Active ? 1f : -1f;
                st.Burst.Emit(new ImVec2(indX + indW * 0.5f, navTop + clicked * step - st.NavScroll + itemH * 0.5f),
                              p.Accent, 12);
                st.Active = clicked;
            }

            // ---- 内容区几何 ----
            st.CMin = new ImVec2(sbMax.X + Gap(), hMax.Y + 16f * u);
            st.CSize = new ImVec2(fxMax.X - st.CMin.X, (wMax.Y - fxPad) - st.CMin.Y);
            if (st.CSize.X < 40f * u) st.CSize.X = 40f * u;
            if (st.CSize.Y < 40f * u) st.CSize.Y = 40f * u;

            // ---- 前景特效层（内容之上再画一层，动画"穿过"面板）----
            st.Aurora.Draw(dl, wMin, wMax, 0.45f, winRad);
            st.Beams.Draw(dl, wMin, wMax, p.Accent, 0.34f);
            st.Floats.Draw(dl, wMin, wMax, 0.55f);

            return clicked;
        }

        /// <summary>内容区开始（页面切换：淡入 + 轻微滑动）。</summary>
        public static void BeginContent(ChromeState st)
        {
            ImGuiApi.SetCursorScreenPos(st.CMin);
            ImGuiApi.BeginChild("##content", st.CSize.X, st.CSize.Y, 0, 0);
            float dt = Glass.Dt();
            st.TabAnim += (1f - st.TabAnim) * (1f - (float)Math.Exp(-dt * 12f));
            ImGuiApi.PushStyleVar((int)Pylon.ImGui.ImGuiStyleVar.Alpha, st.TabAnim);
            float slide = (1f - st.TabAnim) * 20f * st.SlideDir;
            ImGuiApi.SetCursorPos(ImGuiApi.GetCursorPosX() + slide, ImGuiApi.GetCursorPosY());
        }

        public static void EndContent()
        {
            ImGuiApi.PopStyleVar(1);
            ImGuiApi.Dummy(0f, 0f);   // 撑开父内容边界（防 SetCursorPos 越界断言）
            ImGuiApi.EndChild();
        }

        /// <summary>页面头：标题 + 副标题（内容区内使用）。</summary>
        public static void PageHeader(string title, string subtitle)
        {
            float fs = ImGuiApi.FontSize;
            var p = GlassPalette.Dark();
            var dl = DrawList.Window;
            var cpos = ImGuiApi.GetCursorScreenPos();
            dl.AddTextFont(new ImVec2(cpos.X + 2f, cpos.Y), p.Text, title, fs * 1.30f);
            if (!string.IsNullOrEmpty(subtitle))
                dl.AddTextFont(new ImVec2(cpos.X + 3f, cpos.Y + fs * 1.45f), p.TextDim, subtitle, fs * 0.82f);
            ImGuiApi.Dummy(0f, fs * 2.45f);
        }

        /// <summary>粒子爆裂（点击/切换时的星火，GlassKit.h ParticleBurst）。</summary>
        public static void DrawBurst(DrawList dl, ParticleBurst burst) => burst.Draw(dl);

        /// <summary>折叠态悬浮球：拖动移动、点击恢复，悬停/拖动带缩放 + 光晕动画；调用方用 NoDecoration|AlwaysAutoResize 包住。返回 true = 恢复菜单。</summary>
        public static bool DrawCollapsedBall(ChromeState st, string label)
        {
            var p = GlassPalette.Dark();
            float radius = 44f * (ImGuiApi.FontSize / 45f) * 1.45f;

            ImGuiApi.InvisibleButton("##menuBall", radius * 2, radius * 2);
            bool hovered = ImGuiApi.IsItemHovered();
            bool held = ImGuiApi.IsItemActive();

            if (ImGuiApi.IsItemActivated())
            {
                st.PressPos = ImGuiApi.MousePos;
                st.BallGrabOffset = new ImVec2(ImGuiApi.GetWindowPos().X - ImGuiApi.MousePos.X,
                                               ImGuiApi.GetWindowPos().Y - ImGuiApi.MousePos.Y);
                st.BallDragging = false;
            }
            if (held && !st.BallDragging)
            {
                float dx = ImGuiApi.MousePos.X - st.PressPos.X;
                float dy = ImGuiApi.MousePos.Y - st.PressPos.Y;
                if (dx * dx + dy * dy > 8f * 8f) st.BallDragging = true;
            }
            if (st.BallDragging && held)
                ImGuiApi.SetWindowPos(ImGuiApi.MousePos.X + st.BallGrabOffset.X, ImGuiApi.MousePos.Y + st.BallGrabOffset.Y);

            bool restore = false;
            if (ImGuiApi.IsItemDeactivated())
            {
                if (!st.BallDragging) restore = true;
                st.BallDragging = false;
            }

            float dt = Glass.Dt();
            float target = st.BallDragging ? 1.14f : (held ? 0.94f : (hovered ? 1.06f : 1f));
            st.BallScale = Anim.Approach(st.BallScale, target, dt, 18f);
            float r = radius * st.BallScale;

            var min = ImGuiApi.GetItemRectMin();
            var max = ImGuiApi.GetItemRectMax();
            var c = new ImVec2((min.X + max.X) * 0.5f, (min.Y + max.Y) * 0.5f);
            uint col = st.BallDragging ? p.AccentHover : (hovered || held ? p.AccentHover : p.Accent);
            float glow = hovered || held ? 0.85f : 0.45f;

            var dl = DrawList.Window;
            dl.AddCircle(c, r + 7f, Fx.WithAlpha(p.Accent, glow * 0.22f), 0, 3f);          // 外圈光晕
            dl.AddCircleFilled(c, r, Fx.WithAlpha(col, 0.90f), 0);                          // 球体
            dl.AddCircle(c, r, Fx.WithAlpha(p.AccentHover, glow), 0, 2f);                   // 描边
            dl.AddCircleFilled(new ImVec2(c.X, c.Y - r * 0.32f), r * 0.62f,                 // 顶部高光
                               Fx.WithAlpha(0xFFFFFFFF, 0.10f), 0);
            // 标签：按球宽自适应缩字体后居中（maxLabelW = 球直径 * 0.72）
            float baseFontSize = ImGuiApi.FontSize;
            var baseTextSize = ImGuiApi.CalcTextSizeA(baseFontSize, label);
            float maxLabelW = radius * 2f * 0.72f;
            float fitFontSize = Math.Min(baseFontSize, (maxLabelW / Math.Max(baseTextSize.X, 1f)) * baseFontSize);
            float textScale = fitFontSize / baseFontSize;
            var textSize = new ImVec2(baseTextSize.X * textScale, baseTextSize.Y * textScale);
            var tp = new ImVec2(c.X - textSize.X * 0.5f, c.Y - textSize.Y * 0.5f);
            dl.AddTextFont(tp, 0xFFFFFFFF, label, fitFontSize);
            return restore;
        }

        // ---- 内部工具 ----

        /// <summary>二阶弹簧追 target（stiff 刚度 / damp 阻尼，帧率无关；vel 为弹簧速度状态）。</summary>
        private static float SpringPos(float cur, ref float vel, float target, float dt, float stiff, float damp)
        {
            if (dt <= 0) return cur;
            float acc = (target - cur) * stiff - vel * damp;
            vel += acc * dt;
            return cur + vel * dt;
        }

        /// <summary>四角星 logo（GlassKit.h DrawSpark）。</summary>
        private static void DrawSpark(DrawList dl, ImVec2 c, float r, uint col)
        {
            float rin = r * 0.30f;
            var pts = new ImVec2[8];
            for (int i = 0; i < 8; i++)
            {
                float ang = -1.5708f + i * 0.7853982f;
                float rad = i % 2 == 0 ? r : rin;
                pts[i] = new ImVec2(c.X + (float)Math.Cos(ang) * rad, c.Y + (float)Math.Sin(ang) * rad);
            }
            dl.AddConvexPolyFilled(pts, col);
        }

        /// <summary>点阵网格（GlassKit.h DrawDotGrid，Chrome 内联版）。</summary>
        private static void DotGridGrid(DrawList dl, ImVec2 min, ImVec2 max, uint dotColor,
                                        float spacing, float cornerRad)
        {
            dl.PushClipRect(min, max, true);
            for (float y = min.Y + spacing * 0.5f; y < max.Y; y += spacing)
            {
                for (float x = min.X + spacing * 0.5f; x < max.X; x += spacing)
                {
                    if (cornerRad > 0.5f)
                    {
                        float cx = -1f, cy = -1f;
                        if (x < min.X + cornerRad && y < min.Y + cornerRad) { cx = min.X + cornerRad; cy = min.Y + cornerRad; }
                        else if (x > max.X - cornerRad && y < min.Y + cornerRad) { cx = max.X - cornerRad; cy = min.Y + cornerRad; }
                        else if (x < min.X + cornerRad && y > max.Y - cornerRad) { cx = min.X + cornerRad; cy = max.Y - cornerRad; }
                        else if (x > max.X - cornerRad && y > max.Y - cornerRad) { cx = max.X - cornerRad; cy = max.Y - cornerRad; }
                        if (cx >= 0f)
                        {
                            float dx = x - cx, dy = y - cy;
                            if (dx * dx + dy * dy > cornerRad * cornerRad) continue;
                        }
                    }
                    dl.AddRectFilled(new ImVec2(x - 0.8f, y - 0.8f), new ImVec2(x + 0.8f, y + 0.8f), dotColor);
                }
            }
            dl.PopClipRect();
        }

        /// <summary>侧边栏图标（GlassKit.h SideIcon 简化：kind 0..9 用简单几何画）。</summary>
        private static void SideIcon(DrawList dl, int kind, ImVec2 c, float s, uint col, float th)
        {
            switch (kind)
            {
                case 0:   // 用户（圆 + 肩）
                    dl.AddCircleFilled(new ImVec2(c.X, c.Y - s * 0.25f), s * 0.32f, col, 0);
                    dl.AddCircleFilled(new ImVec2(c.X, c.Y + s * 0.5f), s * 0.55f, col, 0);
                    break;
                case 1:   // 地球（圆 + 横线）
                    dl.AddCircle(c, s * 0.55f, col, 0, th);
                    dl.AddLine(new ImVec2(c.X - s * 0.55f, c.Y), new ImVec2(c.X + s * 0.55f, c.Y), col, th);
                    break;
                case 4:   // 盒子（方）
                    dl.AddRect(new ImVec2(c.X - s * 0.45f, c.Y - s * 0.4f), new ImVec2(c.X + s * 0.45f, c.Y + s * 0.4f),
                               col, 2f, th);
                    break;
                case 5:   // 星火（四角星）
                    DrawSpark(dl, c, s * 0.5f, col);
                    break;
                case 6:   // WiFi（三条弧线 → 简化三角）
                    dl.AddLine(new ImVec2(c.X - s * 0.5f, c.Y - s * 0.15f), new ImVec2(c.X, c.Y + s * 0.4f), col, th);
                    dl.AddLine(new ImVec2(c.X, c.Y + s * 0.4f), new ImVec2(c.X + s * 0.5f, c.Y - s * 0.15f), col, th);
                    break;
                case 7:   // 齿轮（圆 + 圆环）
                    dl.AddCircle(c, s * 0.4f, col, 0, th);
                    dl.AddCircle(c, s * 0.18f, col, 0, th);
                    break;
                case 8:   // 眼睛（椭圆 + 点）
                    dl.AddEllipseFilled(c, new ImVec2(s * 0.5f, s * 0.35f), col, 0, 0);
                    dl.AddCircleFilled(c, s * 0.12f, Fx.WithAlpha(col, 0f) | 0xFF000000, 0);
                    break;
                default:  // 点
                    dl.AddCircleFilled(c, s * 0.25f, col, 0);
                    break;
            }
        }
    }

    /// <summary>粒子爆裂（GlassKit.h ParticleBurst 完整移植）。</summary>
    public class ParticleBurst
    {
        private struct P
        {
            public float X, Y, Vx, Vy;
            public float Life, MaxLife, Size;
            public uint Col;
            public bool Alive;
        }
        private const int kMax = 120;
        private readonly P[] _parts = new P[kMax];
        private int _aliveCount;

        public bool Active => _aliveCount > 0;

        public void Emit(ImVec2 origin, uint color, int count = 40)
        {
            for (int n = 0; n < count && _aliveCount < kMax; n++)
            {
                int idx = -1;
                for (int i = 0; i < kMax; i++) if (!_parts[i].Alive) { idx = i; break; }
                if (idx < 0) break;
                float ang = Fx.RndRange(0f, 6.2831853f);
                float spd = Fx.RndRange(60, 260);
                _parts[idx] = new P
                {
                    X = origin.X, Y = origin.Y,
                    Vx = (float)Math.Cos(ang) * spd, Vy = (float)Math.Sin(ang) * spd,
                    MaxLife = Fx.RndRange(0.4f, 0.9f), Life = Fx.RndRange(0.4f, 0.9f),
                    Size = Fx.RndRange(2f, 5f), Col = color, Alive = true,
                };
                _aliveCount++;
            }
        }

        public void Update(float dt)
        {
            for (int i = 0; i < kMax; i++)
            {
                var p = _parts[i];
                if (!p.Alive) continue;
                p.Vx *= 1f - 3f * dt;
                p.Vy = p.Vy * (1f - 3f * dt) + 120f * dt;
                p.X += p.Vx * dt;
                p.Y += p.Vy * dt;
                p.Life -= dt;
                if (p.Life <= 0f) { p.Alive = false; _aliveCount--; }
                _parts[i] = p;
            }
        }

        public void Draw(DrawList dl)
        {
            for (int i = 0; i < kMax; i++)
            {
                var p = _parts[i];
                if (!p.Alive) continue;
                float k = p.Life / p.MaxLife;
                float s = p.Size * (0.5f + 0.5f * k);
                dl.AddRectFilled(new ImVec2(p.X - s, p.Y - s), new ImVec2(p.X + s, p.Y + s),
                                 Fx.WithAlpha(p.Col, k), 1.5f);
            }
        }
    }
}