import sys

p = "dist/app.js"
s = open(p, encoding="utf-8").read()

old = ('downloadUrls={windows:"",android:"",ios:""},'
       'releaseInfo=Object.fromEntries(Object.keys(downloadUrls).map(e=>[e,{version:"",size:"",sha256:"",date:""}]))')
assert s.count(old) == 1, ("downloadUrls", s.count(old))
new = ('downloadUrls={windows:"",android:"download/PressureMap-android-1.0.0.apk",ios:""},'
       'releaseInfo=Object.assign(Object.fromEntries(Object.keys(downloadUrls).map(e=>[e,{version:"",size:"",sha256:"",date:""}])),'
       '{android:{version:"1.0.0",size:"92 KB",'
       'sha256:"5ae5c9541ce018d366f010702d4f10cab3fd0811b838afd03a0e6d87726fe0f6",date:"2026-10-20"}})')
s = s.replace(old, new)

old2 = "下载 iOS 版|Download iOS\\n"
assert s.count(old2) == 1, ("dict", s.count(old2))
s = s.replace(old2, old2 + "不想安装？直接在浏览器里用手机版|No install needed — use it in your browser\\n")

open(p, "w", encoding="utf-8", newline="").write(s)
print("app.js patched")
