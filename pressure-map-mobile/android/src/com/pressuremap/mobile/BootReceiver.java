package com.pressuremap.mobile;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;

/**
 * 手机重启或应用被覆盖安装后，系统会清空所有闹钟。
 * 这里按上次保存的计划重新排一遍，否则「重开机就再也不提醒」。
 */
public class BootReceiver extends BroadcastReceiver {

    @Override
    public void onReceive(Context context, Intent intent) {
        if (intent == null) return;
        String action = intent.getAction();
        if (Intent.ACTION_BOOT_COMPLETED.equals(action)
                || Intent.ACTION_MY_PACKAGE_REPLACED.equals(action)
                || "android.intent.action.QUICKBOOT_POWERON".equals(action)) {
            Reminders.rearm(context);
        }
    }
}
