using Pylon.ModLoader;
using Terraria;
using Terraria.DataStructures;
var count=0;
void Check(bool value,string name) { if(!value) throw new Exception(name); count++; }
Main.player=[new Player()];
var p=new Projectile(); var pipeline=PlayerPipeline.Instance;
PlayerFishingHooks.Install(); PlayerFishingHooks.Install();
var a=new FishingAttempt { rolledItemDrop=3, rolledEnemySpawn=4, fishingLevel=10 };
var observer=new Observer(); pipeline.Mods=[observer];
var calls=0;
On.Terraria.Projectile.Probe((Projectile _, ref FishingAttempt v)=>{ calls++; Check(v.fishingLevel==15,"条件在orig之前回写"); Check(v.rolledItemDrop==3 && v.rolledEnemySpawn==4,"条件钩子保留掷骰结果"); },p,ref a);
Check(calls==1 && observer.Probes==1,"重复安装不重复派发");
On.Terraria.Projectile.Roll((Projectile _, ref FishingAttempt v)=>{ v.rolledItemDrop=7; v.rolledEnemySpawn=8; },p,ref a);
Check(observer.Seen==7,"CatchFish在orig之后"); Check(a.rolledItemDrop==9 && a.rolledEnemySpawn==10,"结果回写");
pipeline.Mods=[new Broken(),observer];
On.Terraria.Projectile.Probe((Projectile _, ref FishingAttempt v)=>{},p,ref a);
Check(observer.Probes==2 && ContentErrors.Count==1,"条件异常隔离");
On.Terraria.Projectile.Roll((Projectile _, ref FishingAttempt v)=>{},p,ref a);
Check(observer.Catches==2 && ContentErrors.Count==2,"结果异常隔离");
observer.Enabled=false;
On.Terraria.Projectile.Roll((Projectile _, ref FishingAttempt v)=>{},p,ref a);
Check(observer.Catches==2,"无覆写门控");
pipeline.Active=false; pipeline.Mods=[observer]; observer.Enabled=true;
var original=a;
On.Terraria.Projectile.Probe((Projectile _, ref FishingAttempt v)=>calls++,p,ref a);
Check(a.fishingLevel==original.fishingLevel && observer.Probes==2,"无订阅直通");
pipeline.Active=true; p.owner=255;
On.Terraria.Projectile.Roll((Projectile _, ref FishingAttempt v)=>calls++,p,ref a);
Check(observer.Catches==2,"owner255直通");
p.owner=-1;
On.Terraria.Projectile.Probe((Projectile _, ref FishingAttempt v)=>calls++,p,ref a);
Check(observer.Probes==2,"负owner直通");
p.owner=1;
On.Terraria.Projectile.Roll((Projectile _, ref FishingAttempt v)=>calls++,p,ref a);
Check(observer.Catches==2,"越界owner直通");
Main.player=null;
On.Terraria.Projectile.Probe((Projectile _, ref FishingAttempt v)=>calls++,p,ref a);
Check(observer.Probes==2,"空玩家表直通");
Console.WriteLine($"ALL PASS {count}");
sealed class Observer:ModPlayer {
 public int Probes,Catches,Seen;
 public override void ModifyFishingAttempt(Player p,ref FishingAttempt a) {Probes++; a.fishingLevel+=5; a.rolledItemDrop=99; a.rolledEnemySpawn=99;}
 public override void CatchFish(Player p,ref FishingAttempt a) {Catches++; Seen=a.rolledItemDrop; a.rolledItemDrop=9; a.rolledEnemySpawn=10;}
}
sealed class Broken:ModPlayer {
 public override void ModifyFishingAttempt(Player p,ref FishingAttempt a)=>throw new Exception("probe");
 public override void CatchFish(Player p,ref FishingAttempt a)=>throw new Exception("catch");
}
