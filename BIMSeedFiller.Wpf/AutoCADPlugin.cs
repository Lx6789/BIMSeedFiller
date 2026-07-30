using Autodesk.AutoCAD.Runtime;
using BIMSeedFiller.Wpf.ViewModels;
using System.Windows;
using System.Windows.Interop;

[assembly: ExtensionApplication(null)]

namespace BIMSeedFiller.Wpf
{
    public class AutoCADPlugin
    {
        private static MainWindow _mainWindow;

        [CommandMethod("ShowSeedFiller")]
        public void ShowMainWindow()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                Autodesk.AutoCAD.ApplicationServices.Application.ShowAlertDialog("请先打开一个AutoCAD文档。");
                return;
            }

            // 手动创建 WPF 应用程序实例（如果还没有）
            if (Application.Current == null)
            {
                new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            }

            if (_mainWindow == null)
            {
                _mainWindow = new MainWindow();
                _mainWindow.DataContext = new MainViewModel();

                // 关闭窗口时：隐藏窗口 + 重置所有选择状态和预览
                _mainWindow.Closing += (s, e) =>
                {
                    e.Cancel = true;      // 阻止真正关闭，只是隐藏窗口
                    _mainWindow.Hide();

                    // 调用 ViewModel 的 Clear 方法清空选择、预览和状态栏
                    if (_mainWindow.DataContext is MainViewModel vm)
                    {
                        vm.Clear();
                    }
                };
            }

            // 设置窗口归属，让它随AutoCAD置顶
            SetOwnerToAutoCAD(_mainWindow);

            _mainWindow.Show();
            _mainWindow.Activate();
        }

        /// <summary>
        /// 将WPF窗口设置为AutoCAD主窗口的子窗口
        /// </summary>
        private static void SetOwnerToAutoCAD(Window window)
        {
            // 获取AutoCAD主窗口的句柄
            var acadMainWindow = Autodesk.AutoCAD.ApplicationServices.Application.MainWindow;
            var acadHandle = new System.Windows.Interop.WindowInteropHelper(window);
            acadHandle.Owner = acadMainWindow.Handle;
        }
    }
}