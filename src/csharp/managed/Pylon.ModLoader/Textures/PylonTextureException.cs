namespace Pylon.ModLoader.Textures;

/// <summary>贴图加载/上传失败（消息里带路径或线程上下文）。</summary>
public sealed class PylonTextureException : Exception
{
    public PylonTextureException(string message) : base(message) { }
}
