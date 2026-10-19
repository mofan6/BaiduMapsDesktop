using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace BaiduMapsDesktop
{
    // Selection coordinates are DIPs; PNG cropping uses actual capture pixels.
    // A frozen frame keeps the selected map identical to the saved image.
    internal sealed partial class MapWindow
    {
        private Canvas captureCanvas;
        private BitmapSource captureFrame;
        private Point captureAnchor;
        private bool preparingCapture, drawingCapture, saveCaptureDialogOpen;

        private void InitializeScreenshotSelection()
        {
            captureCanvas = (Canvas)Window.FindName("CaptureCanvas");
            model.SaveCapture = new RelayCommand(SaveScreenshotSelection, delegate { return model.HasCaptureSelection; });
            model.ResetCapture = new RelayCommand(ResetScreenshot);
            model.CancelCapture = new RelayCommand(CancelScreenshot);
            captureCanvas.SizeChanged += delegate { UpdateCaptureViewport(); };
            captureCanvas.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (!model.CaptureSelecting) return;
                captureAnchor = ClampCapturePoint(e.GetPosition(captureCanvas));
                drawingCapture = true;
                model.CaptureRegion = new Rect(captureAnchor, captureAnchor);
                captureCanvas.Focus();
                captureCanvas.CaptureMouse();
                e.Handled = true;
            };
            captureCanvas.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (drawingCapture && e.LeftButton == MouseButtonState.Pressed)
                    model.CaptureRegion = new Rect(captureAnchor, ClampCapturePoint(e.GetPosition(captureCanvas)));
            };
            captureCanvas.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                if (!drawingCapture) return;
                model.CaptureRegion = new Rect(captureAnchor, ClampCapturePoint(e.GetPosition(captureCanvas)));
                drawingCapture = false;
                captureCanvas.ReleaseMouseCapture();
                e.Handled = true;
                if (model.HasCaptureSelection)
                    Window.Dispatcher.BeginInvoke(new Action(SaveScreenshotSelection), System.Windows.Threading.DispatcherPriority.Background);
            };
            captureCanvas.LostMouseCapture += delegate { drawingCapture = false; };
        }

        private async void SaveScreenshot()
        {
            if (browser.CoreWebView2 == null || !model.Ready || preparingCapture || closing) return;
            if (model.CaptureSelecting) { ResetScreenshot(); return; }
            preparingCapture = true;
            try
            {
                using (var stream = new MemoryStream())
                {
                    await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                    if (closing) return;
                    stream.Position = 0;
                    var frame = new BitmapImage();
                    frame.BeginInit();
                    frame.CacheOption = BitmapCacheOption.OnLoad;
                    frame.StreamSource = stream;
                    frame.EndInit();
                    frame.Freeze();
                    captureFrame = frame;
                }
                model.Library = false;
                model.CaptureImage = captureFrame;
                model.CaptureRegion = Rect.Empty;
                model.CaptureSelecting = true;
                Window.UpdateLayout();
                UpdateCaptureViewport();
                captureCanvas.Focus();
            }
            catch (Exception ex)
            {
                CancelScreenshot();
                MessageBox.Show(Window, "截图准备失败：" + ex.Message, "地图截图", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { preparingCapture = false; }
        }

        private void UpdateCaptureViewport()
        {
            if (captureFrame != null && captureCanvas.ActualWidth > 0 && captureCanvas.ActualHeight > 0)
                model.SetCaptureViewport(captureCanvas.ActualWidth, captureCanvas.ActualHeight, captureFrame.PixelWidth, captureFrame.PixelHeight);
        }
        private Point ClampCapturePoint(Point point)
        {
            return new Point(Math.Max(0, Math.Min(captureCanvas.ActualWidth, point.X)), Math.Max(0, Math.Min(captureCanvas.ActualHeight, point.Y)));
        }
        private void ResetScreenshot()
        {
            drawingCapture = false;
            captureCanvas.ReleaseMouseCapture();
            model.CaptureRegion = Rect.Empty;
            captureCanvas.Focus();
        }
        private void CancelScreenshot()
        {
            ResetScreenshot();
            model.CaptureSelecting = false;
            model.CaptureImage = null;
            captureFrame = null;
        }
        private void SaveScreenshotSelection()
        {
            if (captureFrame == null || !model.HasCaptureSelection || saveCaptureDialogOpen) return;
            var dialog = new SaveFileDialog { Title = "保存框选截图", Filter = "PNG 图片 (*.png)|*.png", FileName = "百度地图_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png", AddExtension = true, OverwritePrompt = true };
            saveCaptureDialogOpen = true;
            try
            {
                if (dialog.ShowDialog(Window) != true) return;
                Int32Rect pixels = ScreenshotCrop.ToPixels(model.CaptureRegion, captureCanvas.ActualWidth, captureCanvas.ActualHeight, captureFrame.PixelWidth, captureFrame.PixelHeight);
                var crop = new CroppedBitmap(captureFrame, pixels);
                crop.Freeze();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(crop));
                using (var stream = File.Create(dialog.FileName)) encoder.Save(stream);
                CancelScreenshot();
            }
            catch (Exception ex) { MessageBox.Show(Window, "截图保存失败：" + ex.Message, "地图截图", MessageBoxButton.OK, MessageBoxImage.Error); }
            finally { saveCaptureDialogOpen = false; }
        }
    }

    internal static class ScreenshotCrop
    {
        internal static Int32Rect ToPixels(Rect selection, double displayWidth, double displayHeight, int pixelWidth, int pixelHeight)
        {
            if (selection.IsEmpty || displayWidth <= 0 || displayHeight <= 0 || pixelWidth <= 0 || pixelHeight <= 0) throw new ArgumentException("无有效选区");
            double scaleX = pixelWidth / displayWidth, scaleY = pixelHeight / displayHeight;
            int left = Math.Max(0, Math.Min(pixelWidth - 1, (int)Math.Floor(selection.Left * scaleX)));
            int top = Math.Max(0, Math.Min(pixelHeight - 1, (int)Math.Floor(selection.Top * scaleY)));
            int right = Math.Max(left + 1, Math.Min(pixelWidth, (int)Math.Ceiling(selection.Right * scaleX)));
            int bottom = Math.Max(top + 1, Math.Min(pixelHeight, (int)Math.Ceiling(selection.Bottom * scaleY)));
            return new Int32Rect(left, top, right - left, bottom - top);
        }
    }

    internal sealed partial class MapViewModel
    {
        private bool captureSelecting;
        private Rect captureRegion = Rect.Empty;
        private ImageSource captureImage;
        private double captureViewWidth, captureViewHeight;
        private int capturePixelWidth, capturePixelHeight;
        public bool CaptureSelecting { get { return captureSelecting; } set { captureSelecting = value; Changed("CaptureVisibility"); UpdateBrowserLayers(); } }
        public Visibility CaptureVisibility { get { return captureSelecting ? Visibility.Visible : Visibility.Collapsed; } }
        public ImageSource CaptureImage { get { return captureImage; } set { captureImage = value; Changed("CaptureImage"); } }
        public Rect CaptureRegion
        {
            get { return captureRegion; }
            set
            {
                captureRegion = value;
                Changed("CaptureLeft"); Changed("CaptureTop"); Changed("CaptureWidth"); Changed("CaptureHeight");
                Changed("CaptureSelectionVisibility"); Changed("CaptureMask"); Changed("CaptureHint");
                if (SaveCapture != null) SaveCapture.Refresh();
            }
        }
        public double CaptureLeft { get { return captureRegion.IsEmpty ? 0 : captureRegion.Left; } }
        public double CaptureTop { get { return captureRegion.IsEmpty ? 0 : captureRegion.Top; } }
        public double CaptureWidth { get { return captureRegion.IsEmpty ? 0 : captureRegion.Width; } }
        public double CaptureHeight { get { return captureRegion.IsEmpty ? 0 : captureRegion.Height; } }
        public bool HasCaptureSelection { get { return CaptureWidth >= 4 && CaptureHeight >= 4; } }
        public Visibility CaptureSelectionVisibility { get { return HasCaptureSelection ? Visibility.Visible : Visibility.Collapsed; } }
        public Geometry CaptureMask
        {
            get
            {
                var geometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
                geometry.Children.Add(new RectangleGeometry(new Rect(0, 0, captureViewWidth, captureViewHeight)));
                if (HasCaptureSelection) geometry.Children.Add(new RectangleGeometry(captureRegion));
                geometry.Freeze();
                return geometry;
            }
        }
        public string CaptureHint
        {
            get
            {
                if (!HasCaptureSelection || captureViewWidth <= 0 || captureViewHeight <= 0) return "拖动框选矩形，松开后选择保存位置 · Esc 取消";
                var pixels = ScreenshotCrop.ToPixels(captureRegion, captureViewWidth, captureViewHeight, capturePixelWidth, capturePixelHeight);
                return pixels.Width + " × " + pixels.Height + " 像素 · Enter 保存 · 可再次拖动重新框选";
            }
        }
        public void SetCaptureViewport(double width, double height, int pixelsWide, int pixelsHigh)
        {
            captureViewWidth = width; captureViewHeight = height; capturePixelWidth = pixelsWide; capturePixelHeight = pixelsHigh;
            Changed("CaptureMask"); Changed("CaptureHint");
        }
        public RelayCommand SaveCapture { get; set; }
        public RelayCommand ResetCapture { get; set; }
        public RelayCommand CancelCapture { get; set; }
    }
}
