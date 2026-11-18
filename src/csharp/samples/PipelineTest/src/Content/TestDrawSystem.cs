using System;
using System.Collections.Generic;
using Il2CppMicrosoft.Xna.Framework;
using Il2CppMicrosoft.Xna.Framework.Graphics;
using Pylon.ModLoader;
using Terraria;
using Terraria.Graphics;
using Terraria.Localization;
using Terraria.Map;

namespace PipelineTest.Content;

/// <summary>
/// 测试系统 B：绘制/网络面。阳光与亮度改写、屏幕位移、矩阵对照、方块与界面绘制计数、
/// 地图层隐藏与恢复、NetSend/NetReceive 往返与 HijackGetData 消费。
/// </summary>
public sealed class TestDrawSystem : ModSystem
{
    public static int Sun, Brightness, Screen, Transforms, Tiles, Interface, MapIcons, FullscreenMap, HijackGets, HijackSends;
    public static bool Tint, Dim, Shift, SameMatrix, BatchSet, ShiftSeen;
    public static float BrightnessBefore;
    public static Vector2 ShiftedTo;
    public static int LayerCount, HiddenWas = -1;
    public static nint HiddenLayer;

    public override void ModifySunLightColor(ref Color tileColor, ref Color backgroundColor)
    {
        Sun++;
        if (!Tint) return;
        tileColor = new Color(10, 20, 30);
        backgroundColor = new Color(40, 50, 60);
    }

    public override void ModifyLightingBrightness(ref float scale)
    {
        Brightness++;
        if (!Dim) return;
        BrightnessBefore = Lighting.GlobalBrightness;
        scale = 0.5f;
    }

    public override void ModifyScreenPosition()
    {
        Screen++;
        if (!Shift) return;
        Main.screenPosition = Main.screenPosition + new Vector2(16f, 0f);
        ShiftedTo = Main.screenPosition;
    }

    public override void ModifyTransformMatrix(ref SpriteViewMatrix Transform)
    {
        Transforms++;
        SameMatrix = Transform.Pointer != 0 && Transform.Pointer == Main.GameViewMatrix.Pointer;
    }

    public override void PostDrawTiles()
    {
        Tiles++;
        if (Shift) ShiftSeen = Main.screenPosition == ShiftedTo;
    }

    public override void PostDrawInterface(SpriteBatch spriteBatch)
    {
        Interface++;
        BatchSet = spriteBatch.Pointer != 0;
    }

    public override void PreDrawMapIconOverlay(IReadOnlyList<IMapLayer> layers, MapOverlayDrawContext mapOverlayDrawContext)
    {
        MapIcons++;
        LayerCount = layers.Count;
        if (layers.Count == 0) return;
        var last = layers[layers.Count - 1];
        HiddenWas = last.GetVisible() ? 1 : 0;
        last.SetVisible(false);
        HiddenLayer = last.GetVisible() ? 0 : last.Pointer;
    }

    public override void PostDrawFullscreenMap(ref string mouseText) => FullscreenMap++;

    public override void NetSend(System.IO.BinaryWriter writer)
    {
        writer.Write((byte)7);
        writer.Write(NetNumber);
    }

    public override void NetReceive(System.IO.BinaryReader reader)
    {
        NetFlag = reader.ReadByte() == 7;
        NetNumber = reader.ReadInt32();
    }

    public override bool HijackGetData(ref byte messageType, ref System.IO.BinaryReader reader, int playerNumber)
    {
        if (messageType != 250) return false;
        HijackGets++;
        HijackType = messageType;
        HijackPayload = $"{reader.ReadByte()},{reader.ReadByte()},{reader.ReadByte()}";
        return true;
    }

    public override bool HijackSendData(int whoAmI, int msgType, int remoteClient, int ignoreClient, NetworkText text,
                                        int number, float number2, float number3, float number4, int number5,
                                        int number6, int number7)
    {
        HijackSends++;
        return false;
    }

    public static int NetNumber;

    public static bool NetFlag;
    public static byte HijackType;
    public static string HijackPayload = "";
}
