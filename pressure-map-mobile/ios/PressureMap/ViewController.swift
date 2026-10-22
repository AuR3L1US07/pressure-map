import UIKit
import WebKit

/// 用 WKWebView 加载打包进 App 的本地页面。
/// 页面保存在 PressureMap/www（由 scripts/prepare-ios.sh 从 ../www 复制）。
final class ViewController: UIViewController, WKScriptMessageHandler {

    private var webView: WKWebView!

    override func viewDidLoad() {
        super.viewDidLoad()

        let config = WKWebViewConfiguration()
        config.allowsInlineMediaPlayback = true
        config.userContentController.add(self, name: "saveBackup")

        webView = WKWebView(frame: .zero, configuration: config)
        webView.translatesAutoresizingMaskIntoConstraints = false
        // 不用系统自动内边距，交给页面里的 env(safe-area-inset-*) 处理
        webView.scrollView.contentInsetAdjustmentBehavior = .never
        webView.scrollView.bounces = false
        webView.scrollView.showsVerticalScrollIndicator = false
        webView.isOpaque = false
        webView.backgroundColor = UIColor(red: 0.973, green: 0.969, blue: 0.984, alpha: 1)
        webView.scrollView.backgroundColor = webView.backgroundColor

        view.backgroundColor = webView.backgroundColor
        view.addSubview(webView)
        NSLayoutConstraint.activate([
            webView.leadingAnchor.constraint(equalTo: view.leadingAnchor),
            webView.trailingAnchor.constraint(equalTo: view.trailingAnchor),
            webView.topAnchor.constraint(equalTo: view.topAnchor),
            webView.bottomAnchor.constraint(equalTo: view.bottomAnchor),
        ])

        loadApp()
    }

    private func loadApp() {
        guard let dir = Bundle.main.url(forResource: "www", withExtension: nil) else {
            assertionFailure("找不到 www 目录，请先运行 scripts/prepare-ios.sh")
            return
        }
        let index = dir.appendingPathComponent("index.html")
        webView.loadFileURL(index, allowingReadAccessTo: dir)
    }

    // 网页调用 window.webkit.messageHandlers.saveBackup.postMessage(...) 时写入「文件」App
    func userContentController(_ userContentController: WKUserContentController,
                               didReceive message: WKScriptMessage) {
        guard message.name == "saveBackup",
              let body = message.body as? [String: Any],
              let name = body["name"] as? String,
              let content = body["content"] as? String else { return }

        let fm = FileManager.default
        guard let docs = fm.urls(for: .documentDirectory, in: .userDomainMask).first else { return }
        let dir = docs.appendingPathComponent("PressureMap", isDirectory: true)
        do {
            try fm.createDirectory(at: dir, withIntermediateDirectories: true)
            try content.data(using: .utf8)?.write(to: dir.appendingPathComponent(name), options: .atomic)
        } catch {
            NSLog("备份写入失败: \(error)")
        }
    }

    override var preferredStatusBarStyle: UIStatusBarStyle {
        return traitCollection.userInterfaceStyle == .dark ? .lightContent : .darkContent
    }

    override var prefersStatusBarHidden: Bool { false }
}
