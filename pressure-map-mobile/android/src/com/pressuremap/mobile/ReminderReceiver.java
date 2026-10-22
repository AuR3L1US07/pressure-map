package com.pressuremap.mobile;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;

/** 闹钟到点：把网页算好的那条提醒弹成系统通知。 */
public class ReminderReceiver extends BroadcastReceiver {

    @Override
    public void onReceive(Context context, Intent intent) {
        if (intent == null) return;
        String title = intent.getStringExtra("title");
        String body = intent.getStringExtra("body");
        Reminders.post(context, title, body);
    }
}
