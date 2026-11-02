using System;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KeyMouseStats
{
    internal static class MouseDistance
    {
        public static bool ValidDpi(double dpi)
        {
            return !double.IsNaN(dpi) && dpi >= 1 && dpi <= 1000000;
        }

        public static double ToMeters(double counts, double dpi)
        {
            return ValidDpi(dpi) ? counts / dpi * 0.0254 : 0;
        }

        public static double CalibratedDpi(double counts, double centimeters)
        {
            return centimeters > 0 ? counts * 2.54 / centimeters : 0;
        }
    }

    internal sealed class RawMouseInput
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Device
        {
            public ushort Page, Usage;
            public uint Flags;
            public IntPtr Target;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(Device[] devices, uint count, uint size);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(IntPtr input, uint command,
            [Out] byte[] data, ref uint size, uint headerSize);

        // RAWINPUTHEADER: DWORD + DWORD + HANDLE + WPARAM; RAWMOUSE: 24 bytes.
        private readonly byte[] _buffer = new byte[8 + 2 * IntPtr.Size + 24];
        public bool Registered { get; private set; }
        public int LastDx { get; private set; }
        public int LastDy { get; private set; }
        public long LastDevice { get; private set; }
        public bool RelativeSeen { get; private set; }
        /// <summary>最近一个包的按钮位(RI_MOUSE_* 常量),0 表示这个包没有按键变化。</summary>
        public ushort LastButtonFlags { get; private set; }
        /// <summary>最近一个包的滚轮增量(±120 的倍数通常代表一格)。</summary>
        public short LastWheelDelta { get; private set; }
        private int _error;
        private bool _absoluteSeen;
        private bool _readFailed;

        public string Status
        {
            get
            {
                if (!Registered) return "鼠标采集注册失败，错误码 " + _error;
                if (_readFailed) return "鼠标数据读取失败，请重启程序重试。";
                if (_absoluteSeen) return "已忽略绝对坐标输入；仅支持相对移动的鼠标。";
                return "鼠标采集已启用（后台和隐藏小组件时仍可统计）。";
            }
        }

        public void Register(IntPtr window)
        {
            RelativeSeen=false;
            Device device = new Device { Page = 1, Usage = 2, Flags = 0x100, Target = window };
            Registered = RegisterRawInputDevices(new Device[] { device }, 1, (uint)Marshal.SizeOf(typeof(Device)));
            _error = Registered ? 0 : Marshal.GetLastWin32Error();
        }

        public void Unregister()
        {
            if (!Registered) return;
            Device device = new Device { Page = 1, Usage = 2, Flags = 1, Target = IntPtr.Zero };
            RegisterRawInputDevices(new Device[] { device }, 1, (uint)Marshal.SizeOf(typeof(Device)));
            Registered = false;
        }

        public bool Read(IntPtr input, out double counts)
        {
            counts = 0;
            uint size = (uint)_buffer.Length;
            uint read = GetRawInputData(input, 0x10000003, _buffer, ref size, (uint)(8 + 2 * IntPtr.Size));
            if (read == uint.MaxValue) { _readFailed = true; RelativeSeen=false;return false; }
            _readFailed = false;
            bool absolute;
            bool valid = Decode(_buffer, (int)read, IntPtr.Size, out counts, out absolute);
            _absoluteSeen |= absolute;
            if(absolute)RelativeSeen=false;
            if(valid)
            {
                int header=8+2*IntPtr.Size;LastDx=BitConverter.ToInt32(_buffer,header+12);LastDy=BitConverter.ToInt32(_buffer,header+16);
                LastDevice=IntPtr.Size==8?BitConverter.ToInt64(_buffer,8):BitConverter.ToInt32(_buffer,8);
                ushort flags; short wheel;
                DecodeButtons(_buffer, IntPtr.Size, out flags, out wheel);
                LastButtonFlags = flags;
                LastWheelDelta = wheel;
                if(counts>0)RelativeSeen=true;
            }
            return valid;
        }

        /// <summary>
        /// 取一个 RAWMOUSE 包里的按钮位与滚轮增量。usButtonFlags 在 RAWMOUSE 偏移 4,
        /// usButtonData 在偏移 6(两者是同一个 DWORD 的上下半字)。
        /// </summary>
        internal static bool DecodeButtons(byte[] packet, int pointerSize, out ushort buttonFlags, out short wheelDelta)
        {
            buttonFlags = 0;
            wheelDelta = 0;
            if (pointerSize != 4 && pointerSize != 8) return false;
            int header = 8 + 2 * pointerSize;
            if (packet == null || packet.Length < header + 24) return false;
            if (BitConverter.ToUInt32(packet, 0) != 0) return false;   // 不是鼠标包
            buttonFlags = BitConverter.ToUInt16(packet, header + 4);
            wheelDelta = BitConverter.ToInt16(packet, header + 6);
            return true;
        }

        internal static bool Decode(byte[] packet, int length, int pointerSize, out double counts, out bool absolute)
        {
            counts = 0;
            absolute = false;
            if (pointerSize != 4 && pointerSize != 8) return false;
            int header = 8 + 2 * pointerSize;
            if (packet == null || length < header + 24 || length > packet.Length) return false;
            if (BitConverter.ToUInt32(packet, 0) != 0) return false; // RIM_TYPEMOUSE
            uint declaredSize = BitConverter.ToUInt32(packet, 4);
            if (declaredSize < header + 24 || declaredSize > length) return false;
            absolute = (BitConverter.ToUInt16(packet, header) & 1) != 0;
            if (absolute) return false; // 绝对坐标并不是传感器计数，不能按 DPI 换算。
            // RAWMOUSE 的按钮 union 按 DWORD 对齐，lLastX/Y 偏移为 12/16。
            double dx = BitConverter.ToInt32(packet, header + 12);
            double dy = BitConverter.ToInt32(packet, header + 16);
            counts = Math.Sqrt(dx * dx + dy * dy);
            return true;
        }
    }

    /// <summary>
    /// 键盘原始输入。这里**不计数**——计数仍由低级键盘钩子做(它的时序更准,按住时长要用)。
    /// 这个类的唯一职责是当「探针」:系统明明在收按键,钩子却一声不吭,说明钩子已经被摘了。
    /// 有了它,键盘停摆的发现时间从「30 秒看门狗」缩短到「1.5 秒」。
    /// </summary>
    internal sealed class RawKeyInput
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Device
        {
            public ushort Page, Usage;
            public uint Flags;
            public IntPtr Target;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(Device[] devices, uint count, uint size);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(IntPtr input, uint command,
            [Out] byte[] data, ref uint size, uint headerSize);

        private readonly byte[] _buffer = new byte[8 + 2 * IntPtr.Size + 16];
        public bool Registered { get; private set; }
        private int _error;

        public string Status { get { return Registered ? "键盘探针已启用" : "键盘探针注册失败,错误码 " + _error; } }

        public void Register(IntPtr window)
        {
            Device device = new Device { Page = 1, Usage = 6, Flags = 0x100, Target = window };
            Registered = RegisterRawInputDevices(new Device[] { device }, 1, (uint)Marshal.SizeOf(typeof(Device)));
            _error = Registered ? 0 : Marshal.GetLastWin32Error();
        }

        public void Unregister()
        {
            if (!Registered) return;
            Device device = new Device { Page = 1, Usage = 6, Flags = 1, Target = IntPtr.Zero };
            RegisterRawInputDevices(new Device[] { device }, 1, (uint)Marshal.SizeOf(typeof(Device)));
            Registered = false;
        }

        /// <summary>这个 WM_INPUT 是不是键盘包;是的话顺便带出虚拟键码与是按下还是抬起。</summary>
        public bool Read(IntPtr input, out ushort vk, out bool down)
        {
            vk = 0;
            down = false;
            uint size = (uint)_buffer.Length;
            uint read = GetRawInputData(input, 0x10000003, _buffer, ref size, (uint)(8 + 2 * IntPtr.Size));
            if (read == uint.MaxValue) return false;
            return Decode(_buffer, (int)read, IntPtr.Size, out vk, out down);
        }

        internal static bool Decode(byte[] packet, int length, int pointerSize, out ushort vk, out bool down)
        {
            vk = 0;
            down = false;
            if (pointerSize != 4 && pointerSize != 8) return false;
            int header = 8 + 2 * pointerSize;
            if (packet == null || length < header + 8 || length > packet.Length) return false;
            if (BitConverter.ToUInt32(packet, 0) != 1) return false;   // RIM_TYPEKEYBOARD
            ushort flags = BitConverter.ToUInt16(packet, header + 2);
            vk = BitConverter.ToUInt16(packet, header + 6);
            down = (flags & 1) == 0;    // RI_KEY_BREAK = 1
            return true;
        }
    }
    internal sealed class DistanceSettings : ThemedDialog
    {
        private readonly NumericUpDown _dpi;
        private readonly NumericUpDown _centimeters;
        private readonly Label _result;
        private readonly Label _status;
        private readonly Button _save;
        private readonly Timer _timer;
        private bool _collecting;
        private double _counts;

        public DistanceSettings(RawMouseInput input)
        {
            Text = "鼠标 DPI / 距离校准";
            Font = new Font("Microsoft YaHei UI", 9f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(560, 465);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            KeyPreview = true;

            AddLabel("填写鼠标驱动中的当前 DPI，或使用尺子校准。\n切换鼠标或 DPI 档位后，请更新这里的设置；历史米数保持不变。", 18, 15, 520, 48);
            AddLabel("当前 DPI", 18, 76, 100, 25);
            _dpi = new NumericUpDown { Location = new Point(120, 72), Width = 150,
                Minimum = 1, Maximum = 1000000, DecimalPlaces = 2,
                Value = MouseDistance.ValidDpi(Store.MouseDpi) ? (decimal)Store.MouseDpi : 800 };
            Controls.Add(_dpi);
            AddLabel(MouseDistance.ValidDpi(Store.MouseDpi) ? "已设置" : "尚未设置，800 仅为输入示例", 285, 76, 255, 25);
            AddLabel("实测校准", 18, 118, 200, 25);
            AddLabel("测量长度（厘米）", 18, 153, 145, 25);
            _centimeters = new NumericUpDown { Location = new Point(170, 149), Width = 100,
                Minimum = 1, Maximum = 200, DecimalPlaces = 1, Value = 10 };
            Controls.Add(_centimeters);
            AddLabel("保持本窗口在前台，将鼠标对准尺子起点。\n按 F8 开始，沿直线单向移动指定长度，再按 F9 结束。\n无需移动光标点击按钮；校准期间暂停累计鼠标米数。", 18, 190, 520, 70);
            _result = AddLabel("等待校准。可直接填写 DPI 后保存。", 18, 269, 520, 42);
            _status = AddLabel(input.Status, 18, 315, 520, 28);
            AddLabel("原光标路程累计：" + Store.Total.MovePx.ToString("N0", CultureInfo.InvariantCulture) + " px（另存于 CSV）\n鼠标路程为估算值；分辨率与屏幕缩放不参与计算。", 18, 348, 520, 48);
            _save = new Button { Text = "保存 DPI", Location = new Point(325, 414), Size = new Size(100, 30) };
            _save.Click += delegate
            {
                Store.MouseDpi = (double)_dpi.Value;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(_save);
            Button cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel,
                Location = new Point(437, 414), Size = new Size(100, 30) };
            Controls.Add(cancel);
            CancelButton = cancel;
            _timer = new Timer { Interval = 250 };
            _timer.Tick += delegate
            {
                _status.Text = input.Status;
                if (_collecting) _result.Text = "正在校准：" + _counts.ToString("N0") + " 计数。到达终点后按 F9。";
            };
            _timer.Start();
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.F8)
                {
                    e.SuppressKeyPress = true;
                    if (_collecting) return;
                    if (!input.Registered) { _result.Text = input.Status; return; }
                    _counts = 0;
                    _collecting = true;
                    _save.Enabled = _dpi.Enabled = _centimeters.Enabled = false;
                    _result.Text = "正在校准，请沿尺子单向移动，到终点按 F9。";
                }
                else if (e.KeyCode == Keys.F9 && _collecting)
                {
                    e.SuppressKeyPress = true;
                    _collecting = false;
                    _save.Enabled = _dpi.Enabled = _centimeters.Enabled = true;
                    double dpi = MouseDistance.CalibratedDpi(_counts, (double)_centimeters.Value);
                    if (!MouseDistance.ValidDpi(dpi))
                    {
                        _result.Text = "校准无效：未收到有效移动或结果超出范围，请按 F8 重试。";
                        return;
                    }
                    _dpi.Value = (decimal)dpi;
                    _result.Text = "校准结果：" + dpi.ToString("0.##") + " DPI。点击保存后生效。";
                }
            };
            Deactivate += delegate
            {
                if (!_collecting) return;
                _collecting = false;
                _save.Enabled = _dpi.Enabled = _centimeters.Enabled = true;
                _result.Text = "校准已取消：窗口失去焦点。请重新对齐起点，按 F8 重试。";
            };
        }

        public bool Collect(double counts)
        {
            if (!_collecting) return false;
            _counts += counts;
            return true;
        }

        private Label AddLabel(string text, int x, int y, int width, int height)
        {
            Label label = new Label { Text = text, Location = new Point(x, y), Size = new Size(width, height) };
            Controls.Add(label);
            return label;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _timer != null) _timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
