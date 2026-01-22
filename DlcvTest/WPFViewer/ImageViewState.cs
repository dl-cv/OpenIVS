using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DlcvTest.WPFViewer
{
    /// <summary>
    /// 共享视图状态：用于两个 Viewer 同步缩放/平移。
    /// 坐标约定：屏幕坐标 = Scale * 图像像素坐标 + Offset。
    /// </summary>
    public sealed class ImageViewState : INotifyPropertyChanged
    {
        private double _scale = 1.0;
        private double _offsetX = 0.0;
        private double _offsetY = 0.0;
        private int _zoomLevel = 0;

        /// <summary>
        /// 缩放倍率（>0）。
        /// </summary>
        public double Scale
        {
            get => _scale;
            set
            {
                if (value <= 0) value = 1e-6;
                if (Math.Abs(_scale - value) < 1e-12) return;
                _scale = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// X 平移（屏幕像素/DIP）。
        /// </summary>
        public double OffsetX
        {
            get => _offsetX;
            set
            {
                if (Math.Abs(_offsetX - value) < 1e-12) return;
                _offsetX = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Y 平移（屏幕像素/DIP）。
        /// </summary>
        public double OffsetY
        {
            get => _offsetY;
            set
            {
                if (Math.Abs(_offsetY - value) < 1e-12) return;
                _offsetY = value;
                OnPropertyChanged();
            }
        }

        public int ZoomLevel
        {
            get => _zoomLevel;
            set
            {
                if (_zoomLevel == value) return;
                _zoomLevel = value;
                OnPropertyChanged();
            }
        }

        public void SetView(double scale, double offsetX, double offsetY)
        {
            if (scale <= 0) scale = 1e-6;

            bool scaleChanged = Math.Abs(_scale - scale) >= 1e-12;
            bool xChanged = Math.Abs(_offsetX - offsetX) >= 1e-12;
            bool yChanged = Math.Abs(_offsetY - offsetY) >= 1e-12;

            if (!scaleChanged && !xChanged && !yChanged) return;

            if (scaleChanged) _scale = scale;
            if (xChanged) _offsetX = offsetX;
            if (yChanged) _offsetY = offsetY;

            // 仅通知真正变化的属性，避免高频交互时触发多余 UI 更新。            if (scaleChanged) OnPropertyChanged(nameof(Scale));
            if (xChanged) OnPropertyChanged(nameof(OffsetX));
            if (yChanged) OnPropertyChanged(nameof(OffsetY));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}


