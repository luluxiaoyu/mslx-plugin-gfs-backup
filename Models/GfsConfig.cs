namespace MSLX.Plugin.Gfs.Backup.Models;

/// <summary>
/// 归档存储模式
/// </summary>
public enum GfsStorageMode
{
    /// <summary>
    /// 硬链接模式（默认）：零磁盘空间占用，原生备份被滚动删除后归档依然完好保留
    /// </summary>
    HardLink = 0,

    /// <summary>
    /// 独立副本模式：物理文件复制，适合归档保存至其他物理磁盘或网络存储
    /// </summary>
    Copy = 1
}

/// <summary>
/// 归档提取时机偏好
/// </summary>
public enum GfsExtractStrategy
{
    /// <summary>
    /// 保留周期内的第一个备份
    /// </summary>
    FirstOfPeriod = 0,

    /// <summary>
    /// 保留周期内的最后一个备份（新备份会覆盖当期归档）
    /// </summary>
    LastOfPeriod = 1
}

/// <summary>
/// 针对单个服务器实例的 GFS 分层归档配置
/// </summary>
public class GfsInstanceConfig
{
    /// <summary>
    /// 是否为当前实例启用 GFS 分层归档
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 存储模式：0 - 硬链接模式 (0 空间占用), 1 - 独立副本拷贝模式
    /// </summary>
    public GfsStorageMode StorageMode { get; set; } = GfsStorageMode.HardLink;

    /// <summary>
    /// 归档提取偏好：0 - 提取首份备份, 1 - 提取最后一份备份
    /// </summary>
    public GfsExtractStrategy Strategy { get; set; } = GfsExtractStrategy.FirstOfPeriod;

    /// <summary>
    /// 日归档保留天数（Son，默认 7 天，范围 1~90 天）
    /// </summary>
    public int KeepDailyDays { get; set; } = 7;

    /// <summary>
    /// 周归档保留周数（Father，默认 4 周，范围 1~52 周）
    /// </summary>
    public int KeepWeeklyWeeks { get; set; } = 4;

    /// <summary>
    /// 月归档保留月数（Grandfather，默认 12 个月，范围 1~36 个月）
    /// </summary>
    public int KeepMonthlyMonths { get; set; } = 12;
}
