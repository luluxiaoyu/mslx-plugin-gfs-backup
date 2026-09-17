using Microsoft.AspNetCore.Mvc.ApplicationParts;
using MSLX.Plugin.Gfs.Backup.Services;
using MSLX.SDK;
using MSLX.SDK.Interfaces;

[assembly: ApplicationPart("MSLX.Plugin.Gfs.Backup")]

namespace MSLX.Plugin.Gfs.Backup;

public class MSLXPluginEntry : IPlugin
{
    public static MSLXPluginEntry Instance { get; private set; } = null!;
    public string Id => "mslx-plugin-gfs-backup"; 
    public string Name => "GFS分层备份";
    public string Description => "解决定时备份容易爆硬盘、无法长久留存的烦恼！自动从日常定时备份中提取出「日备份、周备份、月备份」的长线历史存档。支持 0 额外磁盘占用的黑科技归档，常规备份滚动删除后依然完好保留。";
    public string Version => "1.0.1";
    public string Icon => "icon.png";
    public string MinSDKVersion => "1.6.4";
    public string Developer => "xiaoyu";
    public string AuthorUrl => "https://mslx-plugins.mslmc.net/plugins/mslx-plugin-gfs-backup";
    public string PluginUrl => "https://github.com/luluxiaoyu/mslx-plugin-gfs-backup";

    public void OnPluginInitialize(IServiceProvider serviceProvider)
    {
        Instance = this;
    }

    public void OnLoad()
    {
        try
        {
            SDK.MSLX.Logger.Info("========================================");
            SDK.MSLX.Logger.Info("🚀 [MSLX GFS 分层备份插件] 已成功加载！");
            SDK.MSLX.Logger.Info("已挂载事件总线，监听宿主备份生成流水线...");
            SDK.MSLX.Logger.Info("========================================");

            // ===== 核心事件监听：捕获原生备份完成 =====
            SDK.MSLX.Events.OnBackupCompleted += HandleBackupCompleted;
        }
        catch (Exception ex)
        {
            SDK.MSLX.Logger.Error($"[GFS] 插件初始化失败: {ex.Message}");
        }
    }

    private void HandleBackupCompleted(object? sender, SDK.Events.BackupCompletedEventArgs e)
    {
        // 异步执行归档流程，避免阻塞宿主主执行流
        Task.Run(async () =>
        {
            await GfsArchiveEngine.ProcessBackupCompletedAsync(e);
        });
    }

    public void OnUnload()
    {
        SDK.MSLX.Events.OnBackupCompleted -= HandleBackupCompleted;
        SDK.MSLX.Logger.Info("[MSLX GFS 分层备份插件] 已安全卸载。");
    }

    public void OnRegisterEndpoints(IEndpointRouteBuilder endpoints)
    {
    }

    public void OnRegisterServices(IServiceCollection services)
    {
    }
}
