public static class ArraySyntaxRegression
{
    public static void Apply(Terraria.Projectile proj, float value)
    {
        proj.ai[1] = 0f;
        proj.localAI[1] += 1f;
        proj.oldPos[1].X = value;
        var ai = proj.ai;
        ai[1] *= 2f;
        var positions = proj.oldPos;
        positions[1].Y += value;
        System.GC.KeepAlive(proj); // owner must outlive any native ref use
    }
}
