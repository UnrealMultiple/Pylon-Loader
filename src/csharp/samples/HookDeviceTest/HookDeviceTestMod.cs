using System.Text;
using Il2CppInterop.RuntimeDetour;
using Il2CppMicrosoft.Xna.Framework;
using Pylon.Mod;

namespace HookDeviceTest;

/// <summary>Opt-in game-thread Hook checks. Does not modify players, worlds, inventory or saves.</summary>
public sealed class HookDeviceTestMod : IPylonMod
{
    private const string DirectoryPath="/storage/emulated/0/Android/data/dev.pylon.loader/files/HookDeviceTest";
    private Action<string> _log=null!;
    private int _frames;
    public void Load(IModContext context)
    {
        _log=context.Log;
        On.Terraria.Main.Update+=Update;
        _log("[hooktest] ready; write run to files/HookDeviceTest/request.txt to execute (no automatic tests)");
    }
    private void Update(On.Terraria.Main.orig_Update orig,Terraria.Main self,GameTime time)
    {
        orig(self,time);
        if(++_frames%30!=0)return;
        var request=Path.Combine(DirectoryPath,"request.txt");
        if(!File.Exists(request))return;
        var command=File.ReadAllText(request).Trim();File.Delete(request);
        if(command!="run")return;
        var report=new StringBuilder();
        void Record(string text){report.AppendLine(text);_log("[hooktest] "+text);}
        void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(label);Record("PASS "+label);}
        Record($"HOOK DEVICE TEST {DateTimeOffset.Now:O}; {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        try
        {
            var baseline=HookManager.AppliedCount;
            var target=typeof(Terraria.Utils).GetMethod("SolveQuadratic",new[]{typeof(float),typeof(float),typeof(float),typeof(float).MakeByRefType(),typeof(float).MakeByRefType()})!;
            var rootCalls=0;
            using(var hook=new Hook(target,new RootsHook((RootsOrig orig,float a,float b,float c,out float x,out float y)=>{rootCalls++;var result=orig(a,b,c,out x,out y);x+=10;y+=10;return result;})))
            {
                var ok=Terraria.Utils.SolveQuadratic(1,-3,2,out var x,out var y);
                Check(ok && rootCalls==1 && Math.Min(x,y)==11 && Math.Max(x,y)==12,"float out: native orig then handler writeback");
            }
            Check(HookManager.AppliedCount==baseline,"last unsubscribe removes user subscription");
            rootCalls=0;
            using(var hook=new Hook(target,new RootsHook((RootsOrig orig,float a,float b,float c,out float x,out float y)=>{rootCalls++;x=7;y=8;return true;})))
            {Check(Terraria.Utils.SolveQuadratic(1,-3,2,out var x,out var y)&&x==7&&y==8&&rootCalls==1,"out without orig + reapply same site");}
            var first=new Hook(target,new RootsHook((RootsOrig orig,float a,float b,float c,out float x,out float y)=>{var ok=orig(a,b,c,out x,out y);x+=10;y+=10;return ok;}));
            var second=new Hook(target,new RootsHook((RootsOrig orig,float a,float b,float c,out float x,out float y)=>{var ok=orig(a,b,c,out x,out y);x+=100;y+=100;return ok;}));
            try
            {
                Terraria.Utils.SolveQuadratic(1,-3,2,out var x,out var y);
                Check(Math.Min(x,y)==111 && Math.Max(x,y)==112,"two typed handlers preserve out chain");
                first.Undo();Terraria.Utils.SolveQuadratic(1,-3,2,out x,out y);
                Check(Math.Min(x,y)==101&&Math.Max(x,y)==102,"head-first undo retains tail");
            }
            finally{second.Dispose();first.Dispose();}
            var reference=typeof(Terraria.Utils).GetMethod("JustBecameTrue",new[]{typeof(bool),typeof(bool).MakeByRefType()})!;
            var refCalls=0;
            using(var hook=new Hook(reference,new RefHook((RefOrig orig,bool state,ref bool holder)=>{refCalls++;var answer=orig(state,ref holder);holder=true;return answer;})))
            {var holder=false;Terraria.Utils.JustBecameTrue(true,ref holder);Check(refCalls==1&&holder,"bool ref conversion and writeback");}
            var vector=typeof(Vector2).GetMethod("Add",new[]{typeof(Vector2).MakeByRefType(),typeof(Vector2).MakeByRefType(),typeof(Vector2).MakeByRefType()})!;
            var vectors=0;
            using(var hook=new Hook(vector,new VectorHook((VectorOrig orig,ref Vector2 a,ref Vector2 b,out Vector2 result)=>{vectors++;orig(ref a,ref b,out result);result.X+=10;})))
            {var a=new Vector2(1,2);var b=new Vector2(3,4);Vector2.Add(ref a,ref b,out var result);Check(vectors==1&&result.X==14&&result.Y==6,"blittable struct ref/ref/out ABI");}
            var text=typeof(Terraria.Utils).GetMethod("ParseCommandPrefix",new[]{typeof(string),typeof(string),typeof(string).MakeByRefType()})!;
            var strings=0;
            using(var hook=new Hook(text,new TextHook((TextOrig orig,string value,string prefix,out string remainder)=>{strings++;remainder="hook-out";return true;})))
            {Check(Terraria.Utils.ParseCommandPrefix("/test","/",out var remainder)&&remainder=="hook-out"&&strings==1,"string out without orig");}
            Check(HookManager.AppliedCount==baseline,"all temporary subscriptions cleaned up");
            Record($"DONE PASS; applied={HookManager.AppliedCount}; cachedSites={HookManager.CachedSiteCount}; idle ShadowHook patches intentionally retained");
        }
        catch(Exception error){Record("FAIL "+error);}
        System.IO.Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(Path.Combine(DirectoryPath,"latest.txt"),report.ToString());
        File.WriteAllText(Path.Combine(DirectoryPath,$"hook-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.txt"),report.ToString());
    }
    private delegate bool RootsOrig(float a,float b,float c,out float x,out float y);
    private delegate bool RootsHook(RootsOrig orig,float a,float b,float c,out float x,out float y);
    private delegate bool RefOrig(bool state,ref bool holder);
    private delegate bool RefHook(RefOrig orig,bool state,ref bool holder);
    private delegate void VectorOrig(ref Vector2 a,ref Vector2 b,out Vector2 result);
    private delegate void VectorHook(VectorOrig orig,ref Vector2 a,ref Vector2 b,out Vector2 result);
    private delegate bool TextOrig(string value,string prefix,out string remainder);
    private delegate bool TextHook(TextOrig orig,string value,string prefix,out string remainder);
}
