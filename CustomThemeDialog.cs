// ============================================================================
//  键鼠统计 - 自定义壁纸皮肤配置浮层  CustomThemeDialog.cs
//
//  固定深色原生窗口:壁纸画廊、压暗滑杆、删除/导入、AI 提示词模板器。
//  所有缩略图在打开浮层时一次性读入内存,OnPaint 只绘制内存位图。
//  3.1: 按系统 DPI 缩放全部逻辑像素;压暗滑轨与预览框改为自绘深色控件。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KeyMouseStats
{
    internal sealed class CustomThemeDialog : Form
    {
        private static readonly Color ShellBg = Color.FromArgb(10, 14, 20);
        private static readonly Color ShellBg2 = Color.FromArgb(7, 10, 15);
        private static readonly Color PanelBg = Color.FromArgb(17, 23, 32);
        private static readonly Color RaisedBg = Color.FromArgb(24, 32, 43);
        private static readonly Color InputBg = Color.FromArgb(13, 19, 27);
        private static readonly Color LineColor = Color.FromArgb(43, 55, 70);
        private static readonly Color TextColor = Color.FromArgb(237, 242, 249);
        private static readonly Color MutedColor = Color.FromArgb(146, 161, 181);
        // 禁用态不再交给系统灰化:中性灰 #68768A + 深描边,保证深色底上仍清晰可辨。
        private static readonly Color DisabledText = Color.FromArgb(104, 118, 138);
        private static readonly Color DisabledBorder = Color.FromArgb(56, 68, 84);
        private static readonly Color DisabledFill = Color.FromArgb(18, 24, 33);

        /// <summary>测试用:强制逻辑缩放倍率,同时按系统 DPI 折算字体,模拟 150% 实机。</summary>
        internal static float DpiScaleOverrideForTests = 0f;

        private readonly float _uiScale;
        private readonly float _fontScale;
        private readonly SkinGallery _gallery = new SkinGallery();
        private readonly DimSlider _dim = new DimSlider();
        private readonly Panel _previewPanel = new Panel();
        private readonly ToolTip _toolTip = new ToolTip();
        private readonly Label _dimValue = new Label();
        private readonly Label _current = new Label();
        private readonly Label _status = new Label();
        private readonly TextBox _subject = new TextBox();
        private readonly TextBox _colors = new TextBox();
        private readonly TextBox _preview = new TextBox();
        private readonly Button _delete = new DarkButton();
        private readonly Button _openFolder = new DarkButton();
        private readonly Button _copyZh = new DarkButton();
        private readonly Button _copyEn = new DarkButton();
        private readonly Button _close = new DarkButton();
        private readonly Button[] _presetButtons = new Button[4];
        private readonly Button[] _scopeButtons = new Button[10];
        private readonly Button _restoreScope = new DarkButton();
        private readonly Button _modeWallpaper = new DarkButton();
        private readonly Button _modeIcon = new DarkButton();
        private readonly DarkSelect _iconSlot = new DarkSelect();
        private readonly DarkSelect _iconStyle = new DarkSelect();
        private readonly DarkSelect _iconMatte = new DarkSelect();
        private readonly Button _iconImport = new DarkButton();
        private readonly Button _iconReset = new DarkButton();
        private readonly Button _iconApplyAll = new DarkButton();
        private readonly Label _iconStatus = new Label();
        private readonly IconPreview _iconPreview = new IconPreview();
        private readonly Dictionary<string, Image> _thumbImages = new Dictionary<string, Image>(StringComparer.Ordinal);
        private int _presetIndex;
        private int _scopeIndex;
        private int _promptMode;
        private bool _syncingDim;
        private bool _syncingIcon;
        private bool _importing;

        public CustomThemeDialog()
        {
            float systemScale = DetectSystemScale();
            _uiScale = DpiScaleOverrideForTests > 0f
                ? LayoutFrame.Clamp(DpiScaleOverrideForTests, 1f, 2.5f) : systemScale;
            _fontScale = _uiScale > 0f && systemScale > 0f ? _uiScale / systemScale : 1f;

            Text = "自定义壁纸皮肤";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            Font = new Font("Microsoft YaHei UI", 9f * _fontScale);
            BackColor = ShellBg;
            ForeColor = TextColor;
            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

            // 880×790 逻辑尺寸:100% / 125% / 150% 下都能一屏放下,不再出现窗体级滚动条。
            Size desired = new Size(S(880), S(790));
            ClientSize = desired;
            ClientSize = FitToWorkingArea(desired);
            AutoScroll = false;
            AutoScrollMinSize = Size.Empty;

            Label title = new Label();
            title.Text = "自定义壁纸皮肤";
            title.Font = new Font("Microsoft YaHei UI", 13f * _fontScale, FontStyle.Bold);
            title.SetBounds(S(24), S(12), S(520), S(28));
            Label subtitle = new Label();
            subtitle.Text = "本地图片保存在数据目录 themes/；分页面壁纸、图标与徽章均在本地离线烘焙，绘制只读内存。";
            subtitle.ForeColor = MutedColor;
            subtitle.SetBounds(S(24), S(40), S(700), S(20));

            for (int i = 0; i < _scopeButtons.Length; i++)
            {
                int scope = i;
                Button button = new DarkButton();
                button.Text = i == 0 ? "全局" : CustomThemeStore.PageLabel(i - 1);
                button.SetBounds(S(24 + i * 46), S(62), S(44), S(22));
                button.Click += delegate { SetScope(scope); };
                _scopeButtons[i] = button;
            }
            _restoreScope.Text = "跟随全局";
            _restoreScope.SetBounds(S(488), S(62), S(96), S(22));
            _restoreScope.Click += delegate { RestoreScope(); };

            Label galleryTitle = new Label();
            galleryTitle.Text = "已保存的壁纸";
            galleryTitle.Font = new Font("Microsoft YaHei UI", 10f * _fontScale, FontStyle.Bold);
            galleryTitle.SetBounds(S(24), S(84), S(300), S(24));
            _gallery.UiScale = _uiScale;
            _gallery.SetBounds(S(24), S(108), S(560), S(198));

            _current.SetBounds(S(608), S(106), S(248), S(20));
            _current.ForeColor = MutedColor;
            _current.AutoEllipsis = true;
            Label dimTitle = new Label();
            dimTitle.Text = "背景压暗";
            dimTitle.Font = new Font("Microsoft YaHei UI", 10f * _fontScale, FontStyle.Bold);
            dimTitle.SetBounds(S(608), S(128), S(248), S(32));
            _dim.UiScale = _uiScale;
            _dim.SetBounds(S(608), S(160), S(248), S(36));
            _dimValue.SetBounds(S(608), S(196), S(248), S(18));
            _dimValue.ForeColor = TextColor;
            Label dimHint = new Label();
            dimHint.Text = "50% 透亮 · 85% 沉稳（默认 70%）";
            dimHint.ForeColor = MutedColor;
            dimHint.SetBounds(S(608), S(216), S(248), S(36));

            _delete.Text = "删除当前";
            _delete.SetBounds(S(608), S(256), S(248), S(30));
            _openFolder.Text = "打开壁纸文件夹";
            _openFolder.SetBounds(S(608), S(290), S(248), S(26));
            _toolTip.SetToolTip(_openFolder, "支持 PNG / JPG / BMP · 仅本地离线保存，不上传");

            Label promptTitle = new Label();
            promptTitle.Text = "AI 专属生图提示词";
            promptTitle.Font = new Font("Microsoft YaHei UI", 11f * _fontScale, FontStyle.Bold);
            promptTitle.SetBounds(S(24), S(520), S(500), S(28));
            _modeWallpaper.Text = "壁纸提示词";
            _modeWallpaper.SetBounds(S(600), S(518), S(120), S(28));
            _modeWallpaper.Click += delegate { SetPromptMode(0); };
            _modeIcon.Text = "图标徽章";
            _modeIcon.SetBounds(S(728), S(518), S(120), S(28));
            _modeIcon.Click += delegate { SetPromptMode(1); };

            for (int i = 0; i < SkinPrompt.Presets.Length && i < _presetButtons.Length; i++)
            {
                int index = i;
                Button button = new DarkButton();
                button.Text = SkinPrompt.Presets[i].Name;
                button.SetBounds(S(24 + i * 208), S(550), S(200), S(28));
                button.Click += delegate { ApplyPreset(index); };
                _presetButtons[i] = button;
            }

            Label subjectLabel = new Label();
            subjectLabel.Text = "画面主题";
            subjectLabel.SetBounds(S(24), S(586), S(62), S(20));
            _subject.SetBounds(S(88), S(582), S(424), S(28));
            Label colorLabel = new Label();
            colorLabel.Text = "主色调";
            colorLabel.SetBounds(S(24), S(620), S(62), S(20));
            _colors.SetBounds(S(88), S(616), S(424), S(28));
            Label editHint = new Label();
            editHint.Text = "直接修改主题与主色关键词，预览与两个复制按钮会同步更新。";
            editHint.ForeColor = MutedColor;
            editHint.SetBounds(S(524), S(582), S(332), S(62));

            _copyZh.Text = "复制中文 Prompt";
            _copyZh.SetBounds(S(24), S(650), S(240), S(30));
            _copyZh.Click += delegate { CopyPrompt(false); };
            _copyEn.Text = "复制英文 Prompt";
            _copyEn.SetBounds(S(272), S(650), S(240), S(30));
            _copyEn.Click += delegate { CopyPrompt(true); };
            _toolTip.SetToolTip(_copyZh, "适配即梦 / 豆包 / 可灵");
            _toolTip.SetToolTip(_copyEn, "适配 Midjourney / Flux");
            _status.SetBounds(S(524), S(654), S(332), S(22));
            _status.ForeColor = MutedColor;
            _status.AutoEllipsis = true;

            Label previewLabel = new Label();
            previewLabel.Text = "中文预览";
            previewLabel.ForeColor = MutedColor;
            previewLabel.SetBounds(S(24), S(684), S(200), S(18));

            _previewPanel.SetBounds(S(24), S(704), S(832), S(66));
            _previewPanel.BackColor = InputBg;
            _previewPanel.Padding = new Padding(S(8), S(6), S(8), S(6));
            _previewPanel.Paint += delegate(object sender, PaintEventArgs e)
            {
                Panel panel = (Panel)sender;
                if (panel.ClientSize.Width <= 0 || panel.ClientSize.Height <= 0) return;
                using (Pen border = new Pen(LineColor, 1f))
                    e.Graphics.DrawRectangle(border, 0, 0, panel.ClientSize.Width - 1, panel.ClientSize.Height - 1);
            };
            _preview.BorderStyle = BorderStyle.None;
            _preview.BackColor = InputBg;
            _preview.ForeColor = TextColor;
            _preview.Font = new Font("Microsoft YaHei UI", 8f * _fontScale);
            _preview.Multiline = true;
            _preview.ReadOnly = true;
            _preview.ScrollBars = ScrollBars.None;
            _preview.Dock = DockStyle.Fill;
            _previewPanel.Controls.Add(_preview);

            Label iconTitle = new Label();
            iconTitle.Text = "图标与徽章自由搭配";
            iconTitle.Font = new Font("Microsoft YaHei UI", 11f * _fontScale, FontStyle.Bold);
            iconTitle.SetBounds(S(24), S(308), S(500), S(28));
            Label iconSlotLabel = new Label();
            iconSlotLabel.Text = "槽位";
            iconSlotLabel.ForeColor = MutedColor;
            iconSlotLabel.SetBounds(S(24), S(348), S(52), S(20));
            for (int i = 0; i < IconPack.SlotLabels.Length; i++) _iconSlot.Items.Add(IconPack.SlotLabels[i]);
            _iconSlot.UiScale = _uiScale;
            _iconSlot.SetBounds(S(78), S(344), S(142), S(28));
            _iconSlot.SelectedIndexChanged += delegate { OnIconSlotChanged(); };
            Label iconStyleLabel = new Label();
            iconStyleLabel.Text = "风格";
            iconStyleLabel.ForeColor = MutedColor;
            iconStyleLabel.SetBounds(S(228), S(348), S(40), S(20));
            for (int i = 0; i < IconPack.Styles.Length; i++) _iconStyle.Items.Add(IconPack.Styles[i].Name);
            _iconStyle.Items.Add("自定义图片");
            _iconStyle.UiScale = _uiScale;
            _iconStyle.SetBounds(S(270), S(344), S(160), S(28));
            _iconStyle.SelectedIndexChanged += delegate { OnIconStyleChanged(); };
            _iconApplyAll.Text = "应用到全部槽位";
            _iconApplyAll.SetBounds(S(438), S(344), S(118), S(28));
            _iconApplyAll.Click += delegate { ApplyIconStyleToAll(); };
            Label iconMatteLabel = new Label();
            iconMatteLabel.Text = "导入抠图";
            iconMatteLabel.ForeColor = MutedColor;
            iconMatteLabel.SetBounds(S(24), S(386), S(54), S(20));
            for (int i = 0; i < IconPack.MatteLabels.Length; i++) _iconMatte.Items.Add(IconPack.MatteLabels[i]);
            _iconMatte.UiScale = _uiScale;
            _iconMatte.SetBounds(S(80), S(382), S(190), S(28));
            _iconImport.Text = "导入图标";
            _iconImport.SetBounds(S(278), S(382), S(112), S(28));
            _iconImport.Click += delegate { ChooseIconImport(); };
            _iconReset.Text = "恢复默认";
            _iconReset.SetBounds(S(398), S(382), S(112), S(28));
            _iconReset.Click += delegate { ResetIcon(); };
            _iconStatus.SetBounds(S(24), S(418), S(532), S(22));
            _iconStatus.ForeColor = MutedColor;
            _iconStatus.AutoEllipsis = true;
            Label iconHint = new Label();
            iconHint.Text = "每个槽位独立生效；自定义图标会离线抠底并缓存到 themes/icons/。";
            iconHint.ForeColor = MutedColor;
            iconHint.SetBounds(S(24), S(444), S(532), S(20));
            _iconPreview.SetBounds(S(576), S(340), S(280), S(176));
            _iconPreview.UiScale = _uiScale;

            _close.Text = "完成";
            _close.SetBounds(S(748), S(12), S(108), S(28));
            _close.DialogResult = DialogResult.OK;
            Label closeHint = new Label();
            closeHint.Text = "切换壁纸与图标会立即生效；关闭后主界面自动刷新。";
            closeHint.ForeColor = MutedColor;
            closeHint.Font = new Font("Microsoft YaHei UI", 8f * _fontScale);
            closeHint.SetBounds(S(24), S(774), S(700), S(14));

            Controls.AddRange(new Control[]
            {
                title, subtitle, galleryTitle, _gallery, _current, dimTitle, _dim, _dimValue, dimHint,
                _delete, _openFolder, promptTitle, _modeWallpaper, _modeIcon,
                _presetButtons[0], _presetButtons[1], _presetButtons[2], _presetButtons[3],
                subjectLabel, _subject, colorLabel, _colors, editHint,
                _copyZh, _copyEn, _status, previewLabel, _previewPanel,
                iconTitle, iconSlotLabel, _iconSlot, iconStyleLabel, _iconStyle, _iconApplyAll,
                iconMatteLabel, _iconMatte, _iconImport, _iconReset, _iconStatus, iconHint, _iconPreview,
                _close, closeHint
            });
            Controls.AddRange(_scopeButtons);
            Controls.Add(_restoreScope);
            AcceptButton = _close;
            CancelButton = _close;

            _subject.TextChanged += delegate { UpdatePromptPreview(); };
            _colors.TextChanged += delegate { UpdatePromptPreview(); };
            _dim.ValueChanged += delegate { OnDimChanged(); };
            _gallery.SelectionChanged += delegate { OnGallerySelected(); };
            _gallery.ImportRequested += delegate { ChooseImport(); };
            _delete.Click += delegate { DeleteCurrent(); };
            _openFolder.Click += delegate { OpenThemeFolder(); };

            ApplyDarkStyle(this);
            ApplyPreset(0);
            SetScope(0);
            SetPromptMode(0);
            _syncingIcon = true;
            _iconSlot.SelectedIndex = 0;
            _iconMatte.SelectedIndex = 0;
            _syncingIcon = false;
            RefreshIconEditor();
            RefreshGallery();
            UpdatePresetStyles();
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // 原生标题栏也压成深色(Windows 10 1809+);旧系统忽略返回值即可。
            try
            {
                int dark = 1;
                if (DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)) != 0)
                    DwmSetWindowAttribute(Handle, 19, ref dark, sizeof(int));
            }
            catch
            {
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (ClientRectangle.Width <= 0 || ClientRectangle.Height <= 0) return;
            using (LinearGradientBrush brush = new LinearGradientBrush(ClientRectangle, ShellBg, ShellBg2, 90f))
                e.Graphics.FillRectangle(brush, ClientRectangle);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _gallery.Thumbs.Clear();
            foreach (Image image in _thumbImages.Values) image.Dispose();
            _thumbImages.Clear();
            _toolTip.Dispose();
            base.OnFormClosed(e);
        }

        private int S(int value)
        {
            return (int)Math.Round(value * _uiScale);
        }

        private static float DetectSystemScale()
        {
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                    return LayoutFrame.Clamp(g.DpiX / 96f, 1f, 2.5f);
            }
            catch
            {
                return 1f;
            }
        }

        private Size FitToWorkingArea(Size desired)
        {
            if (DpiScaleOverrideForTests > 0f) return desired;
            try
            {
                Screen screen = Screen.PrimaryScreen;
                if (screen == null) return desired;
                Rectangle work = screen.WorkingArea;
                // ClientSize 已按 desired 设过一次,这里能拿到真实的标题栏/边框厚度;
                // 只夹客户区会让窗口底部被任务栏盖住。
                int nonClientW = Math.Max(0, Width - ClientSize.Width);
                int nonClientH = Math.Max(0, Height - ClientSize.Height);
                int width = Math.Min(desired.Width, Math.Max(S(720), work.Width - nonClientW - S(4)));
                int height = Math.Min(desired.Height, Math.Max(S(560), work.Height - nonClientH - S(4)));
                return new Size(width, height);
            }
            catch
            {
                return desired;
            }
        }

        private void ApplyDarkStyle(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                Label label = control as Label;
                if (label != null)
                {
                    label.BackColor = Color.Transparent;
                    if (label.ForeColor != MutedColor) label.ForeColor = TextColor;
                }
                Button button = control as Button;
                if (button != null)
                {
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderSize = 1;
                    button.FlatAppearance.BorderColor = LineColor;
                    button.BackColor = RaisedBg;
                    button.ForeColor = TextColor;
                    button.UseVisualStyleBackColor = false;
                    button.Cursor = Cursors.Hand;
                }
                TextBox textBox = control as TextBox;
                if (textBox != null)
                {
                    textBox.BackColor = InputBg;
                    textBox.ForeColor = TextColor;
                    textBox.BorderStyle = textBox == _preview ? BorderStyle.None : BorderStyle.FixedSingle;
                }
                DarkSelect select = control as DarkSelect;
                if (select != null) select.ForeColor = TextColor;
                ApplyDarkStyle(control);
            }
        }

        private static Color Accent
        {
            get
            {
                ArtTheme theme = CustomThemeStore.ActiveTheme;
                return theme == null ? Color.FromArgb(110, 168, 255) : theme.Accent;
            }
        }

        private void ApplyPreset(int index)
        {
            if (_promptMode == 1)
            {
                if (index < 0 || index >= SkinPrompt.IconPresets.Length) index = 0;
                _presetIndex = index;
                IconPromptPreset preset = SkinPrompt.IconPresets[index];
                _subject.Text = preset.SubjectZh;
                _colors.Text = preset.ColorsZh;
            }
            else
            {
                if (index < 0 || index >= SkinPrompt.Presets.Length) index = 0;
                _presetIndex = index;
                SkinPromptPreset preset = SkinPrompt.Presets[index];
                _subject.Text = preset.SubjectZh;
                _colors.Text = preset.ColorsZh;
            }
            UpdatePresetStyles();
            UpdatePromptPreview();
        }

        private void SetPromptMode(int mode)
        {
            _promptMode = mode == 1 ? 1 : 0;
            for (int i = 0; i < _presetButtons.Length; i++)
            {
                Button button = _presetButtons[i];
                if (button == null) continue;
                if (_promptMode == 1)
                {
                    button.Visible = i < SkinPrompt.IconPresets.Length;
                    if (button.Visible) button.Text = SkinPrompt.IconPresets[i].Name;
                }
                else
                {
                    button.Visible = true;
                    button.Text = SkinPrompt.Presets[i].Name;
                }
            }
            _modeWallpaper.BackColor = _promptMode == 0 ? ArtTheme.Mix(RaisedBg, Accent, 0.30) : RaisedBg;
            _modeIcon.BackColor = _promptMode == 1 ? ArtTheme.Mix(RaisedBg, Accent, 0.30) : RaisedBg;
            _modeWallpaper.FlatAppearance.BorderColor = _promptMode == 0 ? Accent : LineColor;
            _modeIcon.FlatAppearance.BorderColor = _promptMode == 1 ? Accent : LineColor;
            ApplyPreset(0);
        }

        private void UpdatePresetStyles()
        {
            for (int i = 0; i < _presetButtons.Length; i++)
            {
                Button button = _presetButtons[i];
                if (button == null) continue;
                bool visible = _promptMode == 0 || i < SkinPrompt.IconPresets.Length;
                button.Visible = visible;
                bool selected = visible && i == _presetIndex;
                button.BackColor = selected ? ArtTheme.Mix(RaisedBg, Accent, 0.30) : RaisedBg;
                button.FlatAppearance.BorderColor = selected ? Accent : LineColor;
            }
        }

        private void UpdatePromptPreview()
        {
            SkinPromptPair pair = _promptMode == 1
                ? SkinPrompt.BuildIcon(SkinPrompt.IconPresets[_presetIndex], _subject.Text.Trim(), _colors.Text.Trim())
                : SkinPrompt.Build(SkinPrompt.Presets[_presetIndex], _subject.Text.Trim(), _colors.Text.Trim());
            _preview.Text = pair.Chinese;
            _preview.SelectionStart = 0;
            _preview.SelectionLength = 0;
        }

        private void SetScope(int index)
        {
            _scopeIndex = Math.Max(0, Math.Min(9, index));
            UpdateScopeStyles();
            RefreshGallery();
        }

        private void UpdateScopeStyles()
        {
            for (int i = 0; i < _scopeButtons.Length; i++)
            {
                Button button = _scopeButtons[i];
                if (button == null) continue;
                bool selected = i == _scopeIndex;
                button.BackColor = selected ? ArtTheme.Mix(RaisedBg, Accent, 0.30) : RaisedBg;
                button.FlatAppearance.BorderColor = selected ? Accent : LineColor;
            }
            _restoreScope.Enabled = _scopeIndex > 0 && CustomThemeStore.IsPageBound(_scopeIndex - 1);
        }

        private void RestoreScope()
        {
            if (_scopeIndex <= 0) return;
            int page = _scopeIndex - 1;
            if (CustomThemeStore.BindPageSkin(page, null))
            {
                _status.Text = "已恢复跟随全局壁纸";
                RefreshGallery();
                if (Owner != null) Owner.Invalidate();
            }
        }

        private CustomThemeSkin CurrentScopeSkin
        {
            get
            {
                return _scopeIndex <= 0 ? CustomThemeStore.ActiveSkin : CustomThemeStore.EffectiveSkin(_scopeIndex - 1);
            }
        }

        private bool ScopeCanDim
        {
            get { return _scopeIndex <= 0 ? CustomThemeStore.ActiveSkin != null : CustomThemeStore.IsPageBound(_scopeIndex - 1); }
        }

        private void OnDimChanged()
        {
            if (_syncingDim) return;
            int value = CustomThemeStore.NormalizeDim(_dim.Value);
            if (value != _dim.Value)
            {
                _syncingDim = true;
                _dim.Value = value;
                _syncingDim = false;
            }
            _dimValue.Text = value.ToString() + "%";
            CustomThemeSkin scopeSkin = CurrentScopeSkin;
            if (scopeSkin != null) CustomThemeStore.SetDim(scopeSkin.Id, value);
            else CustomThemeStore.SetActiveDim(value);
            if (Owner != null) Owner.Invalidate();
        }

        private void OnGallerySelected()
        {
            string id = _gallery.SelectedId;
            if (string.IsNullOrEmpty(id)) return;
            if (_scopeIndex <= 0)
            {
                if (!CustomThemeStore.Activate(id)) return;
            }
            else
            {
                if (!CustomThemeStore.BindPageSkin(_scopeIndex - 1, id)) return;
            }
            RefreshGallery();
            _status.Text = _scopeIndex <= 0 ? "已切换全局壁纸" : "已为「" + CustomThemeStore.PageLabel(_scopeIndex - 1) + "」指定壁纸";
            if (Owner != null) Owner.Invalidate();
        }

        private void ChooseImport()
        {
            if (_importing) return;
            string path;
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "导入自定义壁纸";
                dialog.Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp";
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                path = dialog.FileName;
            }
            _importing = true;
            _gallery.Enabled = false;
            _status.Text = "正在解码并生成缓存…";
            CustomThemeStore.ImportAsync(path, delegate(CustomThemeSkin skin, string error)
            {
                try
                {
                    BeginInvoke(new MethodInvoker(delegate { OnImportCompleted(skin, error); }));
                }
                catch
                {
                }
            });
        }

        private void OnImportCompleted(CustomThemeSkin skin, string error)
        {
            if (IsDisposed) return;
            _importing = false;
            _gallery.Enabled = true;
            if (skin == null)
            {
                _status.Text = "导入失败：" + (string.IsNullOrEmpty(error) ? "未知错误" : error);
                return;
            }
            if (_scopeIndex <= 0) CustomThemeStore.Activate(skin.Id);
            else CustomThemeStore.BindPageSkin(_scopeIndex - 1, skin.Id);
            RefreshGallery();
            _status.Text = _scopeIndex <= 0 ? ("已导入并启用「" + skin.Name + "」")
                : ("已导入并绑定到「" + CustomThemeStore.PageLabel(_scopeIndex - 1) + "」");
            if (Owner != null) Owner.Invalidate();
        }

        private void DeleteCurrent()
        {
            int page = _scopeIndex - 1;
            string id = page < 0 ? Store.CustomThemeId : (CustomThemeStore.IsPageBound(page) ? CustomThemeStore.EffectiveSkinId(page) : null);
            if (string.IsNullOrEmpty(id))
            {
                _status.Text = page < 0 ? "当前没有启用自定义壁纸" : "当前页面跟随全局，没有独立壁纸可删";
                return;
            }
            CustomThemeSkin active = page < 0 ? CustomThemeStore.ActiveSkin : CustomThemeStore.EffectiveSkin(page);
            string name = active == null ? "当前壁纸" : active.Name;
            DialogResult confirm = ThemeMessage.Show(this,
                "删除「" + name + "」？\n原始图片、缩略图与背景缓存会一起移除。",
                "自定义皮肤", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;
            if (CustomThemeStore.Delete(id))
            {
                RefreshGallery();
                _status.Text = "已删除壁纸";
                if (Owner != null) Owner.Invalidate();
            }
        }

        private void OpenThemeFolder()
        {
            try
            {
                Directory.CreateDirectory(CustomThemeStore.RootDirectory);
                Process.Start("explorer.exe", CustomThemeStore.RootDirectory);
            }
            catch
            {
                _status.Text = "无法打开壁纸文件夹";
            }
        }

        private void CopyPrompt(bool english)
        {
            SkinPromptPair pair = _promptMode == 1
                ? SkinPrompt.BuildIcon(SkinPrompt.IconPresets[_presetIndex], _subject.Text.Trim(), _colors.Text.Trim())
                : SkinPrompt.Build(SkinPrompt.Presets[_presetIndex], _subject.Text.Trim(), _colors.Text.Trim());
            string text = english ? pair.English : pair.Chinese;
            try
            {
                Clipboard.SetText(text);
                _status.Text = english ? "已复制英文 Prompt" : "已复制中文 Prompt";
            }
            catch
            {
                _status.Text = "复制失败，请手动选择预览文本";
            }
        }

        private string CurrentIconSlot
        {
            get
            {
                int index = _iconSlot.SelectedIndex;
                if (index < 0 || index >= IconPack.SlotKeys.Length) index = 0;
                return IconPack.SlotKeys[index];
            }
        }

        private void OnIconSlotChanged()
        {
            if (_syncingIcon) return;
            RefreshIconEditor();
        }

        private void OnIconStyleChanged()
        {
            if (_syncingIcon) return;
            int index = _iconStyle.SelectedIndex;
            if (index < 0) return;
            if (index >= IconPack.Styles.Length)
            {
                _iconStatus.Text = "自定义图片请使用「导入图标」选择文件";
                RefreshIconEditor();
                return;
            }
            string slot = CurrentIconSlot;
            IconPack.SetStyle(slot, IconPack.Styles[index].Key);
            _iconStatus.Text = "已将「" + IconPack.SlotLabels[_iconSlot.SelectedIndex] + "」切换为 " + IconPack.Styles[index].Name;
            _iconPreview.Invalidate();
            if (Owner != null) Owner.Invalidate();
        }

        private void ApplyIconStyleToAll()
        {
            int index = _iconStyle.SelectedIndex;
            if (index < 0) return;
            if (index >= IconPack.Styles.Length)
            {
                _iconStatus.Text = "自定义图片不能批量套用，请逐槽位导入";
                return;
            }
            IconPack.ApplyStyleToAll(IconPack.Styles[index].Key);
            _iconStatus.Text = "四个槽位已统一为 " + IconPack.Styles[index].Name;
            _iconPreview.Invalidate();
            if (Owner != null) Owner.Invalidate();
        }

        private void RefreshIconEditor()
        {
            _syncingIcon = true;
            string slot = CurrentIconSlot;
            string style = IconPack.StyleKey(slot);
            int styleIndex = -1;
            for (int i = 0; i < IconPack.Styles.Length; i++)
                if (string.Equals(IconPack.Styles[i].Key, style, StringComparison.Ordinal)) { styleIndex = i; break; }
            if (styleIndex < 0) styleIndex = IconPack.Styles.Length;   // 自定义图片
            _iconStyle.SelectedIndex = styleIndex;
            int matte = IconPack.MatteMode(slot);
            _iconMatte.SelectedIndex = Math.Max(0, Math.Min(2, matte));
            _syncingIcon = false;
            _iconPreview.Invalidate();
        }

        private void ChooseIconImport()
        {
            if (_importing) return;
            string path;
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "导入图标 / 徽章";
                dialog.Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp";
                dialog.CheckFileExists = true;
                dialog.Multiselect = false;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                path = dialog.FileName;
            }
            _importing = true;
            _iconImport.Enabled = false;
            _iconStatus.Text = "正在离线抠底并烘焙图标…";
            IconMatte matte = (IconMatte)Math.Max(0, Math.Min(2, _iconMatte.SelectedIndex));
            string slot = CurrentIconSlot;
            IconPack.ImportAsync(slot, path, matte, delegate(bool ok, string error)
            {
                try
                {
                    BeginInvoke(new MethodInvoker(delegate { OnIconImportCompleted(ok, error); }));
                }
                catch
                {
                }
            });
        }

        private void OnIconImportCompleted(bool ok, string error)
        {
            if (IsDisposed) return;
            _importing = false;
            _iconImport.Enabled = true;
            if (!ok)
            {
                _iconStatus.Text = "图标导入失败：" + (string.IsNullOrEmpty(error) ? "未知错误" : error);
                return;
            }
            _iconStatus.Text = "已导入并应用到「" + IconPack.SlotLabels[_iconSlot.SelectedIndex] + "」";
            RefreshIconEditor();
            if (Owner != null) Owner.Invalidate();
        }

        private void ResetIcon()
        {
            string slot = CurrentIconSlot;
            IconPack.ClearSlot(slot);
            _iconStatus.Text = "已恢复「" + IconPack.SlotLabels[_iconSlot.SelectedIndex] + "」默认样式";
            RefreshIconEditor();
            if (Owner != null) Owner.Invalidate();
        }

        private void RefreshGallery()
        {
            List<CustomThemeSkin> skins = CustomThemeStore.Skins;
            _gallery.Skins.Clear();
            _gallery.Skins.AddRange(skins);
            int page = _scopeIndex - 1;
            bool bound = page >= 0 && CustomThemeStore.IsPageBound(page);
            string effectiveId = page < 0 ? (Store.CustomThemeId ?? "") : (CustomThemeStore.EffectiveSkinId(page) ?? "");
            CustomThemeSkin active = page < 0 ? CustomThemeStore.ActiveSkin : CustomThemeStore.EffectiveSkin(page);
            _gallery.ActiveId = effectiveId;
            _gallery.SelectedId = effectiveId;

            foreach (Image image in _thumbImages.Values) image.Dispose();
            _thumbImages.Clear();
            _gallery.Thumbs.Clear();
            for (int i = 0; i < skins.Count; i++)
            {
                CustomThemeSkin skin = skins[i];
                string path = skin.ThumbPath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) path = skin.CachePath;
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                try
                {
                    using (Image source = Image.FromFile(path))
                    {
                        Bitmap copy = new Bitmap(source);
                        _thumbImages[skin.Id] = copy;
                        _gallery.Thumbs[skin.Id] = copy;
                    }
                }
                catch
                {
                }
            }
            _gallery.RefreshLayout();

            if (page < 0)
                _current.Text = active == null ? "全局：未启用自定义壁纸" : "全局：" + active.Name;
            else if (bound)
                _current.Text = CustomThemeStore.PageLabel(page) + "：" + (active == null ? "独立壁纸" : active.Name) + "（独立）";
            else
                _current.Text = CustomThemeStore.PageLabel(page) + "：跟随全局" + (active == null ? "" : " · " + active.Name);
            _delete.Enabled = page < 0 ? active != null : bound && active != null;

            _syncingDim = true;
            if (!ScopeCanDim || active == null)
            {
                _dim.Enabled = false;
                _dim.Value = CustomThemeStore.DefaultDim;
                _dimValue.Text = "--";
            }
            else
            {
                _dim.Enabled = true;
                _dim.Value = Math.Max(CustomThemeStore.MinimumDim, Math.Min(CustomThemeStore.MaximumDim, active.Dim));
                _dimValue.Text = active.Dim.ToString() + "%";
            }
            _syncingDim = false;
            UpdateScopeStyles();
            UpdatePresetStyles();
        }

        /// <summary>四槽位 2×2 实时预览。只读 IconPack 内存位图 / 矢量,不读写文件。</summary>
        private sealed class IconPreview : Control
        {
            internal float UiScale = 1f;
            internal IconPreview()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = PanelBg;
            }

            private int S(int value)
            {
                return (int)Math.Round(value * UiScale);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush bg = new SolidBrush(PanelBg)) g.FillRectangle(bg, ClientRectangle);
                using (Pen edge = new Pen(LineColor)) g.DrawRectangle(edge, 0, 0, Width - 1, Height - 1);
                TextRenderer.DrawText(g, "实时预览 · 4 槽位", Font,
                    new Rectangle(S(12), S(6), Width - S(24), S(20)), MutedColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                for (int i = 0; i < IconPack.SlotKeys.Length; i++)
                {
                    int col = i % 2, row = i / 2;
                    RectangleF cell = new RectangleF(S(8 + col * 134), S(30 + row * 68), S(130), S(62));
                    using (SolidBrush cellBg = new SolidBrush(InputBg))
                        g.FillRectangle(cellBg, cell);
                    using (Pen cellEdge = new Pen(LineColor))
                        g.DrawRectangle(cellEdge, cell.X, cell.Y, cell.Width - 1, cell.Height - 1);
                    RectangleF rect = new RectangleF(cell.X + S(7), cell.Y + S(13), S(34), S(34));
                    if (!IconPack.Draw(g, IconPack.SlotKeys[i], i, rect, Accent, TextColor))
                    {
                        using (Pen ring = new Pen(Accent, 1.4f))
                        using (SolidBrush dot = new SolidBrush(Accent))
                        {
                            g.DrawEllipse(ring, rect.X + 3, rect.Y + 3, rect.Width - 6, rect.Height - 6);
                            g.FillEllipse(dot, rect.X + rect.Width / 2f - 2, rect.Y + rect.Height / 2f - 2, 4, 4);
                        }
                    }
                    Rectangle label = new Rectangle((int)cell.X + S(44), (int)cell.Y + S(9),
                        (int)cell.Width - S(48), S(19));
                    TextRenderer.DrawText(g, IconPack.SlotLabels[i], Font, label, TextColor,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                    Rectangle style = new Rectangle(label.X, label.Y + S(20), label.Width, S(18));
                    TextRenderer.DrawText(g, ShortStyle(IconPack.StyleKeyCached(IconPack.SlotKeys[i])), Font, style, MutedColor,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                }
            }

            private static string ShortStyle(string key)
            {
                if (string.Equals(key, "cyber", StringComparison.Ordinal)) return "赛博仪表";
                if (string.Equals(key, "seal", StringComparison.Ordinal)) return "古风印章";
                if (string.Equals(key, "pixel", StringComparison.Ordinal)) return "像素复古";
                if (string.Equals(key, "compass", StringComparison.Ordinal)) return "星轨罗盘";
                if (string.Equals(key, "custom", StringComparison.Ordinal)) return "自定义图片";
                return "默认风格";
            }
        }

        /// <summary>深色自绘按钮:禁用态使用 #68768A,不再依赖系统灰化。</summary>
        private sealed class DarkButton : Button
        {
            private bool _hover;
            private bool _pressed;

            internal DarkButton()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 1;
                FlatAppearance.MouseOverBackColor = Color.Empty;
                FlatAppearance.MouseDownBackColor = Color.Empty;
                UseVisualStyleBackColor = false;
                Cursor = Cursors.Hand;
            }

            private Color Backdrop()
            {
                Control parent = Parent;
                if (parent == null || parent.ClientSize.Height <= 0) return ShellBg;
                double position = (Top + Height / 2.0) / parent.ClientSize.Height;
                return ArtTheme.Mix(ShellBg, ShellBg2, position);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                Color backdrop = Backdrop();
                g.Clear(backdrop);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Color fill = BackColor;
                Color border = FlatAppearance.BorderColor;
                if (!Enabled)
                {
                    fill = DisabledFill;
                    border = DisabledBorder;
                }
                else if (_pressed)
                {
                    fill = FlatAppearance.MouseDownBackColor.IsEmpty
                        ? ArtTheme.Mix(BackColor, Accent, 0.36) : FlatAppearance.MouseDownBackColor;
                    border = Accent;
                }
                else if (_hover)
                {
                    fill = FlatAppearance.MouseOverBackColor.IsEmpty
                        ? ArtTheme.Mix(BackColor, Accent, 0.22) : FlatAppearance.MouseOverBackColor;
                }
                Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
                using (GraphicsPath path = Rounded(rect, 6f))
                {
                    using (SolidBrush brush = new SolidBrush(fill)) g.FillPath(brush, path);
                    using (Pen pen = new Pen(border, 1f)) g.DrawPath(pen, path);
                }
                Color text = Enabled ? ForeColor : DisabledText;
                TextRenderer.DrawText(g, Text, Font, new Rectangle(2, 0, Width - 4, Height), text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if (Focused && Enabled)
                    using (Pen focus = new Pen(Color.FromArgb(110, TextColor), 1f))
                    {
                        focus.DashStyle = DashStyle.Dot;
                        g.DrawRectangle(focus, 3, 3, Width - 7, Height - 7);
                    }
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                base.OnMouseEnter(e);
                _hover = true;
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                _hover = false;
                _pressed = false;
                Invalidate();
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                _pressed = true;
                Invalidate();
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                _pressed = false;
                Invalidate();
            }

            protected override void OnEnabledChanged(EventArgs e)
            {
                base.OnEnabledChanged(e);
                Cursor = Enabled ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }

            private static GraphicsPath Rounded(Rectangle rect, float radius)
            {
                GraphicsPath path = new GraphicsPath();
                float d = Math.Max(2f, radius * 2f);
                if (d > rect.Width) d = rect.Width;
                if (d > rect.Height) d = rect.Height;
                path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                return path;
            }
        }

        /// <summary>紧凑深色下拉选择器。完全自绘,不再出现原生 ComboBox 的白色按钮方块。</summary>
        private sealed class DarkSelect : Control
        {
            internal readonly List<string> Items = new List<string>();
            internal event EventHandler SelectedIndexChanged;
            internal float UiScale = 1f;
            private int _selectedIndex = -1;
            private bool _hover;
            private bool _open;
            private bool _disposing;
            private ContextMenuStrip _menu;

            internal DarkSelect()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                BackColor = InputBg;
                ForeColor = TextColor;
                TabStop = true;
                Cursor = Cursors.Hand;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && _menu != null)
                {
                    // 绝不能在 ContextMenuStrip.Closed 里 Dispose 自己:
                    // ToolStrip 关闭过程中还会访问句柄,原生会抛 ObjectDisposedException。
                    _disposing = true;
                    _menu.Dispose();
                    _menu = null;
                }
                base.Dispose(disposing);
            }

            internal int SelectedIndex
            {
                get { return _selectedIndex; }
                set
                {
                    int next = value;
                    if (next < -1) next = -1;
                    if (next >= Items.Count) next = Items.Count - 1;
                    if (next == _selectedIndex) return;
                    _selectedIndex = next;
                    Invalidate();
                    if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
                }
            }

            internal string SelectedText
            {
                get { return _selectedIndex >= 0 && _selectedIndex < Items.Count ? Items[_selectedIndex] : ""; }
            }

            private int S(int value)
            {
                return (int)Math.Round(value * UiScale);
            }

            private Color Backdrop()
            {
                Control parent = Parent;
                if (parent == null || parent.ClientSize.Height <= 0) return ShellBg;
                double position = (Top + Height / 2.0) / parent.ClientSize.Height;
                return ArtTheme.Mix(ShellBg, ShellBg2, position);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(Backdrop());
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
                using (GraphicsPath path = Rounded(rect, S(6)))
                {
                    using (SolidBrush brush = new SolidBrush(Enabled ? InputBg : DisabledFill)) g.FillPath(brush, path);
                    Color border = !Enabled ? DisabledBorder : _open || Focused ? Accent : _hover ? MutedColor : LineColor;
                    using (Pen pen = new Pen(border, 1f)) g.DrawPath(pen, path);
                }
                Rectangle textRect = new Rectangle(S(9), 0, Math.Max(1, Width - S(30)), Height);
                TextRenderer.DrawText(g, SelectedText, Font, textRect, Enabled ? ForeColor : DisabledText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                int cx = Width - S(13), cy = Height / 2;
                using (Pen arrow = new Pen(Enabled ? MutedColor : DisabledBorder, Math.Max(1f, 1.4f * UiScale)))
                {
                    arrow.StartCap = arrow.EndCap = LineCap.Round;
                    g.DrawLine(arrow, cx - S(3), cy - S(2), cx, cy + S(2));
                    g.DrawLine(arrow, cx, cy + S(2), cx + S(3), cy - S(2));
                }
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                base.OnMouseEnter(e);
                _hover = true;
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                _hover = false;
                Invalidate();
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (!Enabled || e.Button != MouseButtons.Left) return;
                Focus();
                ShowMenu();
            }

            protected override bool IsInputKey(Keys keyData)
            {
                if (keyData == Keys.Space || keyData == Keys.Enter || keyData == Keys.Up || keyData == Keys.Down) return true;
                return base.IsInputKey(keyData);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (!Enabled) return;
                if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
                {
                    ShowMenu();
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Down)
                {
                    SelectedIndex = Math.Min(Items.Count - 1, _selectedIndex + 1);
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Up)
                {
                    SelectedIndex = Math.Max(0, _selectedIndex - 1);
                    e.Handled = true;
                }
            }

            private void ShowMenu()
            {
                if (Items.Count == 0 || IsDisposed) return;
                if (_menu != null && _menu.Visible)
                {
                    _menu.Close();
                    return;
                }
                if (_open && _menu != null)
                {
                    _menu.Close();
                    return;
                }
                if (_menu == null)
                {
                    _menu = new ContextMenuStrip();
                    _menu.Renderer = new DarkMenuRenderer();
                    _menu.BackColor = InputBg;
                    _menu.ForeColor = TextColor;
                    _menu.ShowImageMargin = false;
                    _menu.ShowCheckMargin = false;
                    _menu.AutoSize = false;
                    _menu.Closed += delegate
                    {
                        _open = false;
                        if (!IsDisposed && !_disposing) Invalidate();
                    };
                }
                _menu.Font = Font;
                _menu.Width = Math.Max(Width, S(80));
                int itemHeight = S(26);
                _menu.Height = Math.Min(Items.Count, 7) * itemHeight + 4;
                _menu.Items.Clear();
                for (int i = 0; i < Items.Count; i++)
                {
                    int index = i;
                    ToolStripMenuItem item = new ToolStripMenuItem(Items[i]);
                    item.Tag = index;
                    item.AutoSize = false;
                    item.Width = _menu.Width - 2;
                    item.Height = itemHeight;
                    item.ForeColor = TextColor;
                    item.BackColor = InputBg;
                    item.Font = Font;
                    item.Click += delegate
                    {
                        if (!IsDisposed && !_disposing) SelectedIndex = index;
                    };
                    _menu.Items.Add(item);
                }
                _open = true;
                Invalidate();
                _menu.Show(this, new Point(0, Height + 2));
            }

            protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
            protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

            private static GraphicsPath Rounded(Rectangle rect, float radius)
            {
                GraphicsPath path = new GraphicsPath();
                float d = Math.Max(2f, radius * 2f);
                if (d > rect.Width) d = rect.Width;
                if (d > rect.Height) d = rect.Height;
                path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                return path;
            }
        }

        private sealed class DarkMenuRenderer : ToolStripRenderer
        {
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                using (SolidBrush brush = new SolidBrush(InputBg)) e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                using (Pen pen = new Pen(LineColor, 1f))
                    e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Selected) return;
                using (SolidBrush brush = new SolidBrush(ArtTheme.Mix(RaisedBg, Accent, 0.24)))
                    e.Graphics.FillRectangle(brush, new Rectangle(1, 0, e.Item.Width - 2, e.Item.Height));
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Selected ? TextColor : MutedColor;
                base.OnRenderItemText(e);
            }
        }

        /// <summary>自绘深色细圆角滑轨。替代原生 TrackBar 的浅灰底与白边。</summary>
        private sealed class DimSlider : Control
        {
            internal event EventHandler ValueChanged;
            internal float UiScale = 1f;
            internal int Minimum = CustomThemeStore.MinimumDim;
            internal int Maximum = CustomThemeStore.MaximumDim;
            internal int Step = 5;
            private int _value = CustomThemeStore.DefaultDim;

            internal DimSlider()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                BackColor = ShellBg;
                ForeColor = TextColor;
                TabStop = true;
                Cursor = Cursors.Hand;
            }

            internal int Value
            {
                get { return _value; }
                set
                {
                    int next = CustomThemeStore.NormalizeDim(value);
                    if (next < Minimum) next = Minimum;
                    if (next > Maximum) next = Maximum;
                    if (next == _value) return;
                    _value = next;
                    Invalidate();
                    if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
                }
            }

            private int S(int value)
            {
                return (int)Math.Round(value * UiScale);
            }

            private Color Backdrop()
            {
                Control parent = Parent;
                if (parent == null || parent.ClientSize.Height <= 0) return ShellBg;
                double position = (Top + Height / 2.0) / parent.ClientSize.Height;
                return ArtTheme.Mix(ShellBg, ShellBg2, position);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Color backdrop = Backdrop();
                e.Graphics.Clear(backdrop);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                int pad = Math.Max(8, S(10));
                int trackHeight = Math.Max(3, S(4));
                int centerY = Height / 2;
                Rectangle track = new Rectangle(pad, centerY - trackHeight / 2, Math.Max(1, Width - pad * 2), trackHeight);
                using (GraphicsPath path = Rounded(track, trackHeight / 2f))
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(58, 72, 90)))
                    e.Graphics.FillPath(brush, path);

                double fraction = (Value - Minimum) / (double)(Maximum - Minimum);
                if (fraction < 0) fraction = 0;
                if (fraction > 1) fraction = 1;
                int fillWidth = (int)Math.Round(track.Width * fraction);
                if (fillWidth > 0)
                {
                    Rectangle fill = new Rectangle(track.X, track.Y, fillWidth, track.Height);
                    using (GraphicsPath path = Rounded(fill, track.Height / 2f))
                    using (SolidBrush brush = new SolidBrush(Enabled ? Accent : MutedColor))
                        e.Graphics.FillPath(brush, path);
                }

                // 50 / 55 / ... / 85 的细刻度,让步进在深色滑轨上仍然可读。
                using (SolidBrush tick = new SolidBrush(Color.FromArgb(70, 88, 108)))
                {
                    for (int value = Minimum; value <= Maximum; value += Step)
                    {
                        double position = (value - Minimum) / (double)(Maximum - Minimum);
                        int x = track.X + (int)Math.Round(track.Width * position);
                        int y = track.Bottom + Math.Max(3, S(4));
                        e.Graphics.FillRectangle(tick, x, y, Math.Max(1, S(1)), Math.Max(2, S(2)));
                    }
                }

                int radius = Math.Max(5, S(7));
                int thumbX = track.X + (int)Math.Round(track.Width * fraction);
                using (SolidBrush thumb = new SolidBrush(Enabled ? Accent : MutedColor))
                    e.Graphics.FillEllipse(thumb, thumbX - radius, centerY - radius, radius * 2, radius * 2);
                using (Pen edge = new Pen(backdrop, Math.Max(1f, S(2))))
                    e.Graphics.DrawEllipse(edge, thumbX - radius, centerY - radius, radius * 2, radius * 2);
                if (Focused)
                    using (Pen focus = new Pen(Color.FromArgb(120, TextColor), 1f))
                    {
                        focus.DashStyle = DashStyle.Dot;
                        e.Graphics.DrawRectangle(focus, 0, 0, Width - 1, Height - 1);
                    }
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (!Enabled || e.Button != MouseButtons.Left) return;
                Focus();
                Capture = true;
                SetFromX(e.X);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (Capture && (e.Button & MouseButtons.Left) != 0) SetFromX(e.X);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                Capture = false;
            }

            protected override bool IsInputKey(Keys keyData)
            {
                if (keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Up
                    || keyData == Keys.Down || keyData == Keys.Home || keyData == Keys.End) return true;
                return base.IsInputKey(keyData);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (!Enabled) return;
                if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) { Value = Value - Step; e.Handled = true; }
                else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) { Value = Value + Step; e.Handled = true; }
                else if (e.KeyCode == Keys.Home) { Value = Minimum; e.Handled = true; }
                else if (e.KeyCode == Keys.End) { Value = Maximum; e.Handled = true; }
            }

            protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
            protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

            private void SetFromX(int x)
            {
                int pad = Math.Max(8, S(10));
                int width = Math.Max(1, Width - pad * 2);
                double fraction = (x - pad) / (double)width;
                if (fraction < 0) fraction = 0;
                if (fraction > 1) fraction = 1;
                Value = Minimum + (int)Math.Round(fraction * (Maximum - Minimum));
            }

            private static GraphicsPath Rounded(Rectangle rect, float radius)
            {
                GraphicsPath path = new GraphicsPath();
                float d = radius * 2;
                path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                return path;
            }
        }

        /// <summary>缩略图画廊:3 列自绘卡片,末尾是「导入图片」虚线卡。</summary>
        private sealed class SkinGallery : ScrollableControl
        {
            internal readonly List<CustomThemeSkin> Skins = new List<CustomThemeSkin>();
            internal readonly Dictionary<string, Image> Thumbs = new Dictionary<string, Image>(StringComparer.Ordinal);
            internal event EventHandler SelectionChanged;
            internal event EventHandler ImportRequested;
            internal float UiScale = 1f;
            internal string ActiveId;
            private string _selectedId = "";
            private int _hover = -1;

            internal string SelectedId
            {
                get { return _selectedId; }
                set { _selectedId = value ?? ""; }
            }

            internal SkinGallery()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                AutoScroll = true;
                BackColor = ShellBg;
                ForeColor = TextColor;
            }

            private int S(int value)
            {
                return (int)Math.Round(value * UiScale);
            }

            private float Sf(float value)
            {
                return value * UiScale;
            }

            internal void RefreshLayout()
            {
                int rows = (Skins.Count + 1 + 2) / 3;
                // 2 行恰好落在 200 px 画廊内,不再触发控件级纵向滚动条。
                AutoScrollMinSize = new Size(0, rows * S(98));
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                PaintBackdrop(e.Graphics);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                int scroll = -AutoScrollPosition.Y;
                int slots = Skins.Count + 1;
                for (int i = 0; i < slots; i++)
                {
                    int col = i % 3;
                    int row = i / 3;
                    RectangleF card = new RectangleF(S(8) + col * S(176), S(8) + row * S(98) - scroll, S(168), S(90));
                    if (card.Bottom < 0 || card.Top > Height) continue;
                    bool import = i == Skins.Count;
                    bool active = !import && string.Equals(Skins[i].Id, ActiveId, StringComparison.Ordinal);
                    bool selected = !import && string.Equals(Skins[i].Id, _selectedId, StringComparison.Ordinal);
                    DrawCard(e.Graphics, card, import ? null : Skins[i], active, selected, i == _hover);
                }
            }

            private void DrawCard(Graphics g, RectangleF card, CustomThemeSkin skin, bool active, bool selected, bool hover)
            {
                using (GraphicsPath path = Rounded(card, Sf(10f)))
                {
                    Color fill = active ? ArtTheme.Mix(RaisedBg, Accent, 0.18) : PanelBg;
                    if (hover && !active) fill = ArtTheme.Mix(PanelBg, TextColor, 0.06);
                    using (SolidBrush brush = new SolidBrush(fill)) g.FillPath(brush, path);
                    Color border = active || selected ? Accent : hover ? MutedColor : LineColor;
                    using (Pen pen = new Pen(border, active ? Math.Max(2f, Sf(2f)) : Math.Max(1f, Sf(1f))))
                        g.DrawPath(pen, path);
                }

                if (skin == null)
                {
                    RectangleF inner = RectangleF.Inflate(card, -Sf(6f), -Sf(6f));
                    using (GraphicsPath path = Rounded(inner, Sf(8f)))
                    using (Pen pen = new Pen(Color.FromArgb(120, Accent), Math.Max(1f, Sf(1f))))
                    {
                        pen.DashStyle = DashStyle.Dash;
                        g.DrawPath(pen, path);
                    }
                    float cx = card.X + card.Width / 2f;
                    float cy = card.Y + card.Height / 2f - Sf(8f);
                    using (Pen pen = new Pen(Accent, Math.Max(1.4f, Sf(1.8f))))
                    {
                        g.DrawLine(pen, cx - Sf(10f), cy, cx + Sf(10f), cy);
                        g.DrawLine(pen, cx, cy - Sf(10f), cx, cy + Sf(10f));
                    }
                    Rectangle text = Rectangle.Round(new RectangleF(card.X, cy + Sf(18f), card.Width, Sf(20f)));
                    TextRenderer.DrawText(g, "导入图片", Font, text, MutedColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    return;
                }

                Rectangle thumbRect = Rectangle.Round(new RectangleF(card.X + Sf(6f), card.Y + Sf(6f),
                    card.Width - Sf(12f), Sf(58f)));
                Image image;
                if (Thumbs.TryGetValue(skin.Id, out image) && image != null)
                {
                    // Cover 裁切:短卡片也不把 16:9 缩略图压扁。
                    double cover = Math.Max(thumbRect.Width / (double)image.Width, thumbRect.Height / (double)image.Height);
                    float srcW = (float)(thumbRect.Width / cover);
                    float srcH = (float)(thumbRect.Height / cover);
                    float srcX = (image.Width - srcW) / 2f;
                    float srcY = (image.Height - srcH) / 2f;
                    g.DrawImage(image, thumbRect, srcX, srcY, srcW, srcH, GraphicsUnit.Pixel);
                }
                else
                {
                    using (SolidBrush brush = new SolidBrush(InputBg)) g.FillRectangle(brush, thumbRect);
                    TextRenderer.DrawText(g, "缩略图不可用", Font, thumbRect, MutedColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
                Rectangle nameRect = Rectangle.Round(new RectangleF(card.X + Sf(8f), card.Y + Sf(66f),
                    card.Width - Sf(16f), Sf(18f)));
                TextRenderer.DrawText(g, ReadableName(skin), Font, nameRect, active ? Accent : TextColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if (active)
                {
                    using (SolidBrush brush = new SolidBrush(Accent))
                        g.FillEllipse(brush, card.Right - Sf(15f), card.Y + Sf(12f), Sf(6f), Sf(6f));
                }
            }

            /// <summary>卡片优先展示可读文件名;旧配置里残留的机器 ID 回退成中性名称。</summary>
            private static string ReadableName(CustomThemeSkin skin)
            {
                string name = skin.Name;
                if (string.IsNullOrEmpty(name)) return "自定义壁纸";
                if (string.Equals(name, skin.Id, StringComparison.Ordinal)) return "自定义壁纸";
                int machines = 0;
                bool machineLike = true;
                for (int i = 0; i < name.Length; i++)
                {
                    char c = name[i];
                    if (c >= '0' && c <= '9') { machines++; continue; }
                    if ((c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F') || c == '-') continue;
                    machineLike = false;
                    break;
                }
                if (machineLike && machines >= 10) return "自定义壁纸";
                return name;
            }

            private void PaintBackdrop(Graphics g)
            {
                Control parent = Parent;
                if (parent == null || parent.ClientSize.Height <= 0)
                {
                    g.Clear(ShellBg);
                    return;
                }
                double top = Top / (double)parent.ClientSize.Height;
                double bottom = (Top + Height) / (double)parent.ClientSize.Height;
                Color first = ArtTheme.Mix(ShellBg, ShellBg2, top);
                Color last = ArtTheme.Mix(ShellBg, ShellBg2, bottom);
                using (LinearGradientBrush brush = new LinearGradientBrush(ClientRectangle, first, last, 90f))
                    g.FillRectangle(brush, ClientRectangle);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int index = IndexAt(e.X, e.Y);
                if (index != _hover)
                {
                    _hover = index;
                    Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
                    Invalidate();
                }
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                if (_hover != -1) { _hover = -1; Cursor = Cursors.Default; Invalidate(); }
            }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                base.OnMouseClick(e);
                if (e.Button != MouseButtons.Left) return;
                int index = IndexAt(e.X, e.Y);
                if (index < 0) return;
                if (index == Skins.Count)
                {
                    if (ImportRequested != null) ImportRequested(this, EventArgs.Empty);
                    return;
                }
                _selectedId = Skins[index].Id;
                Invalidate();
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            }

            private int IndexAt(int clientX, int clientY)
            {
                int scroll = -AutoScrollPosition.Y;
                int logicalY = clientY + scroll;
                int col = (clientX - S(8)) / Math.Max(1, S(176));
                int row = (logicalY - S(8)) / Math.Max(1, S(98));
                if (col < 0 || col > 2 || row < 0) return -1;
                int localX = clientX - (S(8) + col * S(176));
                int localY = logicalY - (S(8) + row * S(98));
                if (localX < 0 || localX > S(168) || localY < 0 || localY > S(90)) return -1;
                int index = row * 3 + col;
                if (index > Skins.Count) return -1;
                return index;
            }

            private static GraphicsPath Rounded(RectangleF rect, float radius)
            {
                GraphicsPath path = new GraphicsPath();
                float d = radius * 2;
                path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                return path;
            }
        }
    }
}
