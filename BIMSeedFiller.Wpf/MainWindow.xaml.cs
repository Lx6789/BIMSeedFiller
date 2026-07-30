using BIMSeedFiller.Wpf.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BIMSeedFiller.Wpf
{
    public partial class MainWindow : Window
    {
        private MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();

            // 等 DataContext 被设置后再订阅事件
            this.DataContextChanged += (s, e) =>
            {
                if (_viewModel != null)
                    _viewModel.OnPreviewDataChanged -= DrawPreview;

                _viewModel = e.NewValue as MainViewModel;
                if (_viewModel != null)
                    _viewModel.OnPreviewDataChanged += DrawPreview;
            };
        }

        private void DrawPreview()
        {
            PreviewCanvas.Children.Clear();

            if (_viewModel?.PreviewPolygons == null || _viewModel.PreviewPolygons.Count == 0)
                return;

            double canvasWidth = PreviewCanvas.ActualWidth;
            double canvasHeight = PreviewCanvas.ActualHeight;
            if (canvasWidth < 1) canvasWidth = PreviewCanvas.Width;
            if (canvasHeight < 1) canvasHeight = PreviewCanvas.Height;
            if (canvasWidth < 1 || canvasHeight < 1) return;

            // 计算所有多边形的包围盒
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (var polygon in _viewModel.PreviewPolygons)
            {
                foreach (var pt in polygon)
                {
                    if (pt.X < minX) minX = pt.X;
                    if (pt.Y < minY) minY = pt.Y;
                    if (pt.X > maxX) maxX = pt.X;
                    if (pt.Y > maxY) maxY = pt.Y;
                }
            }

            double dataWidth = maxX - minX;
            double dataHeight = maxY - minY;

            if (dataWidth < 0.001 || dataHeight < 0.001)
            {
                var rect = new Rectangle
                {
                    Width = canvasWidth * 0.8,
                    Height = canvasHeight * 0.8,
                    Stroke = Brushes.Blue,
                    Fill = Brushes.LightBlue,
                    Opacity = 0.5
                };
                Canvas.SetLeft(rect, canvasWidth * 0.1);
                Canvas.SetTop(rect, canvasHeight * 0.1);
                PreviewCanvas.Children.Add(rect);
                return;
            }

            double scale = Math.Min(canvasWidth / dataWidth, canvasHeight / dataHeight) * 0.9;
            double offsetX = (canvasWidth - dataWidth * scale) / 2;
            double offsetY = (canvasHeight - dataHeight * scale) / 2;

            foreach (var polygon in _viewModel.PreviewPolygons)
            {
                if (polygon.Count < 3) continue;

                var wpfPolygon = new Polygon
                {
                    Stroke = Brushes.Blue,
                    StrokeThickness = 1,
                    Fill = Brushes.LightBlue,
                    Opacity = 0.5
                };

                foreach (var pt in polygon)
                {
                    double x = (pt.X - minX) * scale + offsetX;
                    double y = canvasHeight - (pt.Y - minY) * scale - offsetY;
                    wpfPolygon.Points.Add(new System.Windows.Point(x, y));
                }

                PreviewCanvas.Children.Add(wpfPolygon);
            }
        }
    }
}