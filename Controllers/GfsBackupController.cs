using Microsoft.AspNetCore.Mvc;
using MSLX.Plugin.Gfs.Backup.Models;
using MSLX.Plugin.Gfs.Backup.Services;
using MSLX.SDK;
using MSLX.SDK.Models;
using Newtonsoft.Json.Linq;

namespace MSLX.Plugin.Gfs.Backup.Controllers;

[ApiController]
[Route("api/plugins/mslx-plugin-gfs-backup")]
public class GfsBackupController : ControllerBase
{
    /// <summary>
    /// 获取当前实例的 GFS 配置
    /// </summary>
    [HttpGet("config")]
    public IActionResult GetConfig([FromQuery] uint instanceId)
    {
        var config = GfsArchiveEngine.GetConfig(instanceId);
        return Ok(new ApiResponse<GfsInstanceConfig>
        {
            Code = 200,
            Message = "获取成功",
            Data = config
        });
    }

    /// <summary>
    /// 保存当前实例的 GFS 配置
    /// </summary>
    [HttpPost("config")]
    public IActionResult SaveConfig([FromQuery] uint instanceId, [FromBody] GfsInstanceConfig config)
    {
        if (config.KeepDailyDays < 1) config.KeepDailyDays = 1;
        if (config.KeepWeeklyWeeks < 1) config.KeepWeeklyWeeks = 1;
        if (config.KeepMonthlyMonths < 1) config.KeepMonthlyMonths = 1;

        GfsArchiveEngine.SaveConfig(instanceId, config);
        return Ok(new ApiResponse<object>
        {
            Code = 200,
            Message = "GFS 分层归档策略已保存",
            Data = config
        });
    }

    /// <summary>
    /// 获取原生备份和定时任务联动状态（用于驱动前端引导横幅）
    /// </summary>
    [HttpGet("status")]
    public IActionResult GetNativeStatus([FromQuery] uint instanceId)
    {
        var server = SDK.MSLX.Config.Servers.GetServer(instanceId);
        if (server == null)
        {
            return NotFound(new ApiResponse<object> { Code = 404, Message = "找不到指定的实例" });
        }

        var status = new NativeBackupStatus
        {
            NativeMaxCount = server.BackupMaxCount > 0 ? server.BackupMaxCount : 20,
            BackupPath = GfsArchiveEngine.GetInstanceBackupDir(instanceId, server.Base, server.BackupPath, server.Name)
        };

        try
        {
            // 通过 SDK.MSLX.Config.Tasks 获取定时任务
            var tasks = SDK.MSLX.Config.Tasks.GetTasksByInstanceId(instanceId);
            var backupTasks = tasks.Where(t => t.Type.Equals("backup", StringComparison.OrdinalIgnoreCase) && t.Enable).ToList();

            if (backupTasks.Any())
            {
                status.HasCronBackup = true;
                var firstTask = backupTasks.First();
                status.CronSummary = $"{firstTask.Name} ({firstTask.Cron})";
            }
            else
            {
                status.HasCronBackup = false;
                status.CronSummary = "未配置定时备份";
            }
        }
        catch (Exception ex)
        {
            SDK.MSLX.Logger.Warn($"[GFS] 探测原生备份状态失败: {ex.Message}");
        }

        return Ok(new ApiResponse<NativeBackupStatus>
        {
            Code = 200,
            Message = "获取成功",
            Data = status
        });
    }

    /// <summary>
    /// 获取当前实例的所有 GFS 归档文件清单
    /// </summary>
    [HttpGet("archives")]
    public IActionResult GetArchives([FromQuery] uint instanceId)
    {
        var server = SDK.MSLX.Config.Servers.GetServer(instanceId);
        if (server == null)
        {
            return NotFound(new ApiResponse<object> { Code = 404, Message = "找不到指定的实例" });
        }

        string backupDir = GfsArchiveEngine.GetInstanceBackupDir(instanceId, server.Base, server.BackupPath, server.Name);
        var items = GfsArchiveEngine.GetArchiveItems(backupDir);

        return Ok(new ApiResponse<List<GfsArchiveItem>>
        {
            Code = 200,
            Message = "获取成功",
            Data = items
        });
    }

    /// <summary>
    /// 删除指定归档文件
    /// </summary>
    [HttpPost("delete-archive")]
    public IActionResult DeleteArchive([FromQuery] uint instanceId, [FromBody] JObject req)
    {
        string relativePath = req["relativePath"]?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = "相对路径不能为空" });
        }

        var server = SDK.MSLX.Config.Servers.GetServer(instanceId);
        if (server == null)
        {
            return NotFound(new ApiResponse<object> { Code = 404, Message = "找不到指定的实例" });
        }

        string backupDir = GfsArchiveEngine.GetInstanceBackupDir(instanceId, server.Base, server.BackupPath, server.Name);
        string archivesBase = GfsArchiveEngine.GetArchivesBaseDir(backupDir);

        string safeTarget = Path.GetFullPath(Path.Combine(archivesBase, relativePath));
        if (!safeTarget.StartsWith(Path.GetFullPath(archivesBase), StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new ApiResponse<object> { Code = 400, Message = "非法的文件路径" });
        }

        if (System.IO.File.Exists(safeTarget))
        {
            System.IO.File.Delete(safeTarget);
            return Ok(new ApiResponse<object> { Code = 200, Message = "归档文件已删除" });
        }

        return NotFound(new ApiResponse<object> { Code = 404, Message = "归档文件不存在" });
    }

    /// <summary>
    /// 手动执行一次过期滚动清理
    /// </summary>
    [HttpPost("purge")]
    public IActionResult PurgeArchives([FromQuery] uint instanceId)
    {
        var server = SDK.MSLX.Config.Servers.GetServer(instanceId);
        if (server == null)
        {
            return NotFound(new ApiResponse<object> { Code = 404, Message = "找不到指定的实例" });
        }

        var config = GfsArchiveEngine.GetConfig(instanceId);
        string backupDir = GfsArchiveEngine.GetInstanceBackupDir(instanceId, server.Base, server.BackupPath, server.Name);
        string archivesBase = GfsArchiveEngine.GetArchivesBaseDir(backupDir);

        GfsArchiveEngine.PurgeExpiredArchives(
            Path.Combine(archivesBase, "daily"),
            Path.Combine(archivesBase, "weekly"),
            Path.Combine(archivesBase, "monthly"),
            config.KeepDailyDays,
            config.KeepWeeklyWeeks,
            config.KeepMonthlyMonths
        );

        return Ok(new ApiResponse<object> { Code = 200, Message = "生命周期清理执行完毕" });
    }
}
