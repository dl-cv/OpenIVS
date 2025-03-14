using System;
using System.Windows.Forms;

namespace DlcvHttpApiTest
{
    /// <summary>
    /// 等待光标辅助类，用于显示等待状态
    /// </summary>
    public class WaitCursor : IDisposable
    {
        private readonly Control _control;
        private readonly Cursor _previousCursor;
        private readonly bool _disposed = false;

        /// <summary>
        /// 创建等待光标实例并立即显示等待状态
        /// </summary>
        /// <param name="control">要应用光标的控件</param>
        public WaitCursor(Control control)
        {
            _control = control ?? throw new ArgumentNullException(nameof(control));
            _previousCursor = _control.Cursor;
            _control.Cursor = Cursors.WaitCursor;
        }

        /// <summary>
        /// 释放资源，恢复原始光标
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                if (_control != null)
                {
                    try
                    {
                        if (_control.InvokeRequired)
                        {
                            _control.BeginInvoke(new Action(() => _control.Cursor = _previousCursor));
                        }
                        else
                        {
                            _control.Cursor = _previousCursor;
                        }
                    }
                    catch
                    {
                        // 如果控件已释放则忽略异常
                    }
                }
            }
        }
    }
} 