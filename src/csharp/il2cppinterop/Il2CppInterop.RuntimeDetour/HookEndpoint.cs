#nullable disable
// 层：Hook 引擎 —— 生成程序集（On.*）的运行时端点。
//
// 生成的 On.Terraria.Main.Update 事件只做一件事：把订阅者交给这里。第一次 Add 时解析
// 目标方法并挂上 Hook，最后一个 Remove 时撤销。事件本身是强类型委托，Mod 作者不用
// 写反射字符串，也不用手工管理 Hook 的生命周期。
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Il2CppInterop.RuntimeDetour
{
    /// <summary>一个生成钩子（On.X.Y）对应的运行时端点。</summary>
    public sealed class HookEndpoint
    {
        private readonly Type _declaringType;
        private readonly string _methodName;
        private readonly Type[] _parameterTypes;
        private readonly Type _handlerType;
        private readonly List<Entry> _entries = new();
        private readonly object _gate = new();
        private MethodInfo _target;
        private Type[] _typeArgs;

        private struct Entry
        {
            public Delegate Handler;
            public Hook Hook;
        }

        private HookEndpoint(Type declaringType, string methodName, Type[] parameterTypes, Type handlerType)
        {
            _declaringType = declaringType;
            _methodName = methodName;
            _parameterTypes = parameterTypes;
            _handlerType = handlerType;
        }

        /// <summary>生成程序集用：绑定一个 On.X.Y 到具体方法（参数类型精确匹配）。</summary>
        public static HookEndpoint Bind(Type declaringType, string methodName, Type[] parameterTypes,
                                        Type origType, Type handlerType)
        {
            if (declaringType == null) throw new ArgumentNullException(nameof(declaringType));
            if (methodName == null) throw new ArgumentNullException(nameof(methodName));
            if (handlerType == null) throw new ArgumentNullException(nameof(handlerType));
            var endpoint = new HookEndpoint(declaringType, methodName, parameterTypes ?? Type.EmptyTypes, handlerType);
            endpoint._target = Resolve(declaringType, methodName, endpoint._parameterTypes);
            return endpoint;
        }

        /// <summary>
        /// 生成程序集用：绑定泛型（泛型类型上的方法，或泛型方法）的 On.X.Y。
        /// <paramref name="typeArgs"/> 是闭合成游戏类型的泛型参数（如 typeof(On.X&lt;T&gt;) 里的 T）。
        /// 要 hook 的 MethodInfo 是 <paramref name="declaringType"/>.MakeGenericType(typeArgs) 后按名解析的；
        /// 解析不到时保留开放定义，等 Add 收到具体委托后再实例化。
        /// </summary>
        public static HookEndpoint Bind(Type declaringType, string methodName, Type[] parameterTypes,
                                        Type[] typeArgs, Type origType, Type handlerType)
        {
            if (declaringType == null) throw new ArgumentNullException(nameof(declaringType));
            if (methodName == null) throw new ArgumentNullException(nameof(methodName));
            var endpoint = new HookEndpoint(declaringType, methodName, parameterTypes ?? Type.EmptyTypes, handlerType);
            endpoint._typeArgs = typeArgs ?? Type.EmptyTypes;
            endpoint._target = Resolve(declaringType, methodName, endpoint._parameterTypes, endpoint._typeArgs);
            return endpoint;
        }

        /// <summary>
        /// 生成程序集用：第一次需要时创建端点（线程安全的惰性初始化）。
        /// 生成出来的事件 add 访问器直接调它，避免每个端点都写一遍 null 检查。
        /// </summary>
        public static HookEndpoint BindOnce(ref HookEndpoint field, Type declaringType, string methodName,
                                             Type[] parameterTypes, Type origType, Type handlerType)
        {
            return global::System.Threading.LazyInitializer.EnsureInitialized(ref field,
                () => Bind(declaringType, methodName, parameterTypes, origType, handlerType));
        }

        /// <summary>
        /// 生成程序集用：泛型（泛型类型/泛型方法）事件的端点初始化。
        /// <paramref name="typeArgs"/> 是闭合成游戏类型的泛型参数；null/空 与 BindOnce 等价。
        /// </summary>
        public static HookEndpoint BindOnce(ref HookEndpoint field, Type declaringType, string methodName,
                                             Type[] parameterTypes, Type[] typeArgs, Type origType, Type handlerType)
        {
            return global::System.Threading.LazyInitializer.EnsureInitialized(ref field,
                () => Bind(declaringType, methodName, parameterTypes, typeArgs, origType, handlerType));
        }

        /// <summary>标签：Type.Method。</summary>
        public string Label => $"{_declaringType.Name}.{_methodName}";

        /// <summary>解析到的游戏方法。</summary>
        public MethodInfo Target => _target;

        /// <summary>当前订阅者数量。</summary>
        public int Count
        {
            get { lock (_gate) return _entries.Count; }
        }

        public void Add(Delegate handler)
        {
            if (handler == null) return;
            if (!_handlerType.IsInstanceOfType(handler))
            {
                throw new ArgumentException(
                    $"{Label} 需要 {_handlerType.Name} 类型的处理器，实际 {handler.GetType().Name}", nameof(handler));
            }

            lock (_gate)
            {
                var hook = new Hook(_target, handler);
                _entries.Add(new Entry { Handler = handler, Hook = hook });
            }
        }

        public void Remove(Delegate handler)
        {
            if (handler == null) return;
            lock (_gate)
            {
                for (var i = _entries.Count - 1; i >= 0; i--)
                {
                    if (!_entries[i].Handler.Equals(handler)) continue;
                    _entries[i].Hook.Dispose();
                    _entries.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>撤销这个端点上的所有订阅（宿主卸载 / 测试用）。</summary>
        public void UndoAll()
        {
            lock (_gate)
            {
                for (var i = _entries.Count - 1; i >= 0; i--) _entries[i].Hook.Dispose();
                _entries.Clear();
            }
        }

        private static MethodInfo TryMakeGenericMethod(MethodInfo method, Type[] args)
        {
            try { return method.MakeGenericMethod(args); } catch { return null; }
        }

        private static MethodInfo Resolve(Type type, string name, Type[] parameterTypes) =>
            Resolve(type, name, parameterTypes, null);

        private static MethodInfo Resolve(Type type, string name, Type[] parameterTypes, Type[] typeArgs)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var declaringType = typeArgs != null && typeArgs.Length > 0 && type.IsGenericTypeDefinition
                ? type.MakeGenericType(typeArgs)
                : type;

            MethodInfo match = null;
            foreach (var method in declaringType.GetMethods(flags))
            {
                if (method.Name != name) continue;
                var parameters = method.GetParameters();
                if (parameters.Length != parameterTypes.Length) continue;
                var same = true;
                for (var i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i].ParameterType != parameterTypes[i]) { same = false; break; }
                }
                if (!same) continue;
                match = method;
                break;
            }

            if (match == null)
            {
                // 泛型方法：开放定义按名找（参数里有泛型参数，精确比不了）
                foreach (var method in declaringType.GetMethods(flags))
                {
                    if (method.Name != name) continue;
                    if (method.GetParameters().Length != parameterTypes.Length) continue;
                    if (!method.IsGenericMethodDefinition) continue;
                    match = method;
                    break;
                }
            }

            if (match == null)
            {
                throw new MissingMethodException(
                    $"{declaringType.FullName}.{name}({parameterTypes.Length} 参) 在代理程序集里找不到；" +
                    "生成的 On.* 与当前游戏的 interop/generated 不一致？");
            }
            return match;
        }
    }
}
