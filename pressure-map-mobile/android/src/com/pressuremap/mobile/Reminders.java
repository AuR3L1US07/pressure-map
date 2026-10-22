package com.pressuremap.mobile;

import android.Manifest;
import android.app.AlarmManager;
import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Build;

import org.json.JSONArray;
import org.json.JSONObject;

import java.util.ArrayList;
import java.util.List;

/**
 * 提醒排程。
 *
 * 网页侧（www/pm-remind.js）已经算好了「哪一刻该提醒哪条待办」，这里只负责三件事：
 * 排系统闹钟、到点弹通知、重启后按上次的计划重排。
 *
 * 之所以不让原生自己算时间：提醒规则（24 小时 / 1 小时 / 到期、已完成待提交的文案）
 * 必须和桌面版逐条一致，放在 JS 里才能被 qa-remind.mjs 单测覆盖。
 */
final class Reminders {

    static final String CHANNEL_ID = "pm_reminders";
    static final String CHANNEL_NAME = "任务截止提醒";
    private static final String PREFS = "pm_reminders";
    private static final String KEY_PLAN = "plan";
    private static final String KEY_CODES = "codes";
    private static final String ACTION = "com.pressuremap.mobile.REMIND";
    private static final String DATA = "pmreminder://";

    private Reminders() {
    }

    static SharedPreferences prefs(Context ctx) {
        return ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
    }

    static void ensureChannel(Context ctx) {
        NotificationManager nm = ctx.getSystemService(NotificationManager.class);
        if (nm == null) return;
        if (nm.getNotificationChannel(CHANNEL_ID) != null) return;
        NotificationChannel channel = new NotificationChannel(
                CHANNEL_ID, CHANNEL_NAME, NotificationManager.IMPORTANCE_HIGH);
        channel.setDescription("课程作业临近截止时提醒");
        channel.enableVibration(true);
        nm.createNotificationChannel(channel);
    }

    private static boolean canExact(AlarmManager am) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
            try {
                return am.canScheduleExactAlarms();
            } catch (Exception e) {
                return false;
            }
        }
        return true;
    }

    private static int codeOf(String id, String key) {
        int code = (id + "|" + key).hashCode();
        return code == 0 ? 1 : Math.abs(code);
    }

    private static PendingIntent pending(Context ctx, int code, String id, String title,
                                         String body, String key) {
        Intent intent = new Intent(ctx, ReminderReceiver.class);
        intent.setAction(ACTION);
        // 每个提醒必须有独立的 data，否则 PendingIntent 会被认成同一个而被覆盖。
        intent.setData(Uri.parse(DATA + code));
        intent.putExtra("id", id);
        intent.putExtra("title", title);
        intent.putExtra("body", body);
        intent.putExtra("key", key);
        int flags = PendingIntent.FLAG_UPDATE_CURRENT;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            flags |= PendingIntent.FLAG_IMMUTABLE;
        }
        return PendingIntent.getBroadcast(ctx, code, intent, flags);
    }

    /** 取消上一次排下的全部闹钟（只按预先记下的 requestCode 取消，不碰别的）。 */
    static void cancelAll(Context ctx) {
        AlarmManager am = ctx.getSystemService(AlarmManager.class);
        if (am == null) return;
        String codes = prefs(ctx).getString(KEY_CODES, "");
        if (codes == null || codes.isEmpty()) return;
        for (String part : codes.split(",")) {
            if (part.isEmpty()) continue;
            int code;
            try {
                code = Integer.parseInt(part);
            } catch (NumberFormatException e) {
                continue;
            }
            PendingIntent pi = pending(ctx, code, "", "", "", "");
            try {
                am.cancel(pi);
            } catch (Exception ignored) {
            }
            try {
                pi.cancel();
            } catch (Exception ignored) {
            }
        }
        prefs(ctx).edit().putString(KEY_CODES, "").apply();
    }

    /** 按网页给的 JSON 计划重排闹钟，返回实际排上的条数。 */
    static int schedule(Context ctx, String json) {
        ensureChannel(ctx);
        AlarmManager am = ctx.getSystemService(AlarmManager.class);
        if (am == null) return 0;
        cancelAll(ctx);
        JSONArray array;
        try {
            array = new JSONArray(json == null || json.isEmpty() ? "[]" : json);
        } catch (Exception e) {
            return 0;
        }
        long now = System.currentTimeMillis();
        List<String> codes = new ArrayList<>();
        int count = 0;
        for (int i = 0; i < array.length(); i++) {
            JSONObject item = array.optJSONObject(i);
            if (item == null) continue;
            long at = item.optLong("at", 0L);
            if (at <= now + 1000L) continue;
            String id = item.optString("id", "");
            if (id.isEmpty()) continue;
            String key = item.optString("key", "");
            String title = item.optString("title", "任务提醒");
            String body = item.optString("body", "");
            int code = codeOf(id, key);
            PendingIntent pi = pending(ctx, code, id, title, body, key);
            try {
                if (canExact(am)) {
                    am.setExactAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, at, pi);
                } else {
                    am.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, at, pi);
                }
                codes.add(Integer.toString(code));
                count++;
            } catch (SecurityException e) {
                // 精确闹钟被系统拒绝时退化成「尽量准时」，最多晚几分钟
                try {
                    am.setAndAllowWhileIdle(AlarmManager.RTC_WAKEUP, at, pi);
                    codes.add(Integer.toString(code));
                    count++;
                } catch (Exception ignored) {
                }
            } catch (Exception ignored) {
            }
        }
        prefs(ctx).edit()
                .putString(KEY_PLAN, json)
                .putString(KEY_CODES, join(codes))
                .apply();
        return count;
    }

    /** 开机 / 应用更新后按上次的计划重排（只排还没到点的）。 */
    static void rearm(Context ctx) {
        String plan = prefs(ctx).getString(KEY_PLAN, "");
        if (plan == null || plan.isEmpty()) return;
        schedule(ctx, plan);
    }

    private static String join(List<String> parts) {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < parts.size(); i++) {
            if (i > 0) sb.append(',');
            sb.append(parts.get(i));
        }
        return sb.toString();
    }

    static boolean canNotify(Context ctx) {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU) return true;
        return ctx.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)
                == PackageManager.PERMISSION_GRANTED;
    }

    /** 弹一条通知；没有通知权限时静默返回 false。 */
    static boolean post(Context ctx, String title, String body) {
        if (!canNotify(ctx)) return false;
        ensureChannel(ctx);
        NotificationManager nm = ctx.getSystemService(NotificationManager.class);
        if (nm == null) return false;
        Intent open = new Intent(ctx, MainActivity.class);
        open.setFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TOP);
        int flags = PendingIntent.FLAG_UPDATE_CURRENT;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            flags |= PendingIntent.FLAG_IMMUTABLE;
        }
        PendingIntent content = PendingIntent.getActivity(ctx, 0, open, flags);
        Notification notification = new Notification.Builder(ctx, CHANNEL_ID)
                .setContentTitle(title == null || title.isEmpty() ? "任务提醒" : title)
                .setContentText(body == null ? "" : body)
                .setStyle(new Notification.BigTextStyle().bigText(body == null ? "" : body))
                .setSmallIcon(R.drawable.ic_notify)
                .setAutoCancel(true)
                .setContentIntent(content)
                .setWhen(System.currentTimeMillis())
                .setShowWhen(true)
                .build();
        try {
            nm.notify((int) (System.currentTimeMillis() & 0x7fffffff), notification);
            return true;
        } catch (Exception e) {
            return false;
        }
    }
}
