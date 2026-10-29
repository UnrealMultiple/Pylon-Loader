using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent;
using Terraria.Localization;
using Terraria.Map;
using Terraria.UI;

namespace Pylon.ModLoader
{
    /// <summary>自定义传送晶塔（ModPylon）：3x4 的 ModTile，游戏的地图/传送系统认它，一个类型在世界里只有一个。</summary>
    public abstract class ModPylon : ModTile
    {
        /// <summary>这边的晶塔类型号；注册时排到游戏的 TeleportPylonType.Count（11）之后。</summary>
        public virtual int PylonType { get; set; } = -1;

        /// <summary>地图图标的贴图路径（默认就是 <see cref="Texture"/> + "_MapIcon"）。</summary>
        public virtual string MapIconTexture => Texture + "_MapIcon";

        /// <summary>能不能再放一个（游戏默认：一个类型在世界里只有一个）。</summary>
        public virtual bool CanPlacePylon() => !Main.PylonSystem.HasPylonOfType((TeleportPylonType)PylonType);

        /// <summary>目的地/出发地的村民够不够（默认 2 个：在附近、在房子里）。</summary>
        public virtual bool ValidTeleportCheck_NPCCount(TeleportPylonInfo pylonInfo, int defaultNecessaryNPCCount) =>
            TeleportPylonsSystem.DoesPositionHaveEnoughNPCs(defaultNecessaryNPCCount, pylonInfo.PositionInTiles);

        /// <summary>目的地周围的生物群系对不对（sceneData 是围着它扫出来的 SceneMetrics）。</summary>
        public virtual bool ValidTeleportCheck_BiomeRequirements(TeleportPylonInfo pylonInfo, SceneMetrics sceneData) => true;

        /// <summary>在地图上画这晶塔的图标、处理悬停和点击；JS 默认走 PylonLoader 的图标与点击收尾（缺 _MapIcon 文件用游戏的），由管线接。</summary>
        public virtual void DrawMapIcon(ref MapOverlayDrawContext context, ref string mouseOverText, TeleportPylonInfo pylonInfo,
                                        bool isNearPylon, Color drawColor, float deselectedScale, float selectedScale) { }

        /// <summary>画一张图标贴图；返回 true = 手指在它上面。</summary>
        public virtual bool DefaultDrawMapIcon(ref MapOverlayDrawContext context, Texture2D mapIcon, Vector2 drawCenter,
                                               Color drawColor, float deselectedScale, float selectedScale) =>
            context.Draw(mapIcon, drawCenter, drawColor, new SpriteFrame(1, 1, 0, 0), deselectedScale, selectedScale, Alignment.Center).IsMouseOver;

        /// <summary>手指在图标上时把名字写进 mouseOverText；JS 的点击收尾（关地图、RequestTeleportation）在 PylonLoader.ClickIcon，由管线接。</summary>
        public virtual void DefaultMapClickHandle(bool mouseIsHovering, TeleportPylonInfo pylonInfo, string hoveringTextKey, ref string mouseOverText)
        {
            if (mouseIsHovering) mouseOverText = Language.GetTextValue(hoveringTextKey);
        }

        /// <summary>画浮着的晶石（SpecialDraw 里调）；默认实现在参考里是 PylonLoader.DrawCrystal，由管线接。</summary>
        public virtual void DefaultDrawPylonCrystal(SpriteBatch spriteBatch, int i, int j, Texture2D crystalTexture,
                                                    Texture2D crystalHighlightTexture, Vector2 crystalOffset, Color pylonShadowColor,
                                                    Color dustColor, int dustChanceDenominator, int crystalVerticalFrameCount) { }

        /// <summary>点它算处理了；JS 默认还按游戏的姿势打开地图（PylonLoader.OpenMap），由管线接。</summary>
        public override bool RightClick(int i, int j) => true;

        /// <summary>总是智能交互的目标（点它开地图）。</summary>
        public override bool HasSmartInteract(int i, int j, object settings) => true;

        /// <summary>JS 默认在左上角那格要一次 SpecialDraw（Main.instance.TilesRenderer.AddSpecialLegacyPoint，本版是私有方法），由管线接。</summary>
        public override void DrawEffects(int i, int j, SpriteBatch spriteBatch, TileDrawData drawData) { }
    }
}