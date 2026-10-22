/* 学期压力地图 · 云账号与同步（对应桌面版 CloudClient + CloudSyncEngine）
   协议与桌面完全一致：/v1/auth/register、/v1/auth/login、/v1/data(GET/PUT)、/v1/auth/logout，
   因此手机和电脑登录同一账号时看到的是同一份数据。 */
(function () {
  "use strict";
  var PM = (window.PM = window.PM || {});
  var DEFAULT_BASE = "https://121.43.32.85/api";
  var TIMEOUT = 30000;

  function join(base, path) { return String(base).replace(/\/+$/, "") + path; }
  function isLoopbackOrHttps(url) {
    var m = /^(https?):\/\/([^/?#]+)([^?#]*)$/i.exec(url);
    if (!m) return false;
    if (m[1].toLowerCase() === "https") return true;
    return /^(127\.0\.0\.1|localhost)(:\d+)?$/i.test(m[2]);
  }
  PM.isValidApiUrl = function (url) {
    if (!url) return false;
    if (!isLoopbackOrHttps(url)) return false;
    if (/[?#]/.test(url)) return false;
    if (/^https?:\/\/[^/]*@/.test(url)) return false;
    return true;
  };
  PM.DEFAULT_API_BASE = DEFAULT_BASE;

  function request(method, url, body, token) {
    var controller = window.AbortController ? new AbortController() : null;
    var timer = setTimeout(function () { if (controller) controller.abort(); }, TIMEOUT);
    var headers = { "Accept": "application/json" };
    if (body != null) headers["Content-Type"] = "application/json";
    if (token) headers["Authorization"] = "Bearer " + token;
    return fetch(url, {
      method: method,
      headers: headers,
      body: body == null ? undefined : JSON.stringify(body),
      cache: "no-store",
      signal: controller ? controller.signal : undefined
    }).then(function (res) {
      clearTimeout(timer);
      return res.text().then(function (text) {
        var json = null;
        try { json = text ? JSON.parse(text) : null; } catch (e) { json = null; }
        if (res.status === 401) { var e401 = new Error("账号或密码不正确，或登录已过期"); e401.code = 401; throw e401; }
        if (res.status === 409) { var e409 = new Error("云端数据已更新"); e409.code = 409; throw e409; }
        if (res.status === 429) { var e429 = new Error("操作过于频繁，请一分钟后重试"); e429.code = 429; throw e429; }
        if (res.status === 400) { var e400 = new Error((json && json.message) || "提交的信息无效"); e400.code = 400; throw e400; }
        if (!res.ok) { var e5 = new Error("云服务暂时不可用（HTTP " + res.status + "）"); e5.code = res.status; throw e5; }
        if (!json) { var e6 = new Error("云服务返回了无效数据"); e6.code = "bad-json"; throw e6; }
        return json;
      });
    }).catch(function (err) {
      clearTimeout(timer);
      if (err && err.code) throw err;
      if (err && err.name === "AbortError") throw new Error("云服务请求超时，请稍后重试");
      throw new Error("无法连接云服务，请检查网络后重试");
    });
  }

  var Cloud = (PM.Cloud = {
    baseUrl: DEFAULT_BASE,
    token: null,
    user: null,
    configure: function (baseUrl) { Cloud.baseUrl = (baseUrl || DEFAULT_BASE).replace(/\/+$/, ""); },
    restore: function (token, user) { Cloud.token = token || null; Cloud.user = user || null; },
    clear: function () { Cloud.token = null; Cloud.user = null; },
    hasSession: function () { return !!(Cloud.token && Cloud.user); },
    register: function (account, name, password) {
      if (!PM.isValidApiUrl(Cloud.baseUrl)) return Promise.reject(new Error("云服务地址必须是有效的 HTTPS 地址"));
      return request("POST", join(Cloud.baseUrl, "/v1/auth/register"), { account: account, name: name, password: password }, null)
        .then(function () { return Cloud.login(account, password); });
    },
    login: function (account, password) {
      if (!PM.isValidApiUrl(Cloud.baseUrl)) return Promise.reject(new Error("云服务地址必须是有效的 HTTPS 地址"));
      return request("POST", join(Cloud.baseUrl, "/v1/auth/login"), { account: account, password: password }, null)
        .then(function (json) {
          var token = json && json.access_token;
          var user = json && json.user;
          if (!token || !user || !user.id) throw new Error("登录响应缺少账户信息");
          Cloud.token = token;
          Cloud.user = { id: String(user.id), name: String(user.name || user.id) };
          return Cloud.user;
        });
    },
    logout: function () {
      var token = Cloud.token;
      Cloud.clear();
      if (!token) return Promise.resolve();
      return request("POST", join(Cloud.baseUrl, "/v1/auth/logout"), {}, token).catch(function () { });
    },
    download: function () {
      if (!Cloud.token) return Promise.reject(new Error("请先登录"));
      return request("GET", join(Cloud.baseUrl, "/v1/data"), null, Cloud.token).then(function (json) {
        if (!json || typeof json.snapshot !== "object" || json.snapshot === null) throw new Error("云端响应缺少数据快照");
        var revision = json.revision == null ? null : String(json.revision);
        if (revision != null && (revision.length < 1 || revision.length > 128)) throw new Error("云端版本号无效");
        return { snapshot: json.snapshot, revision: revision };
      });
    },
    upload: function (snapshot, baseRevision) {
      if (!Cloud.token) return Promise.reject(new Error("请先登录"));
      return request("PUT", join(Cloud.baseUrl, "/v1/data"), { base_revision: baseRevision == null ? null : baseRevision, snapshot: snapshot }, Cloud.token)
        .then(function (json) {
          var revision = json && json.revision;
          if (!revision) throw new Error("上传响应缺少版本号");
          return String(revision);
        });
    },
    /** 账户在本机的隔离标识，与桌面 AccountDirectory 同一算法。 */
    accountKey: function () {
      if (!Cloud.user) return "";
      return PM.sha256(Cloud.baseUrl + "\n" + Cloud.user.id).toLowerCase();
    }
  });

  /* ---------------- 同步引擎 ----------------
     ctx = { snapshot(), apply(snap), baseline(), saveBaseline(revision, hash), canApply() } */
  var Sync = (PM.Sync = {
    conflict: null,
    busy: false,
    run: function (ctx) {
      if (Sync.busy) return Promise.reject(new Error("正在同步，请稍候"));
      if (Sync.conflict) return Promise.resolve({ outcome: "conflict", conflict: Sync.conflict });
      if (!Cloud.hasSession()) return Promise.reject(new Error("请先登录云账号"));
      Sync.busy = true;
      var local = ctx.snapshot();
      var localHash = PM.hashSnapshot(local);
      var baseline = ctx.baseline();
      var remoteSnap, remoteHash, remoteRevision;
      return Cloud.download().then(function (remote) {
        remoteSnap = remote.snapshot;
        remoteRevision = remote.revision;
        remoteHash = PM.hashSnapshot(remoteSnap);
        if (localHash === remoteHash) {
          ctx.saveBaseline(remoteRevision, localHash);
          return { outcome: "unchanged" };
        }
        var localEmpty = PM.isEmptySnapshot(local);
        var remoteEmpty = remoteRevision == null && PM.isEmptySnapshot(remoteSnap);
        var localChanged = baseline == null ? !localEmpty : localHash !== baseline.hash;
        var remoteChanged = baseline == null ? !remoteEmpty : remoteRevision !== baseline.revision;
        if (localChanged && remoteChanged) {
          if (remoteRevision == null) throw new Error("云端已重置，需要重新登录后确认数据");
          Sync.conflict = { remote: remoteSnap, revision: remoteRevision, localHash: localHash };
          return { outcome: "conflict", conflict: Sync.conflict };
        }
        if (remoteChanged) {
          if (ctx.canApply && !ctx.canApply()) throw new Error("正在输入，稍后会自动同步云端更新");
          ctx.apply(remoteSnap);
          ctx.saveBaseline(remoteRevision, PM.hashSnapshot(ctx.snapshot()));
          return { outcome: "downloaded" };
        }
        if (localChanged) {
          return Cloud.upload(local, remoteRevision).then(function (revision) {
            ctx.saveBaseline(revision, localHash);
            return { outcome: "uploaded" };
          }).catch(function (err) {
            if (err.code !== 409) throw err;
            return Cloud.download().then(function (latest) {
              Sync.conflict = { remote: latest.snapshot, revision: latest.revision, localHash: localHash };
              return { outcome: "conflict", conflict: Sync.conflict };
            });
          });
        }
        return { outcome: "unchanged" };
      }).then(function (result) {
        Sync.busy = false;
        return result;
      }, function (err) {
        Sync.busy = false;
        throw err;
      });
    },
    /** useLocal=true 覆盖云端；false 用云端覆盖本地。 */
    resolve: function (useLocal, ctx) {
      var conflict = Sync.conflict;
      if (!conflict) return Promise.reject(new Error("没有待处理的同步冲突"));
      if (PM.hashSnapshot(ctx.snapshot()) !== conflict.localHash) {
        Sync.conflict = null;
        return Promise.reject(new Error("本地数据已有新修改，请重新同步后再选择"));
      }
      if (useLocal) {
        return Cloud.upload(ctx.snapshot(), conflict.revision).then(function (revision) {
          ctx.saveBaseline(revision, conflict.localHash);
          Sync.conflict = null;
          return { outcome: "uploaded" };
        }, function (err) {
          if (err.code === 409) Sync.conflict = null;
          throw err;
        });
      }
      return Cloud.download().then(function (latest) {
        if (latest.revision !== conflict.revision) {
          Sync.conflict = null;
          throw new Error("云端再次更新，请重新同步后再选择");
        }
        if (ctx.canApply && !ctx.canApply()) throw new Error("正在输入，请保存后处理冲突");
        ctx.apply(conflict.remote);
        ctx.saveBaseline(conflict.revision, PM.hashSnapshot(ctx.snapshot()));
        Sync.conflict = null;
        return { outcome: "downloaded" };
      });
    },
    clearConflict: function () { Sync.conflict = null; }
  });
})();
