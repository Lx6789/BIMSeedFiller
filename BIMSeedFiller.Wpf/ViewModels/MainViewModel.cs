using BIMSeedFiller.Core;
using BIMSeedFiller.Core.Data;
using BIMSeedFiller.Core.DataModel;
using BIMSeedFiller.Wpf.Commands;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace BIMSeedFiller.Wpf.ViewModels
{
    class MainViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private SeedData _seedData;
        private BoundaryData _boundaryData;

        PromptEntityResult seedResult;
        PromptEntityResult boundaryResult;

        public ICommand getSeedCommand { get; }
        public ICommand getBoundaryCommand { get; }
        public ICommand FillCommand { get; }

        public MainViewModel()
        {
            _seedData = new SeedData();
            _boundaryData = new BoundaryData();

            getSeedCommand = new RelayCommand(ExecuteGetSeed);
            getBoundaryCommand = new RelayCommand(ExecuteGetBoundary);
            FillCommand = new RelayCommand(ExecuteFillCommand);
        }

        public SeedData SeedData
        {
            get => _seedData;
            set
            {
                _seedData = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SeedData)));
            }
        }

        public BoundaryData BoundaryData
        {
            get => _boundaryData;
            set
            {
                _boundaryData = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BoundaryData)));
            }
        }

        /// <summary>
        /// 激活 AutoCAD 主窗口，使其获得焦点
        /// </summary>
        private void ActivateAutoCADWindow()
        {
            var mainWindow = Autodesk.AutoCAD.ApplicationServices.Application.MainWindow;
            if (mainWindow != null)
            {
                int hWnd = (int)mainWindow.Handle; // 或者 mainWindow.Handle.ToInt32()
                NativeMethods.SetForegroundWindow(hWnd);
            }
        }

        /// <summary>
        /// 执行获取种子
        /// </summary>
        private void ExecuteGetSeed()
        {
            ActivateAutoCADWindow();

            Editor ed = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor;
            seedResult = InputCollector.GetSeed(ed);

            if (seedResult == null)
            {
                MessageBox.Show("未知错误...");
                return;
            }

            SeedData seedData = TileFiller.ExtractSeedData(seedResult);
            if (seedData == null)
            {
                MessageBox.Show("未知错误");
                return;
            }
            else
            {
                ed.WriteMessage("\n种子选择成功...");
            }

            SeedData = seedData;
        }

        /// <summary>
        /// 执行获取边界
        /// </summary>
        private void ExecuteGetBoundary()
        {
            ActivateAutoCADWindow();

            Editor ed = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor;
            boundaryResult = InputCollector.GetBoundary(ed);

            if (boundaryResult == null)
            {
                MessageBox.Show("未知错误...");
                return;
            }
            else
            {
                ed.WriteMessage("\n边界选择成功...");
            }
        }

        /// <summary>
        /// 执行种子填充命令
        /// </summary>
        private void ExecuteFillCommand()
        {
            try
            {
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                using (doc.LockDocument())
                {
                    bool success = TileFiller.File(seedResult, boundaryResult);
                    if (success)
                    {
                        doc.Editor.WriteMessage("\n填充完成！");
                        doc.Editor.Regen();
                    }
                    else
                    {
                        doc.Editor.WriteMessage("\n填充失败，请检查种子和边界是否正确选择。");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"填充出错：{ex.Message}", "错误");
            }
        }
    }

    internal static class NativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(int hWnd);
    }
}