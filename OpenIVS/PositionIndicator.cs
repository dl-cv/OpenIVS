using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OpenIVS
{
    /// <summary>
    /// 位置指示器自定义控件
    /// </summary>
    public class PositionIndicator : Control
    {
        #region 私有成员
        private float _position = 0; // 当前位置
        private float _minPosition = 0; // 最小位置
        private float _maxPosition = 100; // 最大位置
        private List<PositionMark> _positionMarks = new List<PositionMark>(); // 位置标记点
        private Color _trackColor = Color.LightGray; // 轨道颜色
        private Color _progressColor = Color.DodgerBlue; // 进度颜色
        private Color _markerColor = Color.Red; // 标记点颜色
        private int _trackHeight = 10; // 轨道高度
        private int _markerSize = 16; // 标记点大小
        private bool _showPositionText = true; // 是否显示位置文本
        #endregion

        #region 公开属性
        [Category("位置指示器")]
        [Description("当前位置值")]
        public float Position
        {
            get { return _position; }
            set
            {
                if (_position != value)
                {
                    _position = Math.Max(_minPosition, Math.Min(_maxPosition, value));
                    Invalidate();
                }
            }
        }

        [Category("位置指示器")]
        [Description("最小位置值")]
        public float MinPosition
        {
            get { return _minPosition; }
            set
            {
                if (_minPosition != value)
                {
                    _minPosition = value;
                    if (_position < _minPosition)
                        _position = _minPosition;
                    Invalidate();
                }
            }
        }

        [Category("位置指示器")]
        [Description("最大位置值")]
        public float MaxPosition
        {
            get { return _maxPosition; }
            set
            {
                if (_maxPosition != value)
                {
                    _maxPosition = value;
                    if (_position > _maxPosition)
                        _position = _maxPosition;
                    Invalidate();
                }
            }
        }

        [Category("位置指示器")]
        [Description("轨道颜色")]
        public Color TrackColor
        {
            get { return _trackColor; }
            set
            {
                if (_trackColor != value)
                {
                    _trackColor = value;
                    Invalidate();
                }
            }
        }

        [Category("位置指示器")]
        [Description("进度颜色")]
        public Color ProgressColor
        {
            get { return _progressColor; }
            set
            {
                if (_progressColor != value)
                {
                    _progressColor = value;
                    Invalidate();
                }
            }
        }

        [Category("位置指示器")]
        [Description("标记点颜色")]
        public Color MarkerColor
        {
            get { return _markerColor; }
            set
            {
                if (_markerColor != value)
                {
                    _markerColor = value;
                    Invalidate();
                }
            }
        }

        [Category("位置指示器")]
        [Description("轨道高度")]
        public int TrackHeight
        {
            get { return _trackHeight; }
            set
            {
                if (_trackHeight != value)
                {
                    _trackHeight = Math.Max(1, value);
                    Invalidate();
                }
            }
        }

        [Category("位置指示器")]
        [Description("标记点大小")]
        public int MarkerSize
        {
            get { return _markerSize; }
            set
            {
                if (_markerSize != value)
                {
                    _markerSize = Math.Max(4, value);
                    Invalidate();
                }
            }
        }

        [Category("位置指示器")]
        [Description("是否显示位置文本")]
        public bool ShowPositionText
        {
            get { return _showPositionText; }
            set
            {
                if (_showPositionText != value)
                {
                    _showPositionText = value;
                    Invalidate();
                }
            }
        }
        #endregion

        #region 构造函数
        public PositionIndicator()
        {
            SetStyle(ControlStyles.UserPaint | 
                     ControlStyles.AllPaintingInWmPaint | 
                     ControlStyles.OptimizedDoubleBuffer | 
                     ControlStyles.ResizeRedraw, true);

            Size = new Size(400, 50);
            BackColor = Color.White;
            Font = new Font("Microsoft Sans Serif", 9F);
        }
        #endregion

        #region 公开方法
        /// <summary>
        /// 添加位置标记点
        /// </summary>
        /// <param name="position">标记点位置</param>
        /// <param name="description">标记点描述</param>
        /// <param name="color">标记点颜色（可选）</param>
        public void AddPositionMark(float position, string description = "", Color? color = null)
        {
            _positionMarks.Add(new PositionMark
            {
                Position = position,
                Description = description,
                Color = color ?? _markerColor
            });
            _positionMarks = _positionMarks.OrderBy(m => m.Position).ToList();
            Invalidate();
        }

        /// <summary>
        /// 清除所有位置标记点
        /// </summary>
        public void ClearPositionMarks()
        {
            _positionMarks.Clear();
            Invalidate();
        }

        /// <summary>
        /// 设置多个位置标记点
        /// </summary>
        /// <param name="positions">位置数组</param>
        public void SetPositionMarks(float[] positions)
        {
            _positionMarks.Clear();
            foreach (var pos in positions)
            {
                AddPositionMark(pos);
            }
            Invalidate();
        }
        #endregion

        #region 绘制方法
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 计算轨道区域
            int trackTop = (Height - _trackHeight) / 2;
            Rectangle trackRect = new Rectangle(0, trackTop, Width, _trackHeight);
            
            // 绘制轨道背景
            using (var brush = new SolidBrush(_trackColor))
            {
                g.FillRectangle(brush, trackRect);
            }

            // 计算进度宽度
            int progressWidth = 0;
            if (_maxPosition > _minPosition)
            {
                float percent = (_position - _minPosition) / (_maxPosition - _minPosition);
                progressWidth = (int)(percent * Width);
                progressWidth = Math.Max(0, Math.Min(Width, progressWidth));
            }

            // 绘制进度条
            //if (progressWidth > 0)
            //{
            //    Rectangle progressRect = new Rectangle(0, trackTop, progressWidth, _trackHeight);
            //    using (var brush = new SolidBrush(_progressColor))
            //    {
            //        g.FillRectangle(brush, progressRect);
            //    }
            //}


            // 绘制标记点
            foreach (var mark in _positionMarks)
            {
                if (mark.Position >= _minPosition && mark.Position <= _maxPosition)
                {
                    float percent = (mark.Position - _minPosition) / (_maxPosition - _minPosition);
                    int markPos = (int)(percent * Width);

                    // 绘制标记点圆圈
                    Rectangle markRect = new Rectangle(
                        markPos - _markerSize / 2,
                        trackTop + _trackHeight / 2 - _markerSize / 2,
                        _markerSize,
                        _markerSize);

                    using (var brush = new SolidBrush(mark.Color))
                    {
                        g.FillEllipse(brush, markRect);
                    }
                }
            }

            // 绘制位置指示器
            int indicatorPos = progressWidth;
            int indicatorSize = _markerSize * 3 / 2;
            int indicatorTop = trackTop - (indicatorSize - _trackHeight) / 2;
            
            using (var brush = new SolidBrush(Color.DarkGreen))
            {
                Point[] rect = new Point[4];
                rect[0] = new Point(indicatorPos - indicatorSize / 2, indicatorTop);
                rect[1] = new Point(indicatorPos + indicatorSize / 2, indicatorTop);
                rect[2] = new Point(indicatorPos + indicatorSize / 2, indicatorTop + indicatorSize);
                rect[3] = new Point(indicatorPos - indicatorSize / 2, indicatorTop + indicatorSize);
                g.FillPolygon(brush, rect);
            }

        }
        #endregion

        #region 内部类
        // 位置标记点类
        private class PositionMark
        {
            public float Position { get; set; }
            public string Description { get; set; }
            public Color Color { get; set; }
        }
        #endregion
    }
} 