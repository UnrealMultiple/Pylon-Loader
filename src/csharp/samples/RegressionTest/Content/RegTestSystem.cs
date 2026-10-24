using Pylon.ModLoader;
using Pylon.ModLoader.Content;
using Pylon.ModLoader.SaveData;
using Terraria.Graphics;

namespace RegressionTest.Content
{
    /// <summary>
    /// 回归测试系统面：世界数据读写、每帧阶段计数、时钟与矩阵改写（原 Mod 级虚方法迁移），
    /// 计数器与开关仍挂在 RegressionTestMod 的静态字段上，供 ImGui 页与帧计数日志读取。
    /// </summary>
    public sealed class RegTestSystem : ModSystem
    {
        public override void OnWorldUnload()
        {
            RegTestScene.ResetForWorldReload();
            RegressionTestMod._autoDone = false;
        }

        public override void PreUpdatePlayers() => RegressionTestMod.CntPrePlayers++;
        public override void PostUpdateNPCs() => RegressionTestMod.CntPostNpcs++;
        public override void PostUpdateGores() => RegressionTestMod.CntPostGores++;
        public override void PreUpdateItems() => RegressionTestMod.CntPreItems++;
        public override void PostUpdateItems() => RegressionTestMod.CntPostItems++;
        public override void PostUpdateProjectiles() => RegressionTestMod.CntPostProjectiles++;

        public override void SaveWorldData(TagCompound tag)
        {
            RegressionTestMod.CntSaveWorld++;
            tag.Set("rtSaves", RegressionTestMod.CntSaveWorld);
            RegressionTestMod.Log.Info($"[rt] SaveWorldData 第 {RegressionTestMod.CntSaveWorld} 次");
        }

        public override void SaveWorldHeader(TagCompound tag)
        {
            RegressionTestMod.CntSaveHeader++;
            tag.Set("rtHeader", RegressionTestMod.CntSaveHeader);
            RegressionTestMod.Log.Info($"[rt] SaveWorldHeader 第 {RegressionTestMod.CntSaveHeader} 次");
        }

        public override void LoadWorldData(TagCompound tag)
        {
            RegressionTestMod.CntLoadWorld++;
            var saves = tag.GetInt("rtSaves");
            RegressionTestMod.Log.Info($"[rt] LoadWorldData 第 {RegressionTestMod.CntLoadWorld} 次（读到历史 rtSaves={saves}）");
        }

        public override void ModifyTimeRate(ref double timeRate, ref double tileUpdateRate, ref double eventUpdateRate)
        {
            RegressionTestMod.CntTimeRate++;
            if (RegressionTestMod.TimeRateMode == 1) timeRate = 2.0;
            else if (RegressionTestMod.TimeRateMode == 2) timeRate = 0.5;
        }

        public override void ModifyTransformMatrix(ref SpriteViewMatrix transform)
        {
            RegressionTestMod.CntTransform++;
            if (RegressionTestMod.ZoomOn)
            {
                var z = transform.Zoom;
                transform.Zoom = new Il2CppMicrosoft.Xna.Framework.Vector2(1.25f, 1.25f);
                _ = z;
            }
        }
    }
}
