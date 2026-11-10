using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Text.RegularExpressions;
using Il2CppInterop;
using Il2CppInterop.Common;
using Il2CppInterop.Generator;
using Il2CppInterop.Generator.Runners;
using Il2CppInterop.RuntimeDetour.HookGen;
using Il2CppInterop.StructGenerator;
using Microsoft.Extensions.Logging;

var command = new RootCommand { new Option<bool>("--verbose", "Produce more verbose output") };
command.Description = "Generate Managed<->IL2CPP interop assemblies from Cpp2IL's output.";

var generateCommand = new Command("generate")
{
    new Option<DirectoryInfo>("--input", "Directory with Il2CppDumper's dummy assemblies") {IsRequired = true}
        .ExistingOnly(),
    new Option<DirectoryInfo>("--output", "Directory to write generated assemblies to") {IsRequired = true},
    new Option<DirectoryInfo>("--unity", "Directory with original Unity assemblies for unstripping").ExistingOnly(),
    new Option<FileInfo>("--game-assembly", "Path to GameAssembly.dll. Used for certain analyses").ExistingOnly(),
    new Option<bool>("--no-xref-cache", "Don't generate xref scanning cache. All scanning will be done at runtime."),
    new Option<string[]>("--add-prefix-to",
        "Assemblies and namespaces starting with these will get an Il2Cpp prefix in generated assemblies. Allows multiple values. Obsolete."),
    new Option<string[]>("--dont-add-prefix-to",
        "Assemblies and namespaces starting with these will not get an Il2Cpp prefix in generated assemblies. Allows multiple values."),
    new Option<bool>("--use-opt-out-prefixing",
        "Assemblies and namespaces will get an Il2Cpp prefix in generated assemblies unless otherwise specified. Obsolete."),
    new Option<FileInfo>("--deobf-map",
        "Specifies a file specifying deobfuscation map for obfuscated types and members.").ExistingOnly(),
    new Option<int>("--deobf-uniq-chars", "How many characters per unique token to use during deobfuscation"),
    new Option<int>("--deobf-uniq-max", "How many maximum unique tokens per type are allowed during deobfuscation"),
    new Option<string[]>("--blacklist-assembly", "Don't write specified assembly to output. Allows multiple values."),
    new Option<Regex>("--obf-regex",
        "Specifies a regex for obfuscated names. All types and members matching will be renamed."),
    new Option<bool>("--passthrough-names",
        "If specified, names will be copied from input assemblies as-is without renaming or deobfuscation."),
    new Option<bool>("--no-parallel", "Disable parallel processing when writing assemblies. Use if you encounter stability issues when generating assemblies."),
    new Option<bool>("--direct-invoke",
        "Emit protected direct-call bodies (Pylon GuardedCall + methodPointer) instead of il2cpp_runtime_invoke for ABI-compatible signatures."),
};
generateCommand.Description = "Generate wrapper assemblies that can be used to interop with Il2Cpp";
generateCommand.Handler = CommandHandler.Create((GenerateCommandOptions opts) =>
{
    var buildResult = opts.Build();
    Il2CppInteropGenerator.Create(buildResult.Options)
        .AddLogger(buildResult.Logger)
        .AddInteropAssemblyGenerator()
        .Run();
});

var hookgenCommand = new Command("hookgen")
{
    new Option<DirectoryInfo>("--input", "Directory with generated proxy assemblies") {IsRequired = true}.ExistingOnly(),
    new Option<string>("--assembly", () => "Assembly-CSharp.dll", "Proxy assembly to add On.* hooks to"),
    new Option<FileInfo>("--runtime-detour", "Il2CppInterop.RuntimeDetour.dll that the game will load"),
};
hookgenCommand.Description = "Add strongly typed On.* hook events (emitted as IL) into generated proxy assemblies";
hookgenCommand.Handler = CommandHandler.Create((HookGenCommandOptions opts) =>
{
    var assembly = Path.Combine(opts.Input.FullName, opts.Assembly);
    var detour = opts.RuntimeDetour?.FullName
                 ?? Path.Combine(AppContext.BaseDirectory, "Il2CppInterop.RuntimeDetour.dll");
    var result = OnHookGenerator.Generate(assembly, detour);
    Console.WriteLine($"{result.Types} types / {result.Hooks} On.* hooks → {assembly}");
});

var deobfCommand = new Command("deobf");
deobfCommand.Description = "Tools for deobfuscating assemblies";
var deobfAnalyzeCommand = new Command("analyze")
{
    new Option<DirectoryInfo>("--input", "Directory of assemblies to deobfuscate") {IsRequired = true}.ExistingOnly(),
    new Option<string[]>("--add-prefix-to",
        "Assemblies and namespaces starting with these will get an Il2Cpp prefix in generated assemblies. Allows multiple values. Obsolete."),
    new Option<string[]>("--dont-add-prefix-to",
        "Assemblies and namespaces starting with these will not get an Il2Cpp prefix in generated assemblies. Allows multiple values."),
    new Option<bool>("--use-opt-out-prefixing",
        "Assemblies and namespaces will get an Il2Cpp prefix in generated assemblies unless otherwise specified. Obsolete.")
};
deobfAnalyzeCommand.Description =
    "Analyze deobfuscation performance with different parameter values. Will not generate assemblies.";

deobfAnalyzeCommand.Handler = CommandHandler.Create((DeobfAnalyzeCommandOptions opts) =>
{
    var buildResult = opts.Build();
    Il2CppInteropGenerator.Create(buildResult.Options)
        .AddLogger(buildResult.Logger)
        .AddDeobfuscationAnalyzer()
        .Run();
});

var deobfGenerateCommand = new Command("generate")
{
    new Option<DirectoryInfo>("--old-assemblies", "Directory with old unobfuscated assemblies") {IsRequired = true}
        .ExistingOnly(),
    new Option<DirectoryInfo>("--new-assemblies", "Directory to write obfuscation maps to") {IsRequired = true}
        .ExistingOnly(),
    new Option<DirectoryInfo>("--output", "Directory to write obfuscation maps to") {IsRequired = true},
    new Option<string[]>("--include",
        "Include these assemblies for deobfuscation map generation. If none are specified, all assemblies will be included."),
    new Option<int>("--deobf-uniq-chars", "How many characters per unique token to use during deobfuscation"),
    new Option<int>("--deobf-uniq-max", "How many maximum unique tokens per type are allowed during deobfuscation"),
    new Option<Regex>("--obf-regex",
        "Specifies a regex for obfuscated names. All types and members matching will be renamed.")
};
deobfGenerateCommand.Description =
    "Generate a deobfuscation map from original unobfuscated assemblies. Will not generate assemblies.";
deobfGenerateCommand.Handler = CommandHandler.Create((DeobfGenerateCommandOptions opts) =>
{
    var buildResult = opts.Build();
    Il2CppInteropGenerator.Create(buildResult.Options)
        .AddLogger(buildResult.Logger)
        .AddDeobfuscationMapGenerator()
        .Run();
});

var eventsCommand = new Command("events")
{
    new Option<DirectoryInfo>("--input", "Directory with the proxy assembly") {IsRequired = true}.ExistingOnly(),
    new Option<string>("--assembly", () => "Assembly-CSharp.dll", "Proxy assembly to list On.* events from"),
    new Option<FileInfo>("--output", "Write the listing to a file instead of stdout"),
};
eventsCommand.Description = "List generated On.* event names and delegate signatures (for hook authoring)";
eventsCommand.Handler = CommandHandler.Create((EventsCommandOptions opts) =>
{
    var assemblyPath = Path.Combine(opts.Input.FullName, opts.Assembly);
    if (!File.Exists(assemblyPath)) throw new FileNotFoundException("Proxy assembly not found", assemblyPath);
    var resolver = new Mono.Cecil.DefaultAssemblyResolver();
    resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(assemblyPath)));
    using var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(assemblyPath,
        new Mono.Cecil.ReaderParameters { AssemblyResolver = resolver });

    var lines = new List<string>();
    foreach (var type in assembly.MainModule.Types)
    {
        if (type.Namespace != "On" && !type.Namespace.StartsWith("On.", StringComparison.Ordinal)) continue;
        CollectEvents(type, type.FullName, lines);
        foreach (var nested in type.NestedTypes) CollectEvents(nested, type.FullName + "." + nested.Name, lines);
    }
    lines.Sort(StringComparer.Ordinal);

    if (opts.Output != null)
    {
        File.WriteAllLines(opts.Output.FullName, lines);
        Console.WriteLine($"{lines.Count} events → {opts.Output.FullName}");
    }
    else
    {
        foreach (var line in lines) Console.WriteLine(line);
    }
});

static string DescribeType(Mono.Cecil.TypeReference t)
{
    switch (t)
    {
        case Mono.Cecil.GenericParameter gp: return gp.Name;
        case Mono.Cecil.ByReferenceType b: return "ref " + DescribeType(b.ElementType);
        case Mono.Cecil.PointerType p: return DescribeType(p.ElementType) + "*";
        case Mono.Cecil.ArrayType a: return DescribeType(a.ElementType) + "[]";
        case Mono.Cecil.GenericInstanceType g:
            return DescribeType(g.ElementType) + "<" + string.Join(", ", g.GenericArguments.Select(DescribeType)) + ">";
        default: return t.FullName.Replace("/", "+");
    }
}

static void CollectEvents(Mono.Cecil.TypeDefinition type, string path, List<string> lines)
{
    foreach (var ev in type.Events)
    {
        var invoke = ev.EventType.Resolve().Methods.FirstOrDefault(m => m.Name == "Invoke");
        var parameters = invoke == null
            ? "?"
            : string.Join(", ", invoke.Parameters.Select(p => DescribeType(p.ParameterType)));
        lines.Add($"{path}.{ev.Name} : {(invoke == null ? "?" : DescribeType(invoke.ReturnType))} ({parameters})");
    }
    // nested orig_/hook_ delegate types are referenced by name in handler signatures
    foreach (var nested in type.NestedTypes)
    {
        if (!nested.Name.StartsWith("orig_", StringComparison.Ordinal) &&
            !nested.Name.StartsWith("hook_", StringComparison.Ordinal)) continue;
        var invoke = nested.Methods.FirstOrDefault(m => m.Name == "Invoke");
        var parameters = invoke == null
            ? "?"
            : string.Join(", ", invoke.Parameters.Select(p => DescribeType(p.ParameterType)));
        lines.Add($"delegate {path}.{nested.Name} : {(invoke == null ? "?" : DescribeType(invoke.ReturnType))} ({parameters})");
    }
}

var wrapperCommand = new Command("wrapper-gen")
{
    new Option<DirectoryInfo>("--headers",
        "Directory that contains libil2cpp headers. Directory must contains subdirectories named after libil2cpp version.")
    {
        IsRequired = true
    }.ExistingOnly(),
    new Option<DirectoryInfo>("--output", "Directory to write managed struct wrapper sources to") {IsRequired = true}
};
wrapperCommand.Description = "Tools for generating Il2Cpp struct wrappers from libi2lcpp source";
wrapperCommand.Handler = CommandHandler.Create((WrapperCommandOptions opts) =>
{
    Il2CppStructWrapperGenerator.Generate(opts.Build());
});

deobfCommand.Add(deobfAnalyzeCommand);
deobfCommand.Add(deobfGenerateCommand);
command.Add(deobfCommand);
command.Add(generateCommand);
command.Add(hookgenCommand);
command.Add(eventsCommand);
command.Add(wrapperCommand);

return command.Invoke(args);


internal record HookGenCommandOptions(DirectoryInfo Input, string Assembly, FileInfo? RuntimeDetour);

internal record EventsCommandOptions(DirectoryInfo Input, string Assembly, FileInfo? Output);

internal record CmdOptionsResult(GeneratorOptions Options, ILogger Logger);

internal record BaseCmdOptions(bool Verbose)
{
    public virtual CmdOptionsResult Build()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder
                .AddFilter("Il2CppInterop", Verbose ? LogLevel.Trace : LogLevel.Information)
                .AddSimpleConsole(opt => { opt.SingleLine = true; });
        });

        var logger = loggerFactory.CreateLogger("Il2CppInterop");

        return new CmdOptionsResult(new GeneratorOptions { Verbose = Verbose }, logger);
    }
}

internal record WrapperCommandOptions(DirectoryInfo Headers, DirectoryInfo Output, bool Verbose)
{
    public virtual Il2CppStructWrapperGeneratorOptions Build()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder
                .AddFilter("Il2CppInterop", Verbose ? LogLevel.Trace : LogLevel.Information)
                .AddSimpleConsole(opt => { opt.SingleLine = true; });
        });
        var logger = loggerFactory.CreateLogger("Il2CppInterop");
        return new Il2CppStructWrapperGeneratorOptions(Headers.FullName, Output.FullName, logger);
    }
}

internal record GenerateCommandOptions(
    bool Verbose,
    DirectoryInfo Input,
    DirectoryInfo Output,
    DirectoryInfo? Unity,
    FileInfo? GameAssembly,
    bool NoXrefCache,
    string[]? AddPrefixTo,
    string[]? DontAddPrefixTo,
    bool UseOptOutPrefixing,
    FileInfo? DeobfMap,
    int DeobfUniqChars,
    int DeobfUniqMax,
    string[]? BlacklistAssembly,
    Regex? ObfRegex,
    bool PassthroughNames,
    bool NoParallel,
    bool DirectInvoke
) : BaseCmdOptions(Verbose)
{
    public override CmdOptionsResult Build()
    {
        var res = base.Build();
        var opts = res.Options;

        opts.Source = Utils.LoadAssembliesFrom(Input);
        opts.OutputDir = Output.FullName;
        opts.UnityBaseLibsDir = Unity?.FullName;
        opts.GameAssemblyPath = GameAssembly?.FullName ?? "";
        opts.NoXrefCache = NoXrefCache;
        opts.TypeDeobfuscationCharsPerUniquifier = DeobfUniqChars;
        opts.TypeDeobfuscationMaxUniquifiers = DeobfUniqMax;
        opts.AdditionalAssembliesBlacklist.AddRange(BlacklistAssembly ?? Array.Empty<string>());
        opts.ObfuscatedNamesRegex = ObfRegex;
        opts.PassthroughNames = PassthroughNames;
        opts.Parallel = !NoParallel;
        opts.DirectInvoke = DirectInvoke;

        if (AddPrefixTo is not null && AddPrefixTo.Length > 0)
        {
            if (DontAddPrefixTo is not null && DontAddPrefixTo.Length > 0)
            {
                throw new Exception("--add-prefix-to cannot be used with --dont-add-prefix-to");
            }
            else if (UseOptOutPrefixing)
            {
                throw new Exception("--add-prefix-to cannot be used with --use-opt-out-prefixing");
            }

            opts.Il2CppPrefixMode = GeneratorOptions.PrefixMode.OptIn;
            foreach (var s in AddPrefixTo)
            {
                opts.NamespacesAndAssembliesToPrefix.Add(s);
            }
        }
        else if (DontAddPrefixTo is not null && DontAddPrefixTo.Length > 0)
        {
            opts.Il2CppPrefixMode = GeneratorOptions.PrefixMode.OptOut;
            foreach (var s in DontAddPrefixTo)
            {
                opts.NamespacesAndAssembliesToNotPrefix.Add(s);
            }
        }
        else if (UseOptOutPrefixing)
        {
            opts.Il2CppPrefixMode = GeneratorOptions.PrefixMode.OptOut;
        }

        if (opts.Il2CppPrefixMode == GeneratorOptions.PrefixMode.OptIn)
        {
            res.Logger.LogWarning("Opt-In prefixing is obsolete and will be removed in a future version.");
        }

        if (DeobfMap is not null)
        {
            opts.ReadRenameMap(DeobfMap.FullName);
        }

        return res;
    }
}

internal record DeobfAnalyzeCommandOptions(
    bool Verbose,
    string[]? AddPrefixTo,
    string[]? DontAddPrefixTo,
    bool UseOptOutPrefixing,
    DirectoryInfo Input
) : BaseCmdOptions(Verbose)
{
    public override CmdOptionsResult Build()
    {
        var res = base.Build();
        var opts = res.Options;

        opts.Source = Utils.LoadAssembliesFrom(Input);

        if (AddPrefixTo is not null && AddPrefixTo.Length > 0)
        {
            if (DontAddPrefixTo is not null && DontAddPrefixTo.Length > 0)
            {
                throw new Exception("--add-prefix-to cannot be used with --dont-add-prefix-to");
            }
            else if (UseOptOutPrefixing)
            {
                throw new Exception("--add-prefix-to cannot be used with --use-opt-out-prefixing");
            }

            opts.Il2CppPrefixMode = GeneratorOptions.PrefixMode.OptIn;
            foreach (var s in AddPrefixTo)
            {
                opts.NamespacesAndAssembliesToPrefix.Add(s);
            }
        }
        else if (DontAddPrefixTo is not null && DontAddPrefixTo.Length > 0)
        {
            opts.Il2CppPrefixMode = GeneratorOptions.PrefixMode.OptOut;
            foreach (var s in DontAddPrefixTo)
            {
                opts.NamespacesAndAssembliesToNotPrefix.Add(s);
            }
        }
        else if (UseOptOutPrefixing)
        {
            opts.Il2CppPrefixMode = GeneratorOptions.PrefixMode.OptOut;
        }

        if (opts.Il2CppPrefixMode == GeneratorOptions.PrefixMode.OptIn)
        {
            res.Logger.LogWarning("Opt-In prefixing is obsolete and will be removed in a future version.");
        }

        return res;
    }
}

internal record DeobfGenerateCommandOptions(
    bool Verbose,
    DirectoryInfo OldAssemblies,
    DirectoryInfo NewAssemblies,
    DirectoryInfo Output,
    string[]? Include,
    int DeobfUniqChars,
    int DeobfUniqMax,
    Regex? ObfRegex
) : BaseCmdOptions(Verbose)
{
    public override CmdOptionsResult Build()
    {
        var res = base.Build();
        var opts = res.Options;

        opts.Source = Utils.LoadAssembliesFrom(OldAssemblies);
        opts.OutputDir = Output.FullName;
        opts.DeobfuscationNewAssembliesPath = NewAssemblies.FullName;
        opts.DeobfuscationGenerationAssemblies.AddRange(Include ?? Array.Empty<string>());
        opts.TypeDeobfuscationCharsPerUniquifier = DeobfUniqChars;
        opts.TypeDeobfuscationMaxUniquifiers = DeobfUniqMax;
        opts.ObfuscatedNamesRegex = ObfRegex;

        return res;
    }
}