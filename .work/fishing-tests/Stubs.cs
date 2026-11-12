namespace Terraria.DataStructures { public struct FishingAttempt { public int rolledItemDrop, rolledEnemySpawn, fishingLevel; } }
namespace Terraria { public class Player { } public class Projectile { public int owner; } public static class Main { public static Player[]? player; } }
namespace On.Terraria {
 public static class Projectile {
  public delegate void orig_FishingCheck_ProbeForQuestFish(global::Terraria.Projectile p, ref global::Terraria.DataStructures.FishingAttempt a);
  public delegate void orig_FishingCheck_RollItemDrop(global::Terraria.Projectile p, ref global::Terraria.DataStructures.FishingAttempt a);
  public delegate void ProbeHook(orig_FishingCheck_ProbeForQuestFish o, global::Terraria.Projectile p, ref global::Terraria.DataStructures.FishingAttempt a);
  public delegate void RollHook(orig_FishingCheck_RollItemDrop o, global::Terraria.Projectile p, ref global::Terraria.DataStructures.FishingAttempt a);
  public static event ProbeHook? FishingCheck_ProbeForQuestFish;
  public static event RollHook? FishingCheck_RollItemDrop;
  public static void Probe(orig_FishingCheck_ProbeForQuestFish o, global::Terraria.Projectile p, ref global::Terraria.DataStructures.FishingAttempt a) => FishingCheck_ProbeForQuestFish!(o,p,ref a);
  public static void Roll(orig_FishingCheck_RollItemDrop o, global::Terraria.Projectile p, ref global::Terraria.DataStructures.FishingAttempt a) => FishingCheck_RollItemDrop!(o,p,ref a);
 }
}
namespace Pylon.ModLoader {
 internal enum HookId { ModifyFishingAttempt, CatchFish }
 internal class ModPlayer {
  public bool Enabled = true;
  public bool HookEnabled(HookId h) => Enabled;
  public virtual void ModifyFishingAttempt(Terraria.Player p, ref Terraria.DataStructures.FishingAttempt a) { }
  public virtual void CatchFish(Terraria.Player p, ref Terraria.DataStructures.FishingAttempt a) { }
 }
 internal sealed class PlayerPipeline {
  public static PlayerPipeline Instance {get;}=new();
  public bool Active=true; public ModPlayer[] Mods=[];
  public bool ModsActive(HookId h)=>Active;
  public ModPlayer[] ModsOf(Terraria.Player p)=>Mods;
 }
 internal static class ContentErrors { public static int Count; public static void Report(string s)=>Count++; }
 internal static class Safe { public static string Describe(Exception e)=>e.Message; }
}
