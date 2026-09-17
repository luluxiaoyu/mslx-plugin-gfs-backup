using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using MSLX.Plugin.Gfs.Backup.Models;
using MSLX.SDK;
using MSLX.SDK.Events;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MSLX.Plugin.Gfs.Backup.Services;

/// <summary>
/// GFS 分层归档核心调度引擎
/// 负责：配置读写、日/周/月归档提取判定、跨平台安全创建(硬链接/独立副本)、超期滚动清理
/// </summary>
public class GfsArchiveEngine
{
    private static readonly object _lock = new();

    #region P/Invoke 硬链接跨平台实现
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkWindows(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int LinkPosix(string oldpath, string newpath);

    public static bool TryCreateHardLink(string linkPath, string existingPath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return CreateHardLinkWindows(linkPath, existingPath, IntPtr.Zero);
        }
        else
        {
            return LinkPosix(existingPath, linkPath) == 0;
        }
    }
    #endregion

    public static string GetConfigKey(uint instanceId) => $"instance_gfs_config_{instanceId}";

    /// <summary>
    /// 读取指定实例的 GFS 配置
    /// </summary>
    public static GfsInstanceConfig GetConfig(uint instanceId)
    {
        try
        {
            var raw = MSLXPluginEntry.Instance.Config().ReadConfigKey(GetConfigKey(instanceId));
            if (raw == null) return new GfsInstanceConfig();

            if (raw is JObject jObj)
            {
                return jObj.ToObject<GfsInstanceConfig>() ?? new GfsInstanceConfig();
            }
            if (raw is JValue jVal && jVal.Value is string str && !string.IsNullOrWhiteSpace(str))
            {
                return JsonConvert.DeserializeObject<GfsInstanceConfig>(str) ?? new GfsInstanceConfig();
            }
        }
        catch (Exception ex)
        {
            SDK.MSLX.Logger.Warn($"[GFS] 读取实例 [{instanceId}] GFS配置失败: {ex.Message}");
        }

        return new GfsInstanceConfig();
    }

    /// <summary>
    /// 保存指定实例的 GFS 配置
    /// </summary>
    public static void SaveConfig(uint instanceId, GfsInstanceConfig config)
    {
        MSLXPluginEntry.Instance.Config().WriteConfigKey(GetConfigKey(instanceId), JObject.FromObject(config));
    }

    /// <summary>
    /// 计算实例的基础备份目录
    /// </summary>
    public static string GetInstanceBackupDir(uint instanceId, string? serverBase, string? serverBackupPath, string? serverName)
    {
        string backupDir = !string.IsNullOrEmpty(serverBase) 
            ? Path.Combine(serverBase, "mslx-backups") 
            : Path.Combine(AppContext.BaseDirectory, "mslx-backups");

        if (serverBackupPath == "MSLX://Backup/Data")
        {
            string appData = SDK.MSLX.Config.GetAppDataPath();
            backupDir = Path.Combine(appData, "Backups", $"Backups_{serverName}_{instanceId}");
        }
        else if (serverBackupPath != "MSLX://Backup/Instance" && !string.IsNullOrEmpty(serverBackupPath))
        {
            backupDir = serverBackupPath;
        }

        return backupDir;
    }

    /// <summary>
    /// 归档根目录: {InstanceBackupDir}/gfs-archives
    /// </summary>
    public static string GetArchivesBaseDir(string backupDir)
    {
        return Path.Combine(backupDir, "gfs-archives");
    }

    /// <summary>
    /// 核心处理入口：当检测到原生备份完成时异步调用
    /// </summary>
    public static async Task ProcessBackupCompletedAsync(BackupCompletedEventArgs e)
    {
        await Task.CompletedTask;
        try
        {
            var config = GetConfig(e.InstanceId);
            if (!config.Enabled)
            {
                return;
            }

            string sourceFile = e.BackupFilePath;
            if (!File.Exists(sourceFile))
            {
                SDK.MSLX.Logger.Warn($"[GFS] 原生备份文件不存在，跳过归档: {sourceFile}");
                return;
            }

            string backupDir = Path.GetDirectoryName(sourceFile) ?? "";
            string archivesBase = GetArchivesBaseDir(backupDir);
            string dailyDir = Path.Combine(archivesBase, "daily");
            string weeklyDir = Path.Combine(archivesBase, "weekly");
            string monthlyDir = Path.Combine(archivesBase, "monthly");

            Directory.CreateDirectory(dailyDir);
            Directory.CreateDirectory(weeklyDir);
            Directory.CreateDirectory(monthlyDir);

            DateTime now = e.Timestamp;
            string dateTag = now.ToString("yyyyMMdd");
            int weekNum = System.Globalization.ISOWeek.GetWeekOfYear(now);
            int weekYear = System.Globalization.ISOWeek.GetYear(now);
            string weekTag = $"{weekYear}W{weekNum:D2}";
            string monthTag = now.ToString("yyyyMM");

            lock (_lock)
            {
                // ================= 1. 日归档处理 (Son) =================
                bool shouldCreateDaily = false;
                var existingDailyFiles = Directory.GetFiles(dailyDir, $"mslx-backup_daily_{dateTag}_*.zip");

                if (config.Strategy == GfsExtractStrategy.FirstOfPeriod)
                {
                    shouldCreateDaily = existingDailyFiles.Length == 0;
                }
                else
                {
                    foreach (var oldDaily in existingDailyFiles)
                    {
                        try { File.Delete(oldDaily); } catch { }
                    }
                    shouldCreateDaily = true;
                }

                if (shouldCreateDaily)
                {
                    string targetDailyFile = Path.Combine(dailyDir, $"mslx-backup_daily_{dateTag}_{now:HHmmss}.zip");
                    bool success = CreateArchiveFile(sourceFile, targetDailyFile, config.StorageMode, out bool isHardLink);
                    if (success)
                    {
                        SDK.MSLX.Logger.Info($"[GFS分层备份] 实例 [{e.InstanceId}] 成功提取日归档: {Path.GetFileName(targetDailyFile)} (模式: {(isHardLink ? "硬链接/零空间占用" : "独立副本")})");
                    }
                }

                // ================= 2. 周归档处理 (Father) =================
                bool shouldCreateWeekly = false;
                var existingWeeklyFiles = Directory.GetFiles(weeklyDir, $"mslx-backup_weekly_{weekTag}_*.zip");

                if (config.Strategy == GfsExtractStrategy.FirstOfPeriod)
                {
                    shouldCreateWeekly = existingWeeklyFiles.Length == 0;
                }
                else
                {
                    foreach (var oldWeekly in existingWeeklyFiles)
                    {
                        try { File.Delete(oldWeekly); } catch { }
                    }
                    shouldCreateWeekly = true;
                }

                if (shouldCreateWeekly)
                {
                    string targetWeeklyFile = Path.Combine(weeklyDir, $"mslx-backup_weekly_{weekTag}_{now:MMddHHmm}.zip");
                    bool success = CreateArchiveFile(sourceFile, targetWeeklyFile, config.StorageMode, out bool isHardLink);
                    if (success)
                    {
                        SDK.MSLX.Logger.Info($"[GFS分层备份] 实例 [{e.InstanceId}] 成功提取周归档: {Path.GetFileName(targetWeeklyFile)} (模式: {(isHardLink ? "硬链接/零空间占用" : "独立副本")})");
                    }
                }

                // ================= 3. 月归档处理 (Grandfather) =================
                bool shouldCreateMonthly = false;
                var existingMonthlyFiles = Directory.GetFiles(monthlyDir, $"mslx-backup_monthly_{monthTag}_*.zip");

                if (config.Strategy == GfsExtractStrategy.FirstOfPeriod)
                {
                    shouldCreateMonthly = existingMonthlyFiles.Length == 0;
                }
                else
                {
                    foreach (var oldMonthly in existingMonthlyFiles)
                    {
                        try { File.Delete(oldMonthly); } catch { }
                    }
                    shouldCreateMonthly = true;
                }

                if (shouldCreateMonthly)
                {
                    string targetMonthlyFile = Path.Combine(monthlyDir, $"mslx-backup_monthly_{monthTag}_{now:ddHHmm}.zip");
                    bool success = CreateArchiveFile(sourceFile, targetMonthlyFile, config.StorageMode, out bool isHardLink);
                    if (success)
                    {
                        SDK.MSLX.Logger.Info($"[GFS分层备份] 实例 [{e.InstanceId}] 成功提取月归档: {Path.GetFileName(targetMonthlyFile)} (模式: {(isHardLink ? "硬链接/零空间占用" : "独立副本")})");
                    }
                }

                // ================= 生命周期滚动清理 =================
                PurgeExpiredArchives(dailyDir, weeklyDir, monthlyDir, config.KeepDailyDays, config.KeepWeeklyWeeks, config.KeepMonthlyMonths);
            }
        }
        catch (Exception ex)
        {
            SDK.MSLX.Logger.Error($"[GFS] 归档流水线执行异常: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 跨平台安全创建归档（硬链接优先 / 跨盘透明降级为独立副本）
    /// </summary>
    public static bool CreateArchiveFile(string sourceFile, string destFile, GfsStorageMode mode, out bool isHardLink)
    {
        isHardLink = false;
        string? destDir = Path.GetDirectoryName(destFile);
        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        if (File.Exists(destFile))
        {
            try { File.Delete(destFile); } catch { }
        }

        if (mode == GfsStorageMode.HardLink)
        {
            try
            {
                if (TryCreateHardLink(destFile, sourceFile))
                {
                    isHardLink = true;
                    return true;
                }
                else
                {
                    SDK.MSLX.Logger.Warn($"[GFS] 创建硬链接未成功 (可能跨文件系统或系统不受支持)，安全自动降级为文件复制。");
                }
            }
            catch (Exception ex)
            {
                SDK.MSLX.Logger.Warn($"[GFS] 硬链接异常，降级为文件复制: {ex.Message}");
            }
        }

        // 降级或者选择独立副本模式
        try
        {
            File.Copy(sourceFile, destFile, true);
            isHardLink = false;
            return true;
        }
        catch (Exception ex)
        {
            SDK.MSLX.Logger.Error($"[GFS] 复制归档文件失败: {ex.Message}", ex);
            return false;
        }
    }

    /// <summary>
    /// 滚动清理超出最大保留份数的归档文件
    /// </summary>
    public static void PurgeExpiredArchives(string dailyDir, string weeklyDir, string monthlyDir, int keepDailyCount, int keepWeeklyCount, int keepMonthlyCount)
    {
        void PurgeDirectory(string dir, string pattern, int maxCount, string label)
        {
            if (!Directory.Exists(dir) || maxCount <= 0) return;
            var files = new DirectoryInfo(dir).GetFiles(pattern)
                .OrderByDescending(f => f.CreationTime)
                .ToList();

            if (files.Count > maxCount)
            {
                var expiredFiles = files.Skip(maxCount);
                foreach (var f in expiredFiles)
                {
                    try
                    {
                        f.Delete();
                        SDK.MSLX.Logger.Info($"[GFS生命周期] 自动清理超期{label}: {f.Name} (保留最近 {maxCount} 份)");
                    }
                    catch (Exception ex)
                    {
                        SDK.MSLX.Logger.Warn($"[GFS] 清理{label} {f.Name} 失败: {ex.Message}");
                    }
                }
            }
        }

        // 1. 清理日备份
        PurgeDirectory(dailyDir, "mslx-backup_daily_*.zip", keepDailyCount, "日备份");

        // 2. 清理周备份
        PurgeDirectory(weeklyDir, "mslx-backup_weekly_*.zip", keepWeeklyCount, "周备份");

        // 3. 清理月备份
        PurgeDirectory(monthlyDir, "mslx-backup_monthly_*.zip", keepMonthlyCount, "月备份");
    }

    /// <summary>
    /// 检索实例的所有归档列表
    /// </summary>
    public static List<GfsArchiveItem> GetArchiveItems(string backupDir)
    {
        var items = new List<GfsArchiveItem>();
        string archivesBase = GetArchivesBaseDir(backupDir);

        void ScanTier(string subFolder, string tierName)
        {
            string tierDir = Path.Combine(archivesBase, subFolder);
            if (!Directory.Exists(tierDir)) return;

            var dirInfo = new DirectoryInfo(tierDir);
            foreach (var file in dirInfo.GetFiles("mslx-backup_*.zip"))
            {
                long len = file.Length;
                string formattedSize = len switch
                {
                    >= 1073741824 => $"{len / 1024.0 / 1024.0 / 1024.0:F2} GB",
                    >= 1048576 => $"{len / 1024.0 / 1024.0:F2} MB",
                    >= 1024 => $"{len / 1024.0:F2} KB",
                    _ => $"{len} Bytes"
                };

                string periodTag = tierName switch
                {
                    "daily" => Regex.Match(file.Name, @"mslx-backup_daily_(\d{4})(\d{2})(\d{2})_").Groups is var g1 && g1[1].Success 
                        ? $"{g1[1].Value}-{g1[2].Value}-{g1[3].Value}" 
                        : file.CreationTime.ToString("yyyy-MM-dd"),
                    "weekly" => Regex.Match(file.Name, @"mslx-backup_weekly_(\d{4})W(\d{2})_").Groups is var g2 && g2[1].Success 
                        ? $"{g2[1].Value}-W{g2[2].Value} (第{int.Parse(g2[2].Value)}周)" 
                        : $"{file.CreationTime.Year}-W{System.Globalization.ISOWeek.GetWeekOfYear(file.CreationTime):D2}",
                    "monthly" => Regex.Match(file.Name, @"mslx-backup_monthly_(\d{4})(\d{2})_").Groups is var g3 && g3[1].Success 
                        ? $"{g3[1].Value}-{g3[2].Value} ({int.Parse(g3[2].Value)}月)" 
                        : file.CreationTime.ToString("yyyy-MM"),
                    _ => string.Empty
                };

                items.Add(new GfsArchiveItem
                {
                    FileName = file.Name,
                    RelativePath = Path.Combine(subFolder, file.Name),
                    Tier = tierName,
                    PeriodTag = periodTag,
                    FileSizeBytes = len,
                    FormattedSize = formattedSize,
                    CreatedTime = file.CreationTime,
                    SourceBackupName = file.Name,
                    IsHardLink = true
                });
            }
        }

        ScanTier("daily", "daily");
        ScanTier("weekly", "weekly");
        ScanTier("monthly", "monthly");

        return items.OrderByDescending(x => x.CreatedTime).ToList();
    }
}
