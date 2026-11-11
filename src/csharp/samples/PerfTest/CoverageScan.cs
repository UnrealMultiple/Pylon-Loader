using System.Diagnostics;
using System.Reflection;
using System.Text;
using Il2CppInterop.Runtime;

namespace PerfTest;

// Counts metadata ABI plans, NOT proxy entry coverage. Never runs proxy cctors/game methods.
internal sealed unsafe class CoverageScan
{
    private readonly MethodInfo _build = typeof(GuardedInvoke).GetMethod("Build", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new NotSupportedException("GuardedInvoke.Build unavailable");
    private readonly List<Row> _images = [];
    private int _image;
    private uint _classIndex;
    private IntPtr _klass, _iterator;
    private readonly object[] _argument = new object[1];
    private int _frames;
    private readonly long _started = Stopwatch.GetTimestamp();
    private sealed class Row(string name, IntPtr image)
    {
        internal readonly string Name = name;
        internal readonly IntPtr Image = image;
        internal uint Classes = IL2CPP.il2cpp_image_get_class_count(image);
        internal int Total, Direct, Fallback, Errors;
    }

    internal CoverageScan()
    {
        uint count = 0;
        IntPtr* assemblies = IL2CPP.il2cpp_domain_get_assemblies(IL2CPP.il2cpp_domain_get(), ref count);
        for (int i = 0; i < count; i++)
        {
            IntPtr image = IL2CPP.il2cpp_assembly_get_image(assemblies[i]);
            string name = IL2CPP.il2cpp_image_get_name_(image) ?? "?";
            if (name == "Assembly-CSharp.dll" || name.StartsWith("Unity", StringComparison.Ordinal))
                _images.Add(new Row(name, image));
        }
    }

    internal bool Step(double milliseconds)
    {
        if (++_frames > 3600) throw new TimeoutException("Coverage exceeded 3600 frames; cancelled without a complete count");
        long deadline = Stopwatch.GetTimestamp() + (long)(Stopwatch.Frequency * milliseconds / 1000);
        int visits = 0;
        do
        {
            if (_image >= _images.Count) return true;
            Row row = _images[_image];
            if (_klass == IntPtr.Zero)
            {
                if (_classIndex >= row.Classes)
                {
                    _image++; _classIndex = 0;
                    continue;
                }
                _klass = IL2CPP.il2cpp_image_get_class(row.Image, _classIndex++);
                _iterator = IntPtr.Zero;
                if (_klass == IntPtr.Zero) continue;
            }
            IntPtr method = IL2CPP.il2cpp_class_get_methods(_klass, ref _iterator);
            if (method == IntPtr.Zero) { _klass = IntPtr.Zero; continue; }
            row.Total++;
            try
            {
                _argument[0] = method;
                // Build, not Resolve: do not permanently populate the runtime method-plan cache.
                if (_build.Invoke(null, _argument) == null) row.Fallback++;
                else row.Direct++;
            }
            catch { row.Errors++; }
        } while (++visits < 64 && Stopwatch.GetTimestamp() < deadline);
        return false;
    }

    internal string Report()
    {
        var text = new StringBuilder();
        text.AppendLine($"ABI metadata planning coverage {DateTimeOffset.Now:O}; frames={_frames}; elapsed={(Stopwatch.GetTimestamp() - _started) / (double)Stopwatch.Frequency:F2}s");
        text.AppendLine("SCOPE: Assembly-CSharp + Unity* game metadata; Build!=null vs null. Not proxy-entry intersection, not a promise every method is callable. No methods executed; no proxy cctors.");
        text.AppendLine("Build counts may include constructors/generic/abstract methods excluded by the generator. This is ABI-planner coverage ONLY; use offline proxy IL counts separately.");
        foreach (Row row in _images)
            text.AppendLine($"{row.Name}: metadata={row.Total}, Build-plan={row.Direct}, fallback={row.Fallback}, errors={row.Errors}, ratio={(row.Total == 0 ? 0 : 100d * row.Direct / row.Total):F2}%");
        int total = _images.Sum(x => x.Total), direct = _images.Sum(x => x.Direct);
        text.AppendLine($"TOTAL metadata={total}, Build-plan={direct}, fallback={_images.Sum(x => x.Fallback)}, errors={_images.Sum(x => x.Errors)}, ratio={(total == 0 ? 0 : 100d * direct / total):F2}%");
        return text.ToString();
    }
}
