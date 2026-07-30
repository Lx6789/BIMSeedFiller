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

        public MainViewModel()
        {
            _seedData = new SeedData();
            _boundaryData = new BoundaryData();

            getSeedCommand = new RelayCommand(ExecuteGetSeed);
            getBoundaryCommand = new RelayCommand(ExecuteGetBoundary);
            FillCommand = new RelayCommand(ExecuteFillCommand);
            PreviewCommand = new RelayCommand(ExecutePreview);
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

        private void ExecuteGetSeed()
        {
            ActivateAutoCADWindow();

            Editor ed = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor;
            seedResult = InputCollector.GetSeed(ed);

            // 明确不更新WPF状态，防止崩溃
            if (seedResult.Status != PromptStatus.OK)
                return;

            SeedData seedData = TileFiller.ExtractSeedData(seedResult);
            if (seedData == null || seedData.Geometry == null)
                return;

            SeedData = seedData;
        }


        private void ExecuteGetBoundary()
        {
            ActivateAutoCADWindow();

            Editor ed = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument.Editor;
            boundaryResult = InputCollector.GetBoundary(ed);

            if (boundaryResult.Status != PromptStatus.OK)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    Status = "边界选择失败，请重试。";
                }));
                return;
            }

            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (seedResult.Status == PromptStatus.OK)
                    Status = "种子和边界已选择，可以进行填充或预览。";
                else
                    Status = "边界已选择，请选择种子...";
            }));
        }

        private void ExecuteFillCommand()
        {
            try
            {
                if (seedResult.Status != PromptStatus.OK || boundaryResult.Status != PromptStatus.OK)
                {
                    MessageBox.Show("请先选择种子和边界。");
                    return;
                }

                // 验证 ObjectId 有效性（对象是否已被删除）
                if (seedResult.ObjectId.IsNull || seedResult.ObjectId.IsErased ||
                    boundaryResult.ObjectId.IsNull || boundaryResult.ObjectId.IsErased)
                {
                    MessageBox.Show("种子或边界图形已失效，请重新选择。");
                    Status = "图形已失效，请重新选择。";
                    return;
                }

                Status = "正在填充...";

                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                using (doc.LockDocument())
                {
                    bool success = TileFiller.Fill(seedResult, boundaryResult);
                    if (success)
                    {
                        doc.Editor.WriteMessage("\n填充完成！");
                        doc.Editor.Regen();
                        Status = "填充完成！";
                    }
                    else
                    {
                        doc.Editor.WriteMessage("\n填充失败，请检查种子和边界是否正确选择。");
                        Status = "填充失败。";
                    }
                }
            }
            catch (Exception ex)
            {
                Status = $"填充出错：{ex.Message}";
                MessageBox.Show($"填充出错：{ex.Message}", "错误");
            }
        }

        private void ExecutePreview()
        {
            try
            {
                if (seedResult.Status != PromptStatus.OK || boundaryResult.Status != PromptStatus.OK)
                {
                    MessageBox.Show("请先选择种子和边界。");
                    return;
                }

                if (seedResult.ObjectId.IsNull || seedResult.ObjectId.IsErased ||
                    boundaryResult.ObjectId.IsNull || boundaryResult.ObjectId.IsErased)
                {
                    MessageBox.Show("种子或边界图形已失效，请重新选择。");
                    Status = "图形已失效，请重新选择。";
                    return;
                }

                Status = "正在生成预览...";

                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                using (doc.LockDocument())
                {
                    PreviewPolygons = TileFiller.Preview(seedResult, boundaryResult);
                }

                if (PreviewPolygons == null || PreviewPolygons.Count == 0)
                {
                    Status = "预览结果为空，请检查种子和边界。";
                    MessageBox.Show("预览结果为空，请检查种子和边界是否有效。");
                }
                else
                {
                    Status = $"预览完成，共 {PreviewPolygons.Count} 个瓦片。";
                }
            }
            catch (Exception ex)
            {
                Status = $"预览出错：{ex.Message}";
                MessageBox.Show($"预览出错：{ex.Message}");
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