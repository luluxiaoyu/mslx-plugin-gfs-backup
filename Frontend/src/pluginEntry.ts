import './style.css';
import { HistoryIcon } from 'tdesign-icons-vue-next';
import InstanceGfsSettingsTab from './views/InstanceGfsSettingsTab.vue';

export const pluginConfig = {
    name: 'GfsBackupPlugin',
    version: '1.0.0',

    routes: [],

    extensions: [
        {
            slot: 'instance-settings-tab', // 注入到实例配置弹窗侧边栏新 Tab
            component: InstanceGfsSettingsTab,
            label: 'GFS分层备份',
            icon: HistoryIcon,
        }
    ]
};
