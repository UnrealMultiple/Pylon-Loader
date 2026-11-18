namespace Pylon.ModLoader.Content;
/// <summary>一次保存的结果：成功、需要重载、或请求已发给服务器。</summary>
public enum ConfigSaveResult
{
    Success,
    NeedsReload,
    RequestSentToServer,
}
