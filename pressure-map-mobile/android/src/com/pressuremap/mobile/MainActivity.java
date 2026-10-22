package com.pressuremap.mobile;

import android.Manifest;
import android.app.Activity;
import android.app.AlertDialog;
import android.content.ContentValues;
import android.content.DialogInterface;
import android.content.Intent;
import android.graphics.Color;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Environment;
import android.provider.MediaStore;
import android.text.InputType;
import android.view.View;
import android.view.ViewGroup;
import android.view.Window;
import android.view.WindowManager;
import android.webkit.CookieManager;
import android.webkit.JavascriptInterface;
import android.webkit.JsPromptResult;
import android.webkit.JsResult;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Button;
import android.widget.EditText;
import android.widget.FrameLayout;
import android.widget.LinearLayout;
import android.widget.TextView;
import android.widget.Toast;

import org.json.JSONObject;

import java.io.ByteArrayInputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;
import java.util.HashMap;
import java.util.Map;

/**
 * 学期压力地图 · 手机版外壳。
 *
 * 所有页面都在 assets/www 内，通过拦截 https://app.local/ 提供给 WebView，
 * 让它拥有真正的 https 源，localStorage 才能稳定持久化，同时完全不联网。
 */
public class MainActivity extends Activity {

    private static final String HOST = "app.local";
    private static final String BASE = "https://" + HOST + "/";
    private static final int REQ_FILE = 4001;
    private static final int REQ_NOTIFY = 4002;

    private WebView web;
    private ValueCallback<Uri[]> fileCallback;
    private boolean dark = false;
    private boolean askedNotify = false;

    /** 正在显示的 JS 对话框（Activity 销毁时必须收尾，否则 WebView 会一直等）。 */
    private AlertDialog dialog;
    private Settle pending;

    /** 根容器与「网页读取」内嵌浏览窗口。 */
    private FrameLayout root;
    private FrameLayout scrapeLayer;
    private WebView scrapeView;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        root = new FrameLayout(this);
        root.setBackgroundColor(Color.parseColor("#F8F7FB"));

        web = new WebView(this);
        WebSettings s = web.getSettings();
        s.setJavaScriptEnabled(true);
        s.setDomStorageEnabled(true);
        s.setDatabaseEnabled(true);
        s.setAllowFileAccess(false);
        // 必须允许 content://：<input type=file> 选完返回的是 content URI，
        // 关掉这个开关后 FileReader 读不到内容，课表/备份导入会静默失败。
        s.setAllowContentAccess(true);
        s.setSupportZoom(false);
        s.setBuiltInZoomControls(false);
        s.setDisplayZoomControls(false);
        s.setTextZoom(100);
        s.setMediaPlaybackRequiresUserGesture(true);
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            s.setSafeBrowsingEnabled(false);
        }
        web.setBackgroundColor(Color.parseColor("#F8F7FB"));
        web.setOverScrollMode(View.OVER_SCROLL_NEVER);

        web.setWebViewClient(new Client(this));

        web.setWebChromeClient(new Chrome(this));

        web.addJavascriptInterface(new Host(this), "AndroidHost");

        root.addView(web, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        setContentView(root);

        applySystemBars();
        web.loadUrl(BASE + "index.html");
    }

    /** 把 assets/www 下的文件当作 https://app.local/ 的响应返回。 */
    private WebResourceResponse asset(String path) {
        String clean = path == null ? "" : path;
        while (clean.startsWith("/")) {
            clean = clean.substring(1);
        }
        if (clean.isEmpty()) {
            clean = "index.html";
        }
        if (clean.contains("..")) {
            return notFound();
        }
        String file = "www/" + clean;
        String mime = mimeOf(clean);
        try {
            InputStream stream = getAssets().open(file);
            Map<String, String> headers = new HashMap<>();
            headers.put("Cache-Control", "no-store");
            return new WebResourceResponse(mime, "utf-8", 200, "OK", headers, stream);
        } catch (Exception e) {
            return notFound();
        }
    }

    private WebResourceResponse notFound() {
        Map<String, String> headers = new HashMap<>();
        return new WebResourceResponse("text/plain", "utf-8", 404, "Not Found", headers,
                new ByteArrayInputStream("not found".getBytes(StandardCharsets.UTF_8)));
    }

    private static String mimeOf(String name) {
        String lower = name.toLowerCase();
        if (lower.endsWith(".html") || lower.endsWith(".htm")) return "text/html";
        if (lower.endsWith(".js")) return "application/javascript";
        if (lower.endsWith(".css")) return "text/css";
        if (lower.endsWith(".json") || lower.endsWith(".webmanifest")) return "application/json";
        if (lower.endsWith(".svg")) return "image/svg+xml";
        if (lower.endsWith(".png")) return "image/png";
        if (lower.endsWith(".jpg") || lower.endsWith(".jpeg")) return "image/jpeg";
        if (lower.endsWith(".webp")) return "image/webp";
        if (lower.endsWith(".woff2")) return "font/woff2";
        if (lower.endsWith(".woff")) return "font/woff";
        return "application/octet-stream";
    }

    // ---------------- WebView 回调（用命名内部类，避免部分 d8 版本对匿名类的缺陷） ----------------
    private static class Client extends WebViewClient {
        private final MainActivity host;

        Client(MainActivity host) {
            this.host = host;
        }

        @Override
        public WebResourceResponse shouldInterceptRequest(WebView view, WebResourceRequest request) {
            Uri uri = request.getUrl();
            if (uri != null && HOST.equals(uri.getHost())) {
                return host.asset(uri.getPath());
            }
            return null;
        }
    }

    private static class Chrome extends WebChromeClient {
        private final MainActivity host;

        Chrome(MainActivity host) {
            this.host = host;
        }

        @Override
        public boolean onShowFileChooser(WebView view, ValueCallback<Uri[]> callback,
                                         FileChooserParams params) {
            return host.pickFile(callback, params);
        }

        @Override
        public boolean onJsAlert(WebView view, String url, String message, JsResult result) {
            return host.jsAlert(message, result);
        }

        @Override
        public boolean onJsConfirm(WebView view, String url, String message, JsResult result) {
            return host.jsConfirm(message, result);
        }

        @Override
        public boolean onJsPrompt(WebView view, String url, String message,
                                  String defaultValue, JsPromptResult result) {
            return host.jsPrompt(message, defaultValue, result);
        }

        @Override
        public boolean onJsBeforeUnload(WebView view, String url, String message, JsResult result) {
            return host.jsConfirm(message, result);
        }
    }

    // ---------------- JS 原生对话框 ----------------
    //
    // WebView 默认不会自己弹 alert/confirm/prompt，网页里 confirm() 会直接返回 false。
    // 本应用有 11 处 confirm（删除待办/课程/学期/课表、退出登录、清理缓存、覆盖数据），
    // 不接管这些回调的话，装机后这些按钮全部静默失效。

    /**
     * 保证 JsResult 只被答复一次：onDismiss 也会触发 cancel，
     * 叠加点击按钮回调和 Activity 销毁时的强制收尾，必须幂等。
     */
    private static class Settle implements DialogInterface.OnDismissListener {
        private final MainActivity host;
        private final JsResult result;
        private final JsPromptResult prompt;
        private boolean settled;
        private String text;

        Settle(MainActivity host, JsResult result) {
            this.host = host;
            this.result = result;
            this.prompt = result instanceof JsPromptResult ? (JsPromptResult) result : null;
        }

        void payload(String value) {
            text = value;
        }

        void confirm() {
            if (settled) return;
            settled = true;
            host.pending = null;
            if (prompt != null) {
                prompt.confirm(text == null ? "" : text);
            } else {
                result.confirm();
            }
        }

        void cancel() {
            if (settled) return;
            settled = true;
            host.pending = null;
            result.cancel();
        }

        @Override
        public void onDismiss(DialogInterface d) {
            cancel();
            if (host.dialog == d) host.dialog = null;
        }
    }

    private AlertDialog.Builder dialogBuilder() {
        int theme = dark
                ? android.R.style.Theme_DeviceDefault_Dialog_Alert
                : android.R.style.Theme_DeviceDefault_Light_Dialog_Alert;
        return new AlertDialog.Builder(this, theme);
    }

    private void showDialog(AlertDialog dlg, Settle settle) {
        dismissPending();
        pending = settle;
        dialog = dlg;
        dlg.setCanceledOnTouchOutside(false);
        dlg.setOnDismissListener(settle);
        dlg.show();
    }

    private void dismissPending() {
        AlertDialog old = dialog;
        dialog = null;
        if (old != null && old.isShowing()) {
            old.dismiss();
        }
        if (pending != null) {
            pending.cancel();
            pending = null;
        }
    }

    private boolean jsAlert(String message, JsResult result) {
        Settle settle = new Settle(this, result);
        AlertDialog dlg = dialogBuilder()
                .setMessage(message)
                .setPositiveButton("确定", (d, w) -> settle.confirm())
                .create();
        showDialog(dlg, settle);
        return true;
    }

    private boolean jsConfirm(String message, JsResult result) {
        Settle settle = new Settle(this, result);
        AlertDialog dlg = dialogBuilder()
                .setMessage(message)
                .setPositiveButton("确定", (d, w) -> settle.confirm())
                .setNegativeButton("取消", (d, w) -> settle.cancel())
                .create();
        showDialog(dlg, settle);
        return true;
    }

    private boolean jsPrompt(String message, String defaultValue, JsPromptResult result) {
        Settle settle = new Settle(this, result);
        EditText input = new EditText(this);
        input.setInputType(InputType.TYPE_CLASS_TEXT);
        input.setText(defaultValue == null ? "" : defaultValue);
        input.setSelection(input.getText().length());

        FrameLayout wrap = new FrameLayout(this);
        int pad = Math.round(20 * getResources().getDisplayMetrics().density);
        wrap.setPadding(pad, pad / 2, pad, 0);
        wrap.addView(input, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT));

        AlertDialog dlg = dialogBuilder()
                .setTitle(message)
                .setView(wrap)
                .setPositiveButton("确定", (d, w) -> {
                    settle.payload(input.getText().toString());
                    settle.confirm();
                })
                .setNegativeButton("取消", (d, w) -> settle.cancel())
                .create();
        showDialog(dlg, settle);
        input.requestFocus();
        return true;
    }

    private boolean pickFile(ValueCallback<Uri[]> callback, WebChromeClient.FileChooserParams params) {
        if (fileCallback != null) {
            fileCallback.onReceiveValue(null);
        }
        fileCallback = callback;
        try {
            // 不用 params.createIntent()：它会把 accept=".ics,.csv" 翻译成 text/calendar 之类的
            // MIME 列表，很多文件管理器映射不上，导致 .ics 直接置灰选不了。
            // 统一放开成 */*，扩展名交给网页自己校验。
            Intent intent = new Intent(Intent.ACTION_GET_CONTENT);
            intent.addCategory(Intent.CATEGORY_OPENABLE);
            intent.setType("*/*");
            intent.putExtra(Intent.EXTRA_ALLOW_MULTIPLE,
                    params != null && params.getMode() == WebChromeClient.FileChooserParams.MODE_OPEN_MULTIPLE);
            startActivityForResult(Intent.createChooser(intent, "选择文件"), REQ_FILE);
            return true;
        } catch (Exception e) {
            fileCallback = null;
            Toast.makeText(this, "没有可用的文件选择器", Toast.LENGTH_SHORT).show();
            return false;
        }
    }

    // ---------------- 通知权限 ----------------
    /** Android 13 起发通知要用户授权，第一次真正排上提醒时再问，不要一开屏就弹。 */
    void ensureNotificationPermission() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.TIRAMISU) return;
        if (Reminders.canNotify(this) || askedNotify) return;
        askedNotify = true;
        runOnUiThread(() -> {
            try {
                requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS}, REQ_NOTIFY);
            } catch (Exception ignored) {
            }
        });
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions, int[] grantResults) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode != REQ_NOTIFY || web == null) return;
        // 授权结果要回给网页：设置页据此显示「已开启 / 被系统拒绝」
        web.evaluateJavascript(
                "(function(){try{window.pmNotifyChanged&&window.pmNotifyChanged("
                        + (Reminders.canNotify(this) ? "true" : "false") + ")}catch(e){}return ''})()",
                null);
    }

    // ---------------- 主题 ----------------
    private void applySystemBars() {
        Window window = getWindow();
        int bg = dark ? Color.parseColor("#17151E") : Color.parseColor("#F8F7FB");
        int bar = dark ? Color.parseColor("#17151E") : Color.parseColor("#F8F7FB");
        window.setStatusBarColor(bar);
        window.setNavigationBarColor(dark ? Color.parseColor("#211E2B") : Color.WHITE);

        View decor = window.getDecorView();
        int flags = decor.getSystemUiVisibility();
        if (dark) {
            flags &= ~View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR;
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                flags &= ~View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR;
            }
        } else {
            flags |= View.SYSTEM_UI_FLAG_LIGHT_STATUS_BAR;
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                flags |= View.SYSTEM_UI_FLAG_LIGHT_NAVIGATION_BAR;
            }
        }
        decor.setSystemUiVisibility(flags);

        View content = findViewById(android.R.id.content);
        if (content != null) content.setBackgroundColor(bg);
    }


    // ---------------- 在线课程：内嵌浏览窗口 ----------------

    /**
     * 打开一个覆盖全屏的浏览窗口加载在线课程站点。
     *
     * WebView 不能像桌面那样开多个标签、遍历 iframe，所以这里只做「会话式抓取」：
     * 用户自己登录（密码不落盘），点「读取这一页」时把提取脚本注入当前页面，
     * 页面执行完通过 AndroidHost.scrapeResult 把 JSON 交回来。
     */
    void showScrape(String id, String url, String script) {
        closeScrape();
        if (root == null) return;

        scrapeLayer = new FrameLayout(this);
        scrapeLayer.setBackgroundColor(dark ? Color.parseColor("#17151E") : Color.parseColor("#F8F7FB"));

        LinearLayout bar = new LinearLayout(this);
        bar.setOrientation(LinearLayout.HORIZONTAL);
        bar.setGravity(android.view.Gravity.CENTER_VERTICAL);
        bar.setBackgroundColor(dark ? Color.parseColor("#211E2B") : Color.WHITE);
        int pad = Math.round(10 * getResources().getDisplayMetrics().density);
        bar.setPadding(pad, pad, pad, pad);

        TextView title = new TextView(this);
        title.setText(id != null && id.contains("mooc") ? "中国大学 MOOC" : "超星学习通");
        title.setTextSize(15);
        title.setTextColor(dark ? Color.parseColor("#EDEAF5") : Color.parseColor("#1E1B26"));
        title.setLayoutParams(new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f));
        bar.addView(title);

        Button cancel = new Button(this);
        cancel.setText("关闭");
        cancel.setAllCaps(false);
        cancel.setOnClickListener(v -> closeScrape());
        bar.addView(cancel);

        Button read = new Button(this);
        read.setText("读取这一页");
        read.setAllCaps(false);
        read.setOnClickListener(v -> injectScrape(script));
        bar.addView(read);

        scrapeView = new WebView(this);
        WebSettings ws = scrapeView.getSettings();
        ws.setJavaScriptEnabled(true);
        ws.setDomStorageEnabled(true);
        ws.setAllowFileAccess(false);
        ws.setAllowContentAccess(true);
        ws.setJavaScriptCanOpenWindowsAutomatically(false);
        ws.setSupportMultipleWindows(false);
        // 登录态要跟着会话走：第三方 Cookie 必须打开，否则登录后仍被当成未登录
        CookieManager cookies = CookieManager.getInstance();
        cookies.setAcceptCookie(true);
        cookies.setAcceptThirdPartyCookies(scrapeView, true);
        scrapeView.addJavascriptInterface(new Host(this), "AndroidHost");
        scrapeView.setWebViewClient(new ScrapeClient(this));
        scrapeView.loadUrl(url);

        FrameLayout.LayoutParams barParams = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT);
        scrapeLayer.addView(bar, barParams);

        FrameLayout.LayoutParams webParams = new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT);
        webParams.topMargin = Math.round(52 * getResources().getDisplayMetrics().density);
        scrapeLayer.addView(scrapeView, webParams);

        root.addView(scrapeLayer, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
    }

    private void injectScrape(String script) {
        if (scrapeView == null) return;
        String body = script == null || script.isEmpty() ? "({ok:false,error:'no script'})" : script;
        String wrapped = "(function(){try{var r=" + body +
                ";window.AndroidHost.scrapeResult(typeof r==='string'?r:JSON.stringify(r));}" +
                "catch(e){window.AndroidHost.scrapeResult(JSON.stringify({ok:false,error:String((e&&e.message)||e)}));}})()";
        scrapeView.evaluateJavascript(wrapped, null);
    }

    /** 把抓取结果转交给主页面，然后收起浏览窗口。 */
    void deliverScrape(String json) {
        String payload = json == null ? "" : json;
        if (web != null) {
            String quoted = JSONObject.quote(payload);
            web.evaluateJavascript(
                    "(function(){try{window.pmScrapeResult&&window.pmScrapeResult(" + quoted + ")}catch(e){}})()", null);
        }
        closeScrape();
    }

    private void closeScrape() {
        if (scrapeLayer != null && root != null) {
            root.removeView(scrapeLayer);
        }
        if (scrapeView != null) {
            scrapeView.setWebChromeClient(null);
            scrapeView.destroy();
            scrapeView = null;
        }
        scrapeLayer = null;
    }

    /** 内嵌窗口里的导航不能跳出去，登录跳转要留在窗口内。 */
    private static class ScrapeClient extends WebViewClient {
        private final MainActivity host;

        ScrapeClient(MainActivity host) {
            this.host = host;
        }

        @Override
        public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
            return false;
        }
    }

    @Override
    protected void onDestroy() {
        // 网页还可能卡在一个没答复的 confirm 上，销毁前必须放行，否则回调永挂。
        dismissPending();
        closeScrape();
        if (web != null) {
            web.setWebChromeClient(null);
            web.destroy();
            web = null;
        }
        super.onDestroy();
    }

    @Override
    public void onBackPressed() {
        if (scrapeLayer != null) {
            closeScrape();
            return;
        }
        if (dialog != null) {
            dismissPending();
            return;
        }
        if (web == null) {
            super.onBackPressed();
            return;
        }
        web.evaluateJavascript(
                "(function(){try{return window.pmHandleBack&&window.pmHandleBack()?'1':'0'}catch(e){return '0'}})()",
                value -> {
                    if (value != null && value.indexOf('1') >= 0) return;
                    finish();
                });
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        if (requestCode == REQ_FILE) {
            if (fileCallback != null) {
                Uri[] result = null;
                if (resultCode == RESULT_OK && data != null) {
                    if (data.getClipData() != null) {
                        int count = data.getClipData().getItemCount();
                        result = new Uri[count];
                        for (int i = 0; i < count; i++) {
                            result[i] = data.getClipData().getItemAt(i).getUri();
                        }
                    } else if (data.getData() != null) {
                        result = new Uri[]{data.getData()};
                    }
                }
                fileCallback.onReceiveValue(result);
                fileCallback = null;
            }
            return;
        }
        super.onActivityResult(requestCode, resultCode, data);
    }

    // ---------------- 给网页用的接口 ----------------
    private static class Host {
        private final MainActivity host;

        Host(MainActivity host) {
            this.host = host;
        }

        @JavascriptInterface
        public void setDark(final boolean value) {
            host.runOnUiThread(() -> {
                if (host.dark == value) return;
                host.dark = value;
                host.applySystemBars();
            });
        }

        /**
         * 网页把「哪一刻提醒哪条待办」算好后推过来，这里排系统闹钟。
         * 返回实际排上的条数（0 表示没有权限或被系统拒绝）。
         */
        @JavascriptInterface
        public int syncReminders(String json) {
            try {
                int count = Reminders.schedule(host, json);
                if (count > 0) host.ensureNotificationPermission();
                return count;
            } catch (Exception e) {
                return 0;
            }
        }

        /** 立刻发一条通知，用于设置页的「测试提醒」。 */
        @JavascriptInterface
        public boolean notifyNow(String title, String body) {
            try {
                if (!Reminders.canNotify(host)) {
                    host.ensureNotificationPermission();
                    return false;
                }
                return Reminders.post(host, title, body);
            } catch (Exception e) {
                return false;
            }
        }

        @JavascriptInterface
        public boolean canNotify() {
            return Reminders.canNotify(host);
        }

        /**
         * 打开内嵌浏览窗口去抓在线课程：用户在里面登录，点「读取这一页」后
         * 把 script 注入当前页面，页面里的 AndroidHost.scrapeResult 把 JSON 传回来。
         */
        @JavascriptInterface
        public void openScrape(String id, String url, String script) {
            host.runOnUiThread(() -> host.showScrape(id, url, script));
        }

        /** 由内嵌窗口的页面调用，把抓取结果交回主界面。 */
        @JavascriptInterface
        public void scrapeResult(String json) {
            host.runOnUiThread(() -> host.deliverScrape(json));
        }

        /** 保存 JSON 备份，返回可展示的路径；失败返回 ERR: 开头。 */
        @JavascriptInterface
        public String saveBackup(String name, String content) {
            try {
                byte[] bytes = content.getBytes(StandardCharsets.UTF_8);
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                    ContentValues values = new ContentValues();
                    values.put(MediaStore.Downloads.DISPLAY_NAME, name);
                    values.put(MediaStore.Downloads.MIME_TYPE, "application/json");
                    values.put(MediaStore.Downloads.RELATIVE_PATH,
                            Environment.DIRECTORY_DOWNLOADS + "/PressureMap");
                    values.put(MediaStore.Downloads.IS_PENDING, 1);
                    Uri uri = host.getContentResolver().insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values);
                    if (uri == null) return "ERR:无法创建文件";
                    try (OutputStream out = host.getContentResolver().openOutputStream(uri)) {
                        if (out == null) return "ERR:无法写入文件";
                        out.write(bytes);
                    }
                    values.clear();
                    values.put(MediaStore.Downloads.IS_PENDING, 0);
                    host.getContentResolver().update(uri, values, null, null);
                    return "下载/PressureMap/" + name;
                }
                File dir = new File(host.getExternalFilesDir(null), "backups");
                if (!dir.exists() && !dir.mkdirs()) return "ERR:无法创建目录";
                File target = new File(dir, name);
                try (FileOutputStream out = new FileOutputStream(target)) {
                    out.write(bytes);
                }
                return target.getAbsolutePath();
            } catch (Exception e) {
                return "ERR:" + e.getMessage();
            }
        }
    }
}
