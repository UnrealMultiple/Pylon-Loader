using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;

namespace Pylon.ModLoader.Effects;

/// <summary>屏幕效果的作者工具：世界↔UV 换算、缓动、遮罩、补间、曲线、颜色换算。</summary>
public static class ScreenUtils
{
    /// <summary>世界坐标 ↔ 屏幕 UV。UV 原点左下（v=0 在底部），和 shader 的 vUV 一致。</summary>
    public static class Space
    {
        private static int _frame = -1;
        private static float _x, _y, _width = 1, _height = 1;

        internal static void BeginFrame(int at)
        {
            _frame = at;
            Vector2 position = Main.screenPosition;
            _x = position.X;
            _y = position.Y;
            _width = Main.screenWidth > 0 ? Main.screenWidth : 1;
            _height = Main.screenHeight > 0 ? Main.screenHeight : 1;
        }

        private static void Fresh()
        {
            if (_frame != ScreenEffects.Frame) BeginFrame(ScreenEffects.Frame);
        }

        public static Vector2 WorldToUV(Vector2 position)
        {
            Fresh();
            return new Vector2((position.X - _x) / _width, 1f - (position.Y - _y) / _height);
        }

        public static Vector2 UVToWorld(Vector2 uv)
        {
            Fresh();
            return new Vector2(_x + uv.X * _width, _y + (1f - uv.Y) * _height);
        }

        public static float PixelsToUV(float pixels)
        {
            Fresh();
            return pixels / _height;
        }

        public static float UVToPixels(float fraction)
        {
            Fresh();
            return fraction * _height;
        }

        public static float Aspect
        {
            get
            {
                Fresh();
                return _width / _height;
            }
        }

        public static Vector2 Size
        {
            get
            {
                Fresh();
                return new Vector2(_width, _height);
            }
        }

        public static bool IsOnScreen(Vector2 position, float margin = 0)
        {
            Fresh();
            float px = position.X - _x, py = position.Y - _y;
            return px >= -margin && py >= -margin && px <= _width + margin && py <= _height + margin;
        }
    }

    public static class Easing
    {
        public static readonly Func<float, float> Linear = t => t;
        public static readonly Func<float, float> InSine = t => 1 - MathF.Cos(t * MathF.PI / 2);
        public static readonly Func<float, float> OutSine = t => MathF.Sin(t * MathF.PI / 2);
        public static readonly Func<float, float> InOutSine = t => -(MathF.Cos(MathF.PI * t) - 1) / 2;
        public static readonly Func<float, float> InQuad = t => t * t;
        public static readonly Func<float, float> OutQuad = t => 1 - (1 - t) * (1 - t);
        public static readonly Func<float, float> InOutQuad = t => t < 0.5f ? 2 * t * t : 1 - MathF.Pow(-2 * t + 2, 2) / 2;
        public static readonly Func<float, float> InCubic = t => t * t * t;
        public static readonly Func<float, float> OutCubic = t => 1 - MathF.Pow(1 - t, 3);
        public static readonly Func<float, float> InOutCubic = t => t < 0.5f ? 4 * t * t * t : 1 - MathF.Pow(-2 * t + 2, 3) / 2;
        public static readonly Func<float, float> InExpo = t => t == 0 ? 0 : MathF.Pow(2, 10 * t - 10);
        public static readonly Func<float, float> OutExpo = t => t == 1 ? 1 : 1 - MathF.Pow(2, -10 * t);
        public static readonly Func<float, float> InOutExpo = t => t == 0 ? 0 : t == 1 ? 1 : t < 0.5f ? MathF.Pow(2, 20 * t - 10) / 2 : (2 - MathF.Pow(2, -20 * t + 10)) / 2;
        public static readonly Func<float, float> InBack = t => 2.70158f * t * t * t - 1.70158f * t * t;
        public static readonly Func<float, float> OutBack = t => 1 + 2.70158f * MathF.Pow(t - 1, 3) + 1.70158f * MathF.Pow(t - 1, 2);
        public static readonly Func<float, float> OutElastic = t => t == 0 ? 0 : t == 1 ? 1 : MathF.Pow(2, -10 * t) * MathF.Sin((t * 10 - 0.75f) * (2 * MathF.PI / 3)) + 1;

        public static readonly Func<float, float> OutBounce = t =>
        {
            const float n = 7.5625f, d = 2.75f;
            if (t < 1 / d) return n * t * t;
            if (t < 2 / d) return n * (t -= 1.5f / d) * t + 0.75f;
            if (t < 2.5f / d) return n * (t -= 2.25f / d) * t + 0.9375f;
            return n * (t -= 2.625f / d) * t + 0.984375f;
        };
    }

    public sealed class Mask
    {
        public enum Falloff
        {
            Linear = 0,
            Smooth = 1,
            Exponential = 2,
        }

        public enum Direction
        {
            Up,
            Down,
            Left,
            Right,
        }

        private readonly int _type;
        private readonly bool _inverted;
        private readonly Falloff _falloff;
        private readonly Vector2 _center;
        private readonly float _radius, _width, _feather, _start, _end, _softness, _roundness;
        private readonly Rectangle _rect;
        private readonly Vector2 _direction;
        private readonly ScreenGraphics.Target? _texture;
        private readonly bool _luma;

        private Mask(int type, Falloff falloff, bool inverted = false, Vector2 center = default, float radius = 0, float width = 0, float feather = 0,
            Rectangle rect = default, Vector2 direction = default, float start = 0, float end = 0, float softness = 0, float roundness = 0,
            ScreenGraphics.Target? texture = null, bool luma = false)
        {
            _type = type;
            _falloff = falloff;
            _inverted = inverted;
            _center = center;
            _radius = radius;
            _width = width;
            _feather = feather;
            _rect = rect;
            _direction = direction;
            _start = start;
            _end = end;
            _softness = softness;
            _roundness = roundness;
            _texture = texture;
            _luma = luma;
        }

        public static Mask None() => new(0, Falloff.Smooth);

        public static Mask Circle(Vector2 center, float radius, float? feather = null, Falloff falloff = Falloff.Smooth) =>
            new(1, falloff, center: center, radius: radius, feather: feather ?? radius * 0.25f);

        public static Mask Ring(Vector2 center, float radius, float width, float? feather = null, Falloff falloff = Falloff.Smooth) =>
            new(2, falloff, center: center, radius: radius, width: width, feather: feather ?? width * 0.5f);

        public static Mask Rect(Rectangle rect, float feather = 16, Falloff falloff = Falloff.Smooth) =>
            new(3, falloff, rect: rect, feather: feather);

        public static Mask Gradient(Direction direction = Direction.Up, float start = 0, float end = 1, Falloff falloff = Falloff.Smooth) =>
            Gradient(direction switch
            {
                Direction.Down => new Vector2(0, -1),
                Direction.Left => new Vector2(-1, 0),
                Direction.Right => new Vector2(1, 0),
                _ => new Vector2(0, 1),
            }, start, end, falloff);

        public static Mask Gradient(Vector2 direction, float start = 0, float end = 1, Falloff falloff = Falloff.Smooth)
        {
            float length = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            if (length <= 0) length = 1;
            return new Mask(4, falloff, direction: direction / length, start: start, end: end);
        }

        public static Mask Vignette(float start = 0.45f, float softness = 0.55f, float roundness = 1, Falloff falloff = Falloff.Smooth) =>
            new(5, falloff, start: start, softness: softness, roundness: roundness);

        public static Mask Texture(ScreenGraphics.Target texture, bool luma = false, Falloff falloff = Falloff.Smooth) =>
            new(6, falloff, texture: texture ?? throw new ArgumentNullException(nameof(texture)), luma: luma);

        public Mask Inverted() =>
            new(_type, _falloff, !_inverted, _center, _radius, _width, _feather, _rect, _direction, _start, _end, _softness, _roundness, _texture, _luma);

        public ScreenGraphics.Target? TextureSource => _type == 6 ? _texture : null;

        public Dictionary<string, object> ToUniforms()
        {
            float[] a = { 0, 0, 0, 0 };
            float[] b = { (int)_falloff, _inverted ? 1 : 0, 0, 0 };
            if (_type == 1 || _type == 2)
            {
                Vector2 uv = Space.WorldToUV(_center);
                float r = Space.PixelsToUV(_radius);
                if (_type == 1)
                {
                    a = new[] { uv.X, uv.Y, r, MathF.Min(Space.PixelsToUV(_feather), r) };
                }
                else
                {
                    a = new[] { uv.X, uv.Y, r, Space.PixelsToUV(_width) };
                    b[2] = Space.PixelsToUV(_feather);
                }
            }
            else if (_type == 3)
            {
                a = RectUV();
                b[2] = Space.PixelsToUV(_feather);
            }
            else if (_type == 4)
            {
                a = new[] { _direction.X, _direction.Y, _start, _end };
            }
            else if (_type == 5)
            {
                a = new[] { _start, _roundness, _softness, 0f };
            }
            else if (_type == 6)
            {
                b[2] = _luma ? 1 : 0;
            }
            return new Dictionary<string, object> { ["uMaskType"] = (float)_type, ["uMaskA"] = a, ["uMaskB"] = b };
        }

        public float[]? Bounds()
        {
            if (_inverted) return null;
            if (_type == 1 || _type == 2)
            {
                Vector2 uv = Space.WorldToUV(_center);
                float r = Space.PixelsToUV(_radius + (_type == 2 ? _width / 2 + _feather : 0));
                float rx = r / Space.Aspect;
                return new[] { uv.X - rx, uv.Y - r, uv.X + rx, uv.Y + r };
            }
            if (_type == 3)
            {
                float f = Space.PixelsToUV(_feather);
                float[] u = RectUV();
                return new[] { u[0] - f, u[1] - f, u[2] + f, u[3] + f };
            }
            return null;
        }

        private float[] RectUV()
        {
            Vector2 lo = Space.WorldToUV(new Vector2(_rect.X, _rect.Y + _rect.Height));
            Vector2 hi = Space.WorldToUV(new Vector2(_rect.X + _rect.Width, _rect.Y));
            return new[] { lo.X, lo.Y, hi.X, hi.Y };
        }
    }

    public sealed class Tween
    {
        private static List<Tween> _active = [];

        private bool _started, _killed;
        private readonly Func<float> _get;
        private readonly Action<float> _set;

        public object Target { get; }

        public string Property { get; }

        public float Start { get; private set; }

        public float End { get; }

        public float Duration { get; }

        public Func<float, float> Easing { get; }

        public float Delay { get; }

        public float Age { get; private set; }

        public Action<object>? OnDone { get; }

        private Tween(object target, string property, Func<float> get, Action<float> set, float to, float duration,
            Func<float, float>? easing, float delay, Action<object>? onDone)
        {
            Target = target;
            Property = property;
            _get = get;
            _set = set;
            End = to;
            Duration = MathF.Max(duration, 0);
            Easing = easing ?? ScreenUtils.Easing.Linear;
            Delay = MathF.Max(delay, 0);
            OnDone = onDone;
        }

        public static Tween To(object target, string property, float to, float duration, Func<float, float>? easing = null,
            float delay = 0, Action<object>? onDone = null)
        {
            ArgumentNullException.ThrowIfNull(target);
            var member = target.GetType().GetProperty(property);
            Func<float> get;
            Action<float> set;
            if (member != null && member.CanRead && member.CanWrite && IsNumber(member.PropertyType))
            {
                get = () => Convert.ToSingle(member.GetValue(target));
                set = v => member.SetValue(target, Convert.ChangeType(v, member.PropertyType));
            }
            else if (target.GetType().GetField(property) is { IsInitOnly: false } field && IsNumber(field.FieldType))
            {
                get = () => Convert.ToSingle(field.GetValue(target));
                set = v => field.SetValue(target, Convert.ChangeType(v, field.FieldType));
            }
            else
            {
                throw new ArgumentException($"Tween: {property} 不是 {target.GetType().Name} 的可写数值成员");
            }
            return Add(new Tween(target, property, get, set, to, duration, easing, delay, onDone));
        }

        public static Tween To(object target, Func<float> get, Action<float> set, float to, float duration,
            Func<float, float>? easing = null, float delay = 0, Action<object>? onDone = null) =>
            Add(new Tween(target, "", get, set, to, duration, easing, delay, onDone));

        private static Tween Add(Tween tween)
        {
            if (tween.Property.Length > 0)
            {
                foreach (Tween t in _active)
                    if (ReferenceEquals(t.Target, tween.Target) && t.Property == tween.Property) t.Kill();
            }
            _active.Add(tween);
            ScreenEffects.Install();
            return tween;
        }

        private static bool IsNumber(Type type) => type == typeof(float) || type == typeof(double) || type == typeof(int);

        public static int Count => _active.Count;

        public bool Done => _killed;

        public void Kill() => _killed = true;

        internal static void Update(float delta)
        {
            if (_active.Count == 0) return;
            foreach (Tween t in _active.ToArray())
            {
                if (t._killed) continue;
                try { t.Step(delta); }
                catch (Exception e)
                {
                    ScreenReport.Report($"Tween {t.Property}", e);
                    t.Kill();
                }
            }
            _active = _active.Where(t => !t._killed).ToList();
        }

        private void Step(float delta)
        {
            if (Target is ModScreenEffect fx && !fx.Active)
            {
                Kill();
                return;
            }
            Age += delta;
            float time = Age - Delay;
            if (time < 0) return;
            if (!_started)
            {
                _started = true;
                Start = _get();
            }
            float t = Duration > 0 ? MathF.Min(time / Duration, 1) : 1;
            _set(Start + (End - Start) * Easing(t));
            if (t >= 1)
            {
                Kill();
                OnDone?.Invoke(Target);
            }
        }
    }

    public sealed class Curve
    {
        private readonly (float T, float Value)[] _points;
        private readonly Func<float, float> _easing;

        public Curve(IEnumerable<(float T, float Value)> points, Func<float, float>? easing = null)
        {
            _points = points.OrderBy(p => p.T).ToArray();
            if (_points.Length == 0) throw new ArgumentException("Curve: 至少给一个 (t, value) 点");
            _easing = easing ?? Easing.Linear;
        }

        public float At(float t)
        {
            if (t <= _points[0].T) return _points[0].Value;
            for (int i = 1; i < _points.Length; i++)
            {
                (float t1, float v1) = _points[i];
                if (t > t1) continue;
                (float t0, float v0) = _points[i - 1];
                float local = t1 > t0 ? (t - t0) / (t1 - t0) : 1;
                return v0 + (v1 - v0) * _easing(local);
            }
            return _points[^1].Value;
        }
    }

    public static class ColorMath
    {
        public static float[] ToFloats(Color color) => new[] { color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f };

        public static Color FromHex(string hex)
        {
            string h = hex.TrimStart('#');
            if ((h.Length != 6 && h.Length != 8) || !h.All(Uri.IsHexDigit))
                throw new FormatException("ColorMath.FromHex: 需要 #rrggbb 或 #rrggbbaa");
            int Byte(int i) => Convert.ToInt32(h.Substring(i, 2), 16);
            return new Color(Byte(0), Byte(2), Byte(4), h.Length == 8 ? Byte(6) : (byte)255);
        }

        public static Color Hsv(float h, float s, float v, float a = 1)
        {
            float hue = ((h % 360 + 360) % 360) / 60;
            float F(float n)
            {
                float k = (n + hue) % 6;
                return v - v * s * MathF.Max(MathF.Min(MathF.Min(k, 4 - k), 1), 0);
            }
            return new Color(F(5), F(3), F(1), a);
        }

        public static Color Lerp(Color a, Color b, float t) =>
            new((a.R + (b.R - a.R) * t) / 255f, (a.G + (b.G - a.G) * t) / 255f,
                (a.B + (b.B - a.B) * t) / 255f, (a.A + (b.A - a.A) * t) / 255f);

        public static Color Temperature(float kelvin)
        {
            float t = Math.Clamp(kelvin, 1000, 40000) / 100;
            static float Clamp(double v) => (float)(Math.Clamp(v, 0, 255) / 255);
            double r = t <= 66 ? 255 : 329.698727446 * Math.Pow(t - 60, -0.1332047592);
            double g = t <= 66 ? 99.4708025861 * Math.Log(t) - 161.1195681661 : 288.1221695283 * Math.Pow(t - 60, -0.0755148492);
            double b = t >= 66 ? 255 : t <= 19 ? 0 : 138.5177312231 * Math.Log(t - 10) - 305.0447927307;
            return new Color(Clamp(r), Clamp(g), Clamp(b), 1f);
        }

        public static float Luma(Color color) => (0.2126f * color.R + 0.7152f * color.G + 0.0722f * color.B) / 255f;
    }
}