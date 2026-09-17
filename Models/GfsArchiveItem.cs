namespace MSLX.Plugin.Gfs.Backup.Models;

/// <summary>
/// 归档文件项
/// </summary>
public class GfsArchiveItem
{
    public string FileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string Tier { get; set; } = "daily"; // "daily", "weekly", or "monthly"
    public string PeriodTag { get; set; } = string.Empty; // 如 "2026-09-17", "2026-第38周", "2026-09"
    public long FileSizeBytes { get; set; }
    public string FormattedSize { get; set; } = string.Empty;
    public DateTime CreatedTime { get; set; }
    public string SourceBackupName { get; set; } = string.Empty;
    public bool IsHardLink { get; set; }
}

/// <summary>
/// 原生备份与定时任务联动状态探测结果
/// </summary>
public class NativeBackupStatus
{
    public bool HasCronBackup { get; set; }
    public string CronSummary { get; set; } = string.Empty;
    public int NativeMaxCount { get; set; } = 20;
    public string BackupPath { get; set; } = string.Empty;
}
