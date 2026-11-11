using System.Reflection;
using System.Runtime.Loader;
using Il2CppInterop.RuntimeDetour.HookGen;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using CA = Mono.Cecil.TypeAttributes;
using MA = Mono.Cecil.MethodAttributes;
using PA = Mono.Cecil.ParameterAttributes;

var directory = Path.Combine(Path.GetTempPath(), "Pylon-HookGen-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var runtime = Path.Combine(directory, "HookGen.TestRuntime.dll");
var fixture = Path.Combine(directory, "HookGen.Fixture.dll");
var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
    Console.WriteLine("PASS " + message);
}
var platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
    .Select(p => MetadataReference.CreateFromFile(p)).ToList();
void Compile(string name, string source, string output, params string[] references)
{
    var compilation = CSharpCompilation.Create(name,
        new[] { CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest)) },
        platform.Concat(references.Select(p => MetadataReference.CreateFromFile(p))),
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
    var result = compilation.Emit(output);
    if (!result.Success) throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
}

Compile("HookGen.TestRuntime", """
using System;
using System.Linq;
using System.Reflection;
namespace Il2CppInterop.RuntimeDetour {
 public sealed class HookEndpoint {
  public static int Adds, Removes, Compact, Generic;
  public static MethodBase LastTarget;
  static HookEndpoint Resolve(Type type,string name,Type[] parameters,Type[] typeArgs,Type[] methodArgs) {
   if(typeArgs.Length != 0) type=type.MakeGenericType(typeArgs);
   if(name==".ctor") LastTarget=type.GetConstructor(parameters) ?? throw new MissingMethodException();
   else LastTarget=type.GetMethods(BindingFlags.Public|BindingFlags.Static|BindingFlags.Instance|BindingFlags.DeclaredOnly)
    .Where(m=>m.Name==name && m.GetGenericArguments().Length==methodArgs.Length)
    .Select(m=>methodArgs.Length==0?m:m.MakeGenericMethod(methodArgs))
    .Single(m=>m.GetParameters().Select(p=>p.ParameterType).SequenceEqual(parameters));
   return new HookEndpoint();
  }
  public static HookEndpoint BindOnce(ref HookEndpoint field,Type type,string name,Type[] parameters,Type orig,Type handler)
   => field ??= Resolve(type,name,parameters,Type.EmptyTypes,Type.EmptyTypes);
  public static HookEndpoint BindOnce(ref HookEndpoint field,Type type,string name,int count,Type orig,Type handler) {
   if(field==null) {
    Compact++;
    var target=type.GetMethods(BindingFlags.Public|BindingFlags.Static|BindingFlags.Instance|BindingFlags.DeclaredOnly)
      .Single(m=>m.Name==name && !m.IsGenericMethod && m.GetParameters().Length==count);
    field=Resolve(type,name,target.GetParameters().Select(p=>p.ParameterType).ToArray(),Type.EmptyTypes,Type.EmptyTypes);
   }
   return field;
  }
  public static HookEndpoint BindOnce(ref HookEndpoint field,Type type,string name,Type[] parameters,Type[] typeArgs,Type orig,Type handler)
   => field ??= Resolve(type,name,parameters,typeArgs,Type.EmptyTypes);
  public static HookEndpoint BindGenericOnce(ref HookEndpoint field,Type type,string name,Type[] parameters,Type[] typeArgs,Type[] methodArgs,Type orig,Type handler) {
   if(field==null) { Generic++; field=Resolve(type,name,parameters,typeArgs,methodArgs); }
   return field;
  }
  public void Add(Delegate handler) { Adds++; }
  public void Remove(Delegate handler) { Removes++; }
 }
}
""", runtime);

using (var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("HookGen.Fixture", new Version(1,0)), "HookGen.Fixture", ModuleKind.Dll))
{
    var module = assembly.MainModule;
    // The proxy contract targets System.Runtime, not HookGen's own CoreLib.
    var core = AssemblyNameReference.Parse(AssemblyName.GetAssemblyName(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "System.Runtime.dll")).FullName);
    module.AssemblyReferences.Add(core);
    module.TypeSystem.CoreLibrary.Name = "System.Runtime";
    if (module.TypeSystem.CoreLibrary is AssemblyNameReference original) { original.Version = core.Version; original.PublicKeyToken = core.PublicKeyToken; }
    TypeDefinition Type(string name, bool generic = false)
    {
        var type = new TypeDefinition("Fixture", name, CA.Public | CA.Class, module.TypeSystem.Object);
        if (generic)
        {
            var gp = new GenericParameter("T", type) { Attributes = Mono.Cecil.GenericParameterAttributes.ReferenceTypeConstraint | Mono.Cecil.GenericParameterAttributes.DefaultConstructorConstraint };
            type.GenericParameters.Add(gp);
        }
        module.Types.Add(type);
        return type;
    }
    MethodDefinition Method(TypeDefinition type, string name, TypeReference result, params TypeReference[] parameters)
    {
        var method = new MethodDefinition(name, MA.Public | MA.Static | MA.HideBySig, result);
        foreach (var p in parameters) method.Parameters.Add(new ParameterDefinition("arg" + method.Parameters.Count, PA.None, p));
        type.Methods.Add(method);
        var pointer = new FieldDefinition("NativeMethodInfoPtr_" + type.Fields.Count, Mono.Cecil.FieldAttributes.Private | Mono.Cecil.FieldAttributes.Static, module.TypeSystem.IntPtr);
        type.Fields.Add(pointer);
        var il = method.Body.GetILProcessor();
        il.Emit(OpCodes.Ldsfld, pointer); il.Emit(OpCodes.Pop);
        if (result is ByReferenceType) { il.Emit(OpCodes.Ldnull); il.Emit(OpCodes.Conv_U); }
        else if (result != module.TypeSystem.Void) il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        return method;
    }
    void Ctor(TypeDefinition type, params TypeReference[] parameters)
    {
        var method = Method(type, ".ctor", module.TypeSystem.Void, parameters);
        method.Attributes = MA.Public | MA.HideBySig | MA.SpecialName | MA.RTSpecialName;
        method.HasThis = true;
        var il = method.Body.GetILProcessor();
        var first = method.Body.Instructions[0];
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Call, new MethodReference(".ctor", module.TypeSystem.Void, module.TypeSystem.Object) { HasThis = true }));
    }
    var single = Type("Single"); Ctor(single, module.TypeSystem.Int32);
    var multi = Type("Multi"); Ctor(multi); Ctor(multi, module.TypeSystem.Int32);
    var plain = Type("Plain");
    Method(plain, "Tick", module.TypeSystem.Void);
    Method(plain, "Tick", module.TypeSystem.Void, module.TypeSystem.Int32);
    Method(plain, "Over", module.TypeSystem.Void, module.TypeSystem.Int32);
    Method(plain, "Over", module.TypeSystem.Void, module.TypeSystem.String);
    Method(plain, "RefReturn", new ByReferenceType(module.TypeSystem.Int32));
    var flags = Method(plain,"Flags",module.TypeSystem.Void,new ByReferenceType(module.TypeSystem.Int32),new ByReferenceType(module.TypeSystem.Int32));
    flags.Parameters[0].Attributes = PA.Out;
    flags.Parameters[1].Attributes = PA.In;
    var readonlyAttribute = new TypeReference("System.Runtime.CompilerServices", "IsReadOnlyAttribute", module, core);
    flags.Parameters[1].CustomAttributes.Add(new CustomAttribute(new MethodReference(".ctor",module.TypeSystem.Void,readonlyAttribute) {HasThis=true}));
    var keyword = Type("class"); Method(keyword,"event",module.TypeSystem.Void);
    var box = Type("Box`1",true); Ctor(box);
    Method(box,"Touch",module.TypeSystem.Void,new ByReferenceType(box.GenericParameters[0]));
    var map = Method(box,"Map",module.TypeSystem.Void);
    var u = new GenericParameter("U",map) { Attributes=Mono.Cecil.GenericParameterAttributes.ReferenceTypeConstraint | Mono.Cecil.GenericParameterAttributes.DefaultConstructorConstraint };
    map.GenericParameters.Add(u);
    map.Parameters.Add(new ParameterDefinition("left", PA.None,new ByReferenceType(box.GenericParameters[0])));
    map.Parameters.Add(new ParameterDefinition("right", PA.Out,new ByReferenceType(u)));
    map.Parameters.Add(new ParameterDefinition("values", PA.None,new ArrayType(u)));
    var echo = Method(plain,"Echo",module.TypeSystem.Void);
    var v = new GenericParameter("V",echo); echo.GenericParameters.Add(v);
    echo.Parameters.Add(new ParameterDefinition("value",PA.None,new ByReferenceType(v)));
    assembly.Write(fixture);
}

var result = OnHookGenerator.Generate(fixture, runtime);
Check(result.Diagnostics.Count == 1 && result.Diagnostics[0].Contains("RefReturn"), "ref-return filtered with diagnostic");
using (var generated = AssemblyDefinition.ReadAssembly(fixture))
{
    var types = generated.MainModule.GetTypes().ToArray();
    var events = types.Where(t=>t.Namespace.StartsWith("On") || t.DeclaringType != null && t.FullName.StartsWith("On.")).SelectMany(t=>t.Events).ToArray();
    Check(events.Length == result.Hooks, "event count matches generator result including single ctor");
    Check(events.All(e=>e.AddMethod.Body.Instructions.Count>0 && e.RemoveMethod.Body.Instructions.Count>0), "all add/remove bodies populated");
    Check(generated.MainModule.GetType("On.Fixture.Single").Events.Single().Name=="ctor", "single constructor event registered");
    Check(generated.MainModule.GetType("On.Fixture.Multi").Events.Count==2, "overloaded constructors registered");
    Check(generated.MainModule.GetType("On.Fixture.class").Events.Single().Name=="event", "metadata keyword identifiers do not contain @");
    var onBox = generated.MainModule.GetType("On.Fixture.Box`1");
    Check(onBox.GenericParameters[0].HasReferenceTypeConstraint && onBox.GenericParameters[0].HasDefaultConstructorConstraint, "declaring type constraints preserved");
    var onMap = onBox.NestedTypes.Single(t=>t.Name=="Map`1");
    Check(onMap.GenericParameters.Count==2 && onMap.GenericParameters[1].HasDefaultConstructorConstraint, "generic host preserves enclosing and method constraints");
    Check(onMap.NestedTypes.Where(t=>t.BaseType?.FullName=="System.MulticastDelegate").All(t=>t.GenericParameters[1].HasDefaultConstructorConstraint),"delegate constraints preserved");
    Check(events.SelectMany(e=>e.AddMethod.Body.Instructions).Any(i=>i.OpCode==OpCodes.Ldc_I4_0), "macro optimization emits short constants");
    var compactCalls=events.SelectMany(e=>e.AddMethod.Body.Instructions).Count(i=>i.Operand is MethodReference m && m.Name=="BindOnce" && m.Parameters.Count==6 && m.Parameters[3].ParameterType.MetadataType==MetadataType.Int32);
    Check(compactCalls>=4, "different parameter-count overloads use compact binding");
    foreach(var e in events.Where(e=>e.Name.StartsWith("ctor")))
        Check(!e.AddMethod.Body.Instructions.Any(i=>i.Operand is MethodReference m && m.Name=="BindOnce" && m.Parameters[3].ParameterType.MetadataType==MetadataType.Int32),"constructor uses pure parameter array");
}
var firstSize = new FileInfo(fixture).Length;
var second = OnHookGenerator.Generate(fixture,runtime);
Check(second.Hooks==result.Hooks && new FileInfo(fixture).Length==firstSize,"repeat generation preserves event inventory and size");

var consumer = Path.Combine(directory,"HookGen.Consumer.dll");
var consumerSource = """
using System.Text;
public static class Consumer {
 public static void Run() {
  On.Fixture.Single.ctor += Single;
  On.Fixture.Single.ctor -= Single;
  On.Fixture.Multi.ctor_ += Empty;
  On.Fixture.Multi.ctor_ -= Empty;
  On.Fixture.Multi.ctor_int += Multi;
  On.Fixture.Multi.ctor_int -= Multi;
  On.Fixture.Plain.Tick_ += Tick;
  On.Fixture.Plain.Tick_ -= Tick;
  On.Fixture.Plain.Tick_int += TickInt;
  On.Fixture.Plain.Tick_int -= TickInt;
  On.Fixture.Plain.Flags += Flags;
  On.Fixture.Plain.Flags -= Flags;
  On.Fixture.@class.@event += Keyword;
  On.Fixture.@class.@event -= Keyword;
  On.Fixture.Box<object>.ctor += Box;
  On.Fixture.Box<object>.ctor -= Box;
  On.Fixture.Box<object>.Touch += Touch;
  On.Fixture.Box<object>.Touch -= Touch;
  On.Fixture.Box<object>.Map<StringBuilder>.Map += Map;
  On.Fixture.Box<object>.Map<StringBuilder>.Map -= Map;
  On.Fixture.Plain.Echo<int>.Echo += Echo;
  On.Fixture.Plain.Echo<int>.Echo -= Echo;
 }
 static void Single(On.Fixture.Single.orig_ctor orig,Fixture.Single self,int x) {}
 static void Empty(On.Fixture.Multi.orig_ctor_ orig,Fixture.Multi self) {}
 static void Multi(On.Fixture.Multi.orig_ctor_int orig,Fixture.Multi self,int x) {}
 static void Tick(On.Fixture.Plain.orig_Tick_ orig) {}
 static void TickInt(On.Fixture.Plain.orig_Tick_int orig,int x) {}
 static void Flags(On.Fixture.Plain.orig_Flags orig,out int x,in int y) { orig(out x,in y); }
 static void Keyword(On.Fixture.@class.orig_event orig) {}
 static void Box(On.Fixture.Box<object>.orig_ctor orig,Fixture.Box<object> self) {}
 static void Touch(On.Fixture.Box<object>.orig_Touch orig,ref object value) { orig(ref value); }
 static void Map(On.Fixture.Box<object>.Map<StringBuilder>.orig_Map orig,ref object left,out StringBuilder right,StringBuilder[] values) { orig(ref left,out right,values); }
 static void Echo(On.Fixture.Plain.Echo<int>.orig_Echo orig,ref int value) { orig(ref value); }
}
""";
Compile("HookGen.Consumer",consumerSource,consumer,fixture,runtime);
Check(true,"Roslyn compiles actual strong-typed consumer including ref/out/in and generic methods");
var context = new AssemblyLoadContext("HookGen.Tests",isCollectible:true);
var loadedRuntime = context.LoadFromAssemblyPath(runtime);
var loadedFixture = context.LoadFromAssemblyPath(fixture);
var loadedConsumer = context.LoadFromAssemblyPath(consumer);
Check(loadedFixture.GetTypes().Length>0,"CLR loads every generated type");
loadedConsumer.GetType("Consumer")!.GetMethod("Run")!.Invoke(null,null);
var endpoint = loadedRuntime.GetType("Il2CppInterop.RuntimeDetour.HookEndpoint")!;
Check((int)endpoint.GetField("Adds")!.GetValue(null)! ==11 && (int)endpoint.GetField("Removes")!.GetValue(null)! ==11,"generated accessors bind/add/remove successfully");
Check((int)endpoint.GetField("Generic")!.GetValue(null)! ==2,"generic method bindings use separated type and method arguments");
var last = (MethodInfo)endpoint.GetField("LastTarget")!.GetValue(null)!;
Check(last.IsGenericMethod && !last.ContainsGenericParameters && last.GetGenericArguments().Single()==typeof(int),"generic endpoint receives closed concrete target");
context.Unload();
var profile = Path.Combine(directory,"HookGen.Profile.dll");
File.Copy(fixture,profile);
var selected = OnHookGenerator.Generate(profile,runtime,new HookGenOptions { MethodFilter = m=>m.Name=="Tick" && m.Parameters.Count==1 });
Check(selected.Hooks==1 && selected.CandidateHooks==result.Hooks && selected.SkippedByFilter==result.Hooks-1,"profile reports selected and skipped hook counts");
using(var profiled=AssemblyDefinition.ReadAssembly(profile))
    Check(profiled.MainModule.GetType("On.Fixture.Plain").Events.Single().Name=="Tick_int","profile preserves full API overload names");
Check(new FileInfo(profile).Length<firstSize,"profile removes unselected generated metadata");
if(args.Length==2 && args[0]=="--runtime")
{
    var realRuntime=Path.GetFullPath(args[1]);
    var realFixture=Path.Combine(directory,"Real.Fixture.dll");
    var realConsumer=Path.Combine(directory,"Real.Consumer.dll");
    File.Copy(fixture,realFixture);
    OnHookGenerator.Generate(realFixture,realRuntime);
    Compile("HookGen.RealConsumer",consumerSource,realConsumer,realFixture,realRuntime);
    Check(true,"real RuntimeDetour: Roslyn compiles the same ref/out/in/generic consumer");
    var realContext=new AssemblyLoadContext("HookGen.RealRuntime",isCollectible:true);
    realContext.Resolving += (load,name) => {
        var dependency=Path.Combine(Path.GetDirectoryName(realRuntime)!,name.Name+".dll");
        return File.Exists(dependency)?load.LoadFromAssemblyPath(dependency):null;
    };
    var realAssembly=realContext.LoadFromAssemblyPath(realRuntime);
    var realProxy=realContext.LoadFromAssemblyPath(realFixture);
    var realClient=realContext.LoadFromAssemblyPath(realConsumer);
    Check(realProxy.GetTypes().Length>0 && realClient.GetType("Consumer")!=null,"real RuntimeDetour: generated types and consumer load");
    var endpointType=realAssembly.GetType("Il2CppInterop.RuntimeDetour.HookEndpoint")!;
    var bind=endpointType.GetMethods().Single(m=>m.Name=="Bind" && m.GetParameters().Length==5);
    object BindReal(string hostName,string eventName,string targetName,Type[] parameterTypes)
    {
        var host=realProxy.GetType(hostName)!;
        var hook=host.GetEvent(eventName)!.EventHandlerType!;
        var orig=hook.GetMethod("Invoke")!.GetParameters()[0].ParameterType;
        var game=realProxy.GetType(hostName.Replace("On.",""))!;
        return bind.Invoke(null,new object[]{game,targetName,parameterTypes,orig,hook})!;
    }
    var flagsEndpoint=BindReal("On.Fixture.Plain","Flags","Flags",new[]{typeof(int).MakeByRefType(),typeof(int).MakeByRefType()});
    var flagsTarget=(MethodInfo)endpointType.GetProperty("Target")!.GetValue(flagsEndpoint)!;
    Check(flagsTarget.GetParameters()[0].IsOut && flagsTarget.GetParameters()[1].IsIn,"real RuntimeDetour: strict out/in delegate validation and exact resolution pass");
    foreach(var entry in new[]{("On.Fixture.Single","ctor",new[]{typeof(int)}),("On.Fixture.Multi","ctor_",Type.EmptyTypes),("On.Fixture.Multi","ctor_int",new[]{typeof(int)})})
        Check(BindReal(entry.Item1,entry.Item2,".ctor",entry.Item3)!=null,"real RuntimeDetour: constructor pure parameter array binds "+entry.Item2);
    var bindGeneric=endpointType.GetMethod("BindGeneric")!;
    var openHost=realProxy.GetType("On.Fixture.Box`1+Map`1")!;
    var closedHost=openHost.MakeGenericType(typeof(object),typeof(System.Text.StringBuilder));
    var mapHook=closedHost.GetEvent("Map")!.EventHandlerType!;
    var mapOrig=mapHook.GetMethod("Invoke")!.GetParameters()[0].ParameterType;
    var mapped=bindGeneric.Invoke(null,new object[]{realProxy.GetType("Fixture.Box`1")!,"Map",new[]{typeof(object).MakeByRefType(),typeof(System.Text.StringBuilder).MakeByRefType(),typeof(System.Text.StringBuilder[])},new[]{typeof(object)},new[]{typeof(System.Text.StringBuilder)},mapOrig,mapHook})!;
    var mapTarget=(MethodInfo)endpointType.GetProperty("Target")!.GetValue(mapped)!;
    Check(!mapTarget.ContainsGenericParameters && mapTarget.DeclaringType!.GetGenericArguments().Single()==typeof(object) && mapTarget.GetGenericArguments().Single()==typeof(System.Text.StringBuilder),"real RuntimeDetour: type and method generic arguments close independently");
    var echoHost=realProxy.GetType("On.Fixture.Plain+Echo`1")!.MakeGenericType(typeof(int));
    var echoHook=echoHost.GetEvent("Echo")!.EventHandlerType!;
    var echoOrig=echoHook.GetMethod("Invoke")!.GetParameters()[0].ParameterType;
    var echoEndpoint=bindGeneric.Invoke(null,new object[]{realProxy.GetType("Fixture.Plain")!,"Echo",new[]{typeof(int).MakeByRefType()},Type.EmptyTypes,new[]{typeof(int)},echoOrig,echoHook})!;
    Check(!((MethodInfo)endpointType.GetProperty("Target")!.GetValue(echoEndpoint)!).ContainsGenericParameters,"real RuntimeDetour: generic ref parameter resolves closed method");
    realContext.Unload();
}
else if(args.Length==2)
{
    var fullCopy=Path.Combine(directory,"Production.Full.dll");
    var profileCopy=Path.Combine(directory,"Production.Profile.dll");
    File.Copy(Path.GetFullPath(args[0]),fullCopy);
    File.Copy(Path.GetFullPath(args[0]),profileCopy);
    var fullResult=OnHookGenerator.Generate(fullCopy,Path.GetFullPath(args[1]));
    var profileResult=OnHookGenerator.Generate(profileCopy,Path.GetFullPath(args[1]),new HookGenOptions { MethodFilter=m=>m.DeclaringType.FullName=="Terraria.Main" && (m.Name=="Update" || m.Name=="Draw") });
    Console.WriteLine($"MEASURE full={new FileInfo(fullCopy).Length} bytes/{fullResult.Hooks} hooks, profile={new FileInfo(profileCopy).Length} bytes/{profileResult.Hooks} hooks, original={new FileInfo(args[0]).Length} bytes; copies={directory}");
}
Console.WriteLine($"ALL PASS {checks}; fixture={firstSize} bytes; isolated output={directory}");
