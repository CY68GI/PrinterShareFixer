using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using PrinterShareFixer.Core.Update;

namespace PrinterShareFixer.App;

/// <summary>
/// 程序入口。
/// 带 --apply-update 参数时进入"更新模式"：完全不加载界面，等主程序退出后
/// 把新版本替换上去并重新启动（详细流程见 Core/Update/UpdateApplier.cs）。
/// </summary>
public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (UpdateApplier.TryParse(args, out var request) && request is not null)
        {
            return UpdateApplier.Apply(request);
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = p;
            new App();
        });

        return 0;
    }
}
