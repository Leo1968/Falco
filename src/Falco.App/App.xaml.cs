using System.Windows;
using Falco.App.Services;

namespace Falco.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Lang.Initialize();   // 读取语言偏好并加载资源字典（默认中文）
        Theme.Initialize();  // 读取主题偏好（默认暗黑）
        // 引擎自检：--engine-test 执行 AH-001 应用→还原往返并退出（供验收/诊断）
        if (e.Args.Contains("--engine-test"))
        {
            var eng = new TweakEngine();
            AppEnv.Log("[自检] 开始 AH-001 应用→还原往返");
            var (pass, fail) = eng.Apply("AH-001", 8, 22);
            AppEnv.Log($"[自检] Apply：pass={pass} fail={fail}");
            eng.Revert("AH-001");
            AppEnv.Log("[自检] Revert 完成，往返结束");
            Shutdown(0);
            return;
        }
        base.OnStartup(e);
        // 全局兜底：UI 线程未处理异常记入日志并保活（防白屏/闪退无迹可查）
        DispatcherUnhandledException += (_, e) =>
        {
            AppEnv.Log($"UI 未处理异常：{e.Exception}", "ERROR");
            e.Handled = true;
        };
    }
}
