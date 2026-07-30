using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using BIMSeedFiller.Core;
using BIMSeedFiller.Core.Data;
using BIMSeedFiller.Core.DataModel;
using BIMSeedFiller.Wpf.Commands;
using System;
using System.Collections.Generic;
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

        private string _status = "请选择种子和边界...";
        private List<List<Point2d>> _previewPolygons;

        public event Action OnPreviewDataChanged;

        public ICommand getSeedCommand { get; }
        public ICommand getBoundaryCommand { get; }
        public ICommand FillCommand { get; }
        public ICommand PreviewCommand { get; }
        public ICommand ClearCommand { get; }

        public MainViewModel()
        {
            _seedData = new SeedData();
            _boundaryData = new BoundaryData();

            getSeedCommand = new RelayCommand(ExecuteGetSeed);
            getBoundaryCommand = new RelayCommand(ExecuteGetBoundary);
            FillCommand = new RelayCommand(ExecuteFillCommand);
            PreviewCommand = new RelayCommand(ExecutePreview);
            ClearCommand = new RelayCommand(Clear);
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

        public List<List<Point2d>> PreviewPolygons
        {
            get => _previewPolygons;
            set
            {
                _previewPolygons = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PreviewPolygons)));
                OnPreviewDataChanged?.Invoke();
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
            }
        }

        private void ActivateAutoCADWindow()
        {
            var mainWindow = Autodesk.AutoCAD.ApplicationServices.Application.MainWindow;
            if (mainWindow != null)
            {
                int hWnd = (int)mainWindow.Handle;
                NativeMethods.SetForegroundWindow(hWnd);
            }
        }

        // 选择种子：使用命令行提示，避免WPF崩溃
        private void ExecuteGetSeed()
        {
            ActivateAutoCADWindow();

            Editor ed = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor;
            seedResult = InputCollector.GetSeed(ed);

            if (seedResult.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\n种子选择失败。");
                return;
            }

            SeedData seedData = TileFiller.ExtractSeedData(seedResult);
            if (seedData == null || seedData.Geometry == null)
            {
                ed.WriteMessage("\n种子数据无效。");
                return;
            }

            SeedData = seedData;
            ed.WriteMessage("\n种子已选择。");
        }

        // 选择边界：使用命令行提示，避免WPF崩溃
        private void ExecuteGetBoundary()
        {
            ActivateAutoCADWindow();

            Editor ed = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor;
            boundaryResult = InputCollector.GetBoundary(ed);

            if (boundaryResult.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\n边界选择失败。");
                return;
            }

            ed.WriteMessage("\n边界已选择。");
        }

        /// <summary>
        /// 检查种子和边界是否都已选择
        /// </summary>
        private bool CheckSelectionsAndPrompt(out string message)
        {
            if (seedResult.Status == PromptStatus.OK && boundaryResult.Status == PromptStatus.OK)
            {
                message = null;
                return true;
            }

            if (seedResult.Status != PromptStatus.OK && boundaryResult.Status != PromptStatus.OK)
                message = "请先选择种子和边界。";
            else if (seedResult.Status != PromptStatus.OK)
                message = "请先选择种子。";
            else
                message = "请先选择边界。";

            return false;
        }

        private void ExecuteFillCommand()
        {
            try
            {
                if (!CheckSelectionsAndPrompt(out string msg))
                {
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        Status = msg;
                    }));
                    MessageBox.Show(msg);
                    return;
                }

                if (seedResult.ObjectId.IsNull || seedResult.ObjectId.IsErased ||
                    boundaryResult.ObjectId.IsNull || boundaryResult.ObjectId.IsErased)
                {
                    string err = "种子或边界图形已失效，请重新选择。";
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        Status = err;
                    }));
                    MessageBox.Show(err);
                    return;
                }

                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    Status = "正在填充...";
                }));

                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                using (doc.LockDocument())
                {
                    bool success = TileFiller.Fill(seedResult, boundaryResult);
                    if (success)
                    {
                        doc.Editor.WriteMessage("\n填充完成！");
                        doc.Editor.Regen();
                        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            Status = "填充完成！";
                        }));
                    }
                    else
                    {
                        doc.Editor.WriteMessage("\n填充失败，请检查种子和边界是否正确选择。");
                        System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            Status = "填充失败。";
                        }));
                    }
                }
            }
            catch (Exception ex)
            {
                string err = $"填充出错：{ex.Message}";
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    Status = err;
                }));
                MessageBox.Show(err, "错误");
            }
        }

        private void ExecutePreview()
        {
            try
            {
                if (!CheckSelectionsAndPrompt(out string msg))
                {
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        Status = msg;
                    }));
                    MessageBox.Show(msg);
                    return;
                }

                if (seedResult.ObjectId.IsNull || seedResult.ObjectId.IsErased ||
                    boundaryResult.ObjectId.IsNull || boundaryResult.ObjectId.IsErased)
                {
                    string err = "种子或边界图形已失效，请重新选择。";
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        Status = err;
                    }));
                    MessageBox.Show(err);
                    return;
                }

                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    Status = "正在生成预览...";
                }));

                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                using (doc.LockDocument())
                {
                    PreviewPolygons = TileFiller.Preview(seedResult, boundaryResult);
                }

                if (PreviewPolygons == null || PreviewPolygons.Count == 0)
                {
                    string err = "预览结果为空，请检查种子和边界。";
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        Status = err;
                    }));
                    MessageBox.Show(err);
                }
                else
                {
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        Status = $"预览完成，共 {PreviewPolygons.Count} 个瓦片。";
                    }));
                }
            }
            catch (Exception ex)
            {
                string err = $"预览出错：{ex.Message}";
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    Status = err;
                }));
                MessageBox.Show(err);
            }
        }

        /// <summary>
        /// 重置所有状态：清空选择、清空预览、重置界面
        /// </summary>
        public void Clear()
        {
            // 清空选择
            seedResult = default(PromptEntityResult);
            boundaryResult = default(PromptEntityResult);
            SeedData = new SeedData();
            BoundaryData = new BoundaryData();

            // 清空预览
            PreviewPolygons = null;

            // 更新状态栏
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                Status = "请选择种子和边界...";
            }));
        }
    }

    internal static class NativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(int hWnd);
    }
}