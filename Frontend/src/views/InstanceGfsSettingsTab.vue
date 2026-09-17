<script setup lang="ts">
import { ref, onMounted, computed, watch } from 'vue';
import { MessagePlugin, DialogPlugin } from 'tdesign-vue-next';
import {
  RefreshIcon,
  DeleteIcon,
  HelpCircleIcon,
  CheckCircleFilledIcon,
  ErrorCircleFilledIcon,
  ClearIcon,
  FileIcon,
  TimeIcon,
  SearchIcon,
  CalendarIcon,
} from 'tdesign-icons-vue-next';
import request from 'mslx-request';

const props = defineProps<{
  serverId?: number;
  instanceId?: number;
}>();

const emits = defineEmits<{
  (e: 'success'): void;
  (e: 'saved'): void;
}>();

const currentInstanceId = computed(() => props.instanceId || props.serverId || 0);

// 数据加载状态
const loading = ref(false);
const saving = ref(false);
const archivesLoading = ref(false);
const purging = ref(false);

// 原生备份状态探测
const nativeStatus = ref<{
  hasCronBackup: boolean;
  cronSummary: string;
  nativeMaxCount: number;
  backupPath: string;
}>({
  hasCronBackup: false,
  cronSummary: '检测中...',
  nativeMaxCount: 20,
  backupPath: '',
});

// GFS 表单配置：日 (Son)、周 (Father)、月 (Grandfather)
const formData = ref({
  enabled: false,
  storageMode: 0, // 0: HardLink (硬链接/0空间), 1: Copy (独立副本)
  strategy: 0,    // 0: FirstOfPeriod (首个), 1: LastOfPeriod (末个)
  keepDailyDays: 7,
  keepWeeklyWeeks: 4,
  keepMonthlyMonths: 12,
});

// 归档清单
interface ArchiveItem {
  fileName: string;
  relativePath: string;
  tier: 'daily' | 'weekly' | 'monthly' | string;
  periodTag?: string;
  fileSizeBytes: number;
  formattedSize: string;
  createdTime: string;
  sourceBackupName: string;
  isHardLink: boolean;
}
const archiveList = ref<ArchiveItem[]>([]);

// 分类 Tab 与搜索过滤
const currentTab = ref<string>('all');
const searchKeyword = ref('');

// 统计数据
const dailyCount = computed(() => archiveList.value.filter(x => x.tier === 'daily').length);
const weeklyCount = computed(() => archiveList.value.filter(x => x.tier === 'weekly').length);
const monthlyCount = computed(() => archiveList.value.filter(x => x.tier === 'monthly').length);

const totalSizeStr = computed(() => {
  const bytes = archiveList.value.reduce((acc, cur) => acc + (cur.fileSizeBytes || 0), 0);
  if (bytes >= 1073741824) return (bytes / (1024 * 1024 * 1024)).toFixed(2) + ' GB';
  if (bytes >= 1048576) return (bytes / (1024 * 1024)).toFixed(2) + ' MB';
  if (bytes >= 1024) return (bytes / 1024).toFixed(2) + ' KB';
  return bytes + ' B';
});

// 过滤后的归档列表
const filteredArchiveList = computed(() => {
  let list = archiveList.value;
  if (currentTab.value !== 'all') {
    list = list.filter(item => item.tier === currentTab.value);
  }
  if (searchKeyword.value.trim()) {
    const q = searchKeyword.value.trim().toLowerCase();
    list = list.filter(item =>
      item.fileName.toLowerCase().includes(q) ||
      (item.periodTag && item.periodTag.toLowerCase().includes(q))
    );
  }
  return list;
});

// 表格列定义
const columns = [
  { colKey: 'tier', title: '备份类型', width: 100 },
  { colKey: 'periodTag', title: '归档周期', width: 160 },
  { colKey: 'fileName', title: '文件名', ellipsis: true },
  { colKey: 'formattedSize', title: '文件大小', width: 100 },
  { colKey: 'createdTime', title: '备份时间', width: 170 },
  { colKey: 'op', title: '操作', width: 80, fixed: 'right' },
];

// 初始化拉取数据
const initData = async () => {
  if (!currentInstanceId.value) return;
  loading.value = true;
  try {
    await Promise.all([fetchConfig(), fetchNativeStatus(), fetchArchives()]);
  } finally {
    loading.value = false;
  }
};

const fetchConfig = async () => {
  try {
    const res: any = await request.get({
      url: `/api/plugins/mslx-plugin-gfs-backup/config?instanceId=${currentInstanceId.value}`,
    });
    if (res) {
      formData.value = {
        enabled: !!res.enabled,
        storageMode: res.storageMode ?? 0,
        strategy: res.strategy ?? 0,
        keepDailyDays: res.keepDailyDays ?? 7,
        keepWeeklyWeeks: res.keepWeeklyWeeks ?? 4,
        keepMonthlyMonths: res.keepMonthlyMonths ?? 12,
      };
    }
  } catch (err: any) {
    console.error('[GFS] 拉取配置失败:', err);
  }
};

const fetchNativeStatus = async () => {
  try {
    const res: any = await request.get({
      url: `/api/plugins/mslx-plugin-gfs-backup/status?instanceId=${currentInstanceId.value}`,
    });
    if (res) {
      nativeStatus.value = res;
    }
  } catch (err: any) {
    console.error('[GFS] 探测原生备份失败:', err);
  }
};

const fetchArchives = async () => {
  archivesLoading.value = true;
  try {
    const res: any = await request.get({
      url: `/api/plugins/mslx-plugin-gfs-backup/archives?instanceId=${currentInstanceId.value}`,
    });
    archiveList.value = res || [];
  } catch (err: any) {
    console.error('[GFS] 拉取备份列表失败:', err);
  } finally {
    archivesLoading.value = false;
  }
};

// 保存配置
const handleSave = async () => {
  saving.value = true;
  try {
    await request.post({
      url: `/api/plugins/mslx-plugin-gfs-backup/config?instanceId=${currentInstanceId.value}`,
      data: formData.value,
    });
    MessagePlugin.success('分级备份策略配置已保存');
    emits('saved');
    emits('success');
  } catch (err: any) {
    MessagePlugin.error(err.message || '保存配置失败');
  } finally {
    saving.value = false;
  }
};

// 手动清理过期
const handlePurge = async () => {
  purging.value = true;
  try {
    await request.post({
      url: `/api/plugins/mslx-plugin-gfs-backup/purge?instanceId=${currentInstanceId.value}`,
    });
    MessagePlugin.success('超额过期备份清理完毕');
    await fetchArchives();
  } catch (err: any) {
    MessagePlugin.error(err.message || '清理失败');
  } finally {
    purging.value = false;
  }
};

// 删除单个归档
const handleDeleteArchive = (row: ArchiveItem) => {
  const tierName = row.tier === 'daily' ? '日备份' : row.tier === 'weekly' ? '周备份' : '月备份';
  const confirmDialog = DialogPlugin.confirm({
    header: '确认删除备份文件?',
    body: `您确定要永久删除 ${tierName} "${row.fileName}" 吗？此操作不可恢复。`,
    theme: 'danger',
    onConfirm: async () => {
      confirmDialog.hide();
      try {
        await request.post({
          url: `/api/plugins/mslx-plugin-gfs-backup/delete-archive?instanceId=${currentInstanceId.value}`,
          data: { relativePath: row.relativePath },
        });
        MessagePlugin.success('备份文件已删除');
        await fetchArchives();
      } catch (err: any) {
        MessagePlugin.error(err.message || '删除失败');
      }
    },
    onClose: () => confirmDialog.hide(),
  });
};

watch(() => currentInstanceId.value, () => initData());
onMounted(() => initData());
</script>

<template>
  <div class="flex flex-col mx-auto w-full pb-8">

    <!-- 顶部 Section 标头：完全匹配宿主样式 -->
    <div class="flex items-center gap-2 mt-5 mb-4 pb-2 border-b border-zinc-200/60 dark:border-zinc-700/60">
      <div class="w-1 h-4 bg-[var(--color-primary)] rounded-full"></div>
      <h2 class="text-base font-bold text-[var(--td-text-color-primary)] m-0">分级备份设置</h2>
    </div>

    <!-- 原生备份联动引导横幅 -->
    <div
      v-if="!nativeStatus.hasCronBackup"
      class="flex flex-col md:flex-row md:items-center justify-between p-3.5 md:p-4 mb-4 rounded-xl border border-amber-300/80 bg-amber-50/70 dark:bg-amber-950/30 dark:border-amber-700/50 gap-3 shadow-xs"
    >
      <div class="flex items-start gap-3">
        <error-circle-filled-icon class="text-xl text-amber-500 shrink-0 mt-0.5" />
        <div class="flex flex-col text-xs leading-relaxed">
          <span class="font-bold text-amber-900 dark:text-amber-200 text-sm">未检测到定时备份任务，请先设置原生备份！</span>
          <span class="text-amber-700 dark:text-amber-400 mt-0.5">
            分级备份依靠定时任务生成的基础备份文件进行长线归档。请在左侧 <b>「定时任务」</b> 添加一个「备份存档 (Backup)」任务（建议每 2~4 小时执行一次）。
          </span>
        </div>
      </div>
    </div>

    <div
      v-else
      class="flex items-center gap-3 p-3 px-4 mb-4 rounded-xl border border-emerald-300/80 bg-emerald-50/70 dark:bg-emerald-950/30 dark:border-emerald-700/50 text-xs shadow-xs"
    >
      <check-circle-filled-icon class="text-base text-emerald-500 shrink-0" />
      <div class="flex-1 text-emerald-800 dark:text-emerald-300">
        <b>原生备份已就绪</b>：检测到定时任务「{{ nativeStatus.cronSummary }}」，常规滚动保留 {{ nativeStatus.nativeMaxCount }} 份。插件将自动为您提取并长期保留「日备份、周备份、月备份」。
      </div>
    </div>

    <!-- 表单区域：按宿主 GeneralSettings 行模式渲染 -->
    <div class="flex flex-col">

      <!-- 功能开关行 -->
      <div class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl">
        <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
          <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">分级备份功能</div>
          <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
            启用后将在每次备份完成时，自动按周期归档日备份、周备份、月备份
          </div>
        </div>
        <div class="w-full md:w-[340px] shrink-0 flex items-center justify-end">
          <t-switch v-model="formData.enabled" size="large" />
        </div>
      </div>

      <template v-if="formData.enabled">

        <!-- 存储模式行 -->
        <div class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl">
          <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
            <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug flex items-center gap-1.5">
              备份存储模式
              <t-tooltip content="硬链接模式依靠底层文件系统 Inode 引用计数。即使原生备份由于达到最大数量被自动清理，归档仍完好无损且 0 额外磁盘空间；若归档路径需存放于外部挂载盘或网络存储，请选独立副本模式。">
                <span class="text-xs text-zinc-400 hover:text-zinc-500 cursor-help flex items-center gap-1">
                  <help-circle-icon />
                </span>
              </t-tooltip>
            </div>
            <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
              全面兼容 Windows NTFS、Linux ext4/xfs、macOS APFS 硬链接
            </div>
          </div>
          <div class="w-full md:w-[340px] shrink-0 flex items-center justify-start md:justify-end">
            <t-radio-group v-model="formData.storageMode" variant="default-filled">
              <t-radio-button :value="0">硬链接 (0空间占用/推荐)</t-radio-button>
              <t-radio-button :value="1">独立副本拷贝</t-radio-button>
            </t-radio-group>
          </div>
        </div>

        <!-- 提取时机行 -->
        <div class="flex flex-col md:flex-row md:items-start justify-between p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 hover:bg-zinc-50/50 dark:hover:bg-zinc-800/20 transition-colors rounded-xl">
          <div class="flex-1 pr-0 md:pr-8 mb-3 md:mb-0 min-w-[200px]">
            <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">周期内快照选取偏好</div>
            <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
              选择保留当期首个备份（保留早期状态），或覆盖保留当期最后一个最新备份
            </div>
          </div>
          <div class="w-full md:w-[340px] shrink-0 flex items-center justify-start md:justify-end">
            <t-radio-group v-model="formData.strategy" variant="default-filled">
              <t-radio-button :value="0">提取首个备份</t-radio-button>
              <t-radio-button :value="1">提取最新备份</t-radio-button>
            </t-radio-group>
          </div>
        </div>

        <!-- 保留份数：周 / 日 / 月 独立清晰设置 -->
        <div class="p-3 md:p-4 border-b border-dashed border-zinc-100 dark:border-zinc-800/60 rounded-xl">
          <div class="mb-3">
            <div class="text-sm font-medium text-[var(--td-text-color-primary)] leading-snug">
              备份保留份数
            </div>
            <div class="text-xs text-[var(--td-text-color-secondary)] mt-1 leading-relaxed">
              分别设置日备份、周备份、月备份各自的最大保留数量。超出设定份数后，系统将自动滚动删除最旧的一份。
            </div>
          </div>

          <!-- 三层保留份数卡片 -->
          <div class="grid grid-cols-1 md:grid-cols-3 gap-3 my-2">

            <!-- 1. 日备份 -->
            <div class="p-3.5 rounded-xl border border-zinc-200/70 dark:border-zinc-700/60 bg-white/60 dark:bg-zinc-800/40 flex flex-col justify-between">
              <div>
                <div class="flex items-center justify-between mb-1.5">
                  <span class="text-xs font-bold text-[var(--color-primary)] flex items-center gap-1.5">
                    <calendar-icon /> 日备份
                  </span>
                  <t-tag theme="primary" variant="light" size="small">每天 1 份</t-tag>
                </div>
                <div class="text-[11px] text-[var(--td-text-color-secondary)] leading-relaxed mb-3">
                  每天自动保存 1 份快照，应对近期误操作、崩溃或即时回退需求。
                </div>
              </div>
              <div class="flex items-center justify-between pt-2 border-t border-zinc-100 dark:border-zinc-700/40">
                <span class="text-xs text-zinc-600 dark:text-zinc-300">最大保留份数</span>
                <div class="w-[120px]">
                  <t-input-number
                    v-model="formData.keepDailyDays"
                    :min="1"
                    :max="365"
                    size="small"
                    theme="column"
                    suffix="份"
                  />
                </div>
              </div>
            </div>

            <!-- 2. 周备份 -->
            <div class="p-3.5 rounded-xl border border-zinc-200/70 dark:border-zinc-700/60 bg-white/60 dark:bg-zinc-800/40 flex flex-col justify-between">
              <div>
                <div class="flex items-center justify-between mb-1.5">
                  <span class="text-xs font-bold text-emerald-600 dark:text-emerald-400 flex items-center gap-1.5">
                    <calendar-icon /> 周备份
                  </span>
                  <t-tag theme="success" variant="light" size="small">每周 1 份</t-tag>
                </div>
                <div class="text-[11px] text-[var(--td-text-color-secondary)] leading-relaxed mb-3">
                  每周自动保存 1 份快照，应对中期地图坏档或长时间滞后的异常排查。
                </div>
              </div>
              <div class="flex items-center justify-between pt-2 border-t border-zinc-100 dark:border-zinc-700/40">
                <span class="text-xs text-zinc-600 dark:text-zinc-300">最大保留份数</span>
                <div class="w-[120px]">
                  <t-input-number
                    v-model="formData.keepWeeklyWeeks"
                    :min="1"
                    :max="365"
                    size="small"
                    theme="column"
                    suffix="份"
                  />
                </div>
              </div>
            </div>

            <!-- 3. 月备份 -->
            <div class="p-3.5 rounded-xl border border-zinc-200/70 dark:border-zinc-700/60 bg-white/60 dark:bg-zinc-800/40 flex flex-col justify-between">
              <div>
                <div class="flex items-center justify-between mb-1.5">
                  <span class="text-xs font-bold text-amber-600 dark:text-amber-400 flex items-center gap-1.5">
                    <calendar-icon /> 月备份
                  </span>
                  <t-tag theme="warning" variant="light" size="small">每月 1 份</t-tag>
                </div>
                <div class="text-[11px] text-[var(--td-text-color-secondary)] leading-relaxed mb-3">
                  每月自动保存 1 份长线快照，作为长线季度服封存或周年历史存档。
                </div>
              </div>
              <div class="flex items-center justify-between pt-2 border-t border-zinc-100 dark:border-zinc-700/40">
                <span class="text-xs text-zinc-600 dark:text-zinc-300">最大保留份数</span>
                <div class="w-[120px]">
                  <t-input-number
                    v-model="formData.keepMonthlyMonths"
                    :min="1"
                    :max="365"
                    size="small"
                    theme="column"
                    suffix="份"
                  />
                </div>
              </div>
            </div>

          </div>

          <!-- 策略直观规划透视总结 -->
          <div class="mt-2.5 p-3 rounded-lg bg-zinc-100/70 dark:bg-zinc-800/60 text-xs text-[var(--td-text-color-secondary)] leading-relaxed flex items-center gap-2">
            <span class="text-base">💡</span>
            <div>
              <b>保留规划总结</b>：当前策略将为您保留最近
              <b class="text-[var(--color-primary)]">{{ formData.keepDailyDays }}</b> 份日备份 + 最近
              <b class="text-emerald-600 dark:text-emerald-400">{{ formData.keepWeeklyWeeks }}</b> 份周备份 + 最近
              <b class="text-amber-600 dark:text-amber-400">{{ formData.keepMonthlyMonths }}</b> 份月备份。
              <span v-if="formData.storageMode === 0" class="text-emerald-600 dark:text-emerald-400 font-medium">
                (硬链接模式下，重叠快照复用磁盘数据块，不消耗额外存储空间)
              </span>
            </div>
          </div>
        </div>

        <!-- 保存按钮行 -->
        <div class="flex justify-end p-3 md:p-4">
          <t-button theme="primary" :loading="saving" class="!rounded-lg shadow-sm" @click="handleSave">
            保存备份配置
          </t-button>
        </div>

      </template>

    </div>

    <!-- 备份列表标头：完全复用 BackupManager 布局 -->
    <div class="flex items-center gap-2 mt-8 mb-4 pb-2 border-b border-zinc-200/60 dark:border-zinc-700/60">
      <div class="w-1 h-4 bg-[var(--color-primary)] rounded-full"></div>
      <h2 class="text-base font-bold text-[var(--td-text-color-primary)] m-0">历史备份管理</h2>
    </div>

    <div class="flex flex-col md:flex-row md:justify-between md:items-center py-2 pr-0 md:pr-2 gap-3">
      <div class="flex-1 min-w-[200px]">
        <div class="text-sm font-bold text-[var(--td-text-color-primary)] leading-snug">备份文件列表</div>
        <div class="text-xs text-[var(--td-text-color-secondary)] mt-0.5 leading-relaxed">
          总计已保留 <b class="text-zinc-700 dark:text-zinc-300">{{ archiveList.length }}</b> 份备份（预估占用空间: {{ totalSizeStr }}）。
        </div>
      </div>

      <div class="shrink-0 flex items-center gap-2">
        <t-input
          v-model="searchKeyword"
          placeholder="搜索文件名或周期..."
          clearable
          size="small"
          class="!w-48"
        >
          <template #prefix-icon><search-icon /></template>
        </t-input>

        <t-button theme="default" variant="outline" size="small" class="!rounded-lg shadow-sm" :loading="purging" @click="handlePurge">
          <template #icon><clear-icon /></template> 清理过期备份
        </t-button>
        <t-button theme="primary" variant="outline" size="small" class="!rounded-lg shadow-sm" :loading="archivesLoading" @click="fetchArchives">
          <template #icon><refresh-icon /></template> 刷新
        </t-button>
      </div>
    </div>

    <!-- 列表分类 Tabs：彻底解决“只显示一个标签找起来麻烦”的问题 -->
    <div class="mt-3">
      <t-tabs v-model="currentTab" theme="card" size="medium">
        <t-tab-panel value="all">
          <template #label>
            <div class="flex items-center gap-1.5">
              <span>全部备份</span>
              <t-tag size="small" variant="light" theme="default">{{ archiveList.length }}</t-tag>
            </div>
          </template>
        </t-tab-panel>

        <t-tab-panel value="daily">
          <template #label>
            <div class="flex items-center gap-1.5">
              <span>日备份</span>
              <t-tag size="small" variant="light" theme="primary">{{ dailyCount }}</t-tag>
            </div>
          </template>
        </t-tab-panel>

        <t-tab-panel value="weekly">
          <template #label>
            <div class="flex items-center gap-1.5">
              <span>周备份</span>
              <t-tag size="small" variant="light" theme="success">{{ weeklyCount }}</t-tag>
            </div>
          </template>
        </t-tab-panel>

        <t-tab-panel value="monthly">
          <template #label>
            <div class="flex items-center gap-1.5">
              <span>月备份</span>
              <t-tag size="small" variant="light" theme="warning">{{ monthlyCount }}</t-tag>
            </div>
          </template>
        </t-tab-panel>
      </t-tabs>
    </div>

    <!-- 表格容器：完全统一 BackupManager 样式 -->
    <div class="mt-2 border border-zinc-200/60 dark:border-zinc-700/60 rounded-xl overflow-hidden shadow-sm bg-white/50 dark:bg-zinc-900/20">
      <t-table
        row-key="relativePath"
        :data="filteredArchiveList"
        :columns="columns"
        :loading="archivesLoading"
        stripe
        hover
        size="small"
      >
        <template #tier="{ row }">
          <div class="flex items-center">
            <t-tag
              v-if="row.tier === 'daily'"
              theme="primary"
              variant="light"
              size="small"
              class="!inline-flex !items-center !justify-center !w-auto !px-2.5 !rounded-md !font-medium"
            >
              日备份
            </t-tag>
            <t-tag
              v-else-if="row.tier === 'weekly'"
              theme="success"
              variant="light"
              size="small"
              class="!inline-flex !items-center !justify-center !w-auto !px-2.5 !rounded-md !font-medium"
            >
              周备份
            </t-tag>
            <t-tag
              v-else-if="row.tier === 'monthly'"
              theme="warning"
              variant="light"
              size="small"
              class="!inline-flex !items-center !justify-center !w-auto !px-2.5 !rounded-md !font-medium"
            >
              月备份
            </t-tag>
          </div>
        </template>

        <template #periodTag="{ row }">
          <div class="font-mono text-xs text-zinc-600 dark:text-zinc-300 font-medium">
            {{ row.periodTag || '-' }}
          </div>
        </template>

        <template #fileName="{ row }">
          <div class="flex items-center font-mono text-[13px] text-zinc-700 dark:text-zinc-300 break-all">
            <file-icon class="mr-1.5 text-[var(--color-primary)] shrink-0" />
            <span>{{ row.fileName }}</span>
          </div>
        </template>

        <template #createdTime="{ row }">
          <div class="flex items-center text-[var(--td-text-color-secondary)] text-[13px]">
            <time-icon class="mr-1.5 shrink-0" />
            {{ new Date(row.createdTime).toLocaleString() }}
          </div>
        </template>

        <template #op="{ row }">
          <t-button variant="text" shape="square" theme="danger" class="!rounded-md hover:!bg-red-500/10 transition-colors" @click="handleDeleteArchive(row)">
            <delete-icon />
          </t-button>
        </template>

        <template #empty>
          <div class="p-8 text-center text-sm font-medium text-[var(--td-text-color-secondary)]">
            <span v-if="searchKeyword">未找到匹配的备份文件</span>
            <span v-else-if="currentTab !== 'all'">当前分类下暂无备份文件</span>
            <span v-else>暂无备份文件（将在下一次定时备份完成后自动归档生成）</span>
          </div>
        </template>
      </t-table>
    </div>

  </div>
</template>

<style scoped lang="less">
@reference "@/style/tailwind/index.css";
</style>
