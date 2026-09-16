namespace DlcvCSharpCppTest
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                {
                    ReleaseSession();
                    if (components != null)
                        components.Dispose();
                }
            }
            finally
            {
                base.Dispose(disposing);
            }
        }

        #region Windows 窗体设计器生成的代码

        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));
            this.components = new System.ComponentModel.Container();
            this.mainLayoutPanel = new System.Windows.Forms.TableLayoutPanel();
            this.inputLayoutPanel = new System.Windows.Forms.TableLayoutPanel();
            this.pathLabel = new System.Windows.Forms.Label();
            this.pathTextBox = new System.Windows.Forms.TextBox();
            this.browseModelButton = new System.Windows.Forms.Button();
            this.deviceLabel = new System.Windows.Forms.Label();
            this.deviceNumericUpDown = new System.Windows.Forms.NumericUpDown();
            this.csharpButtonsPanel = new System.Windows.Forms.TableLayoutPanel();
            this.cppButtonsPanel = new System.Windows.Forms.TableLayoutPanel();
            this.loadCSharpButton = new System.Windows.Forms.Button();
            this.loadCppButton = new System.Windows.Forms.Button();
            this.convertToCppButton = new System.Windows.Forms.Button();
            this.getCSharpInfoButton = new System.Windows.Forms.Button();
            this.getCppInfoButton = new System.Windows.Forms.Button();
            this.releaseCSharpButton = new System.Windows.Forms.Button();
            this.releaseCppButton = new System.Windows.Forms.Button();
            this.modelsLayoutPanel = new System.Windows.Forms.TableLayoutPanel();
            this.csharpModelGroupBox = new System.Windows.Forms.GroupBox();
            this.csharpLayoutPanel = new System.Windows.Forms.TableLayoutPanel();
            this.csharpStateLabel = new System.Windows.Forms.Label();
            this.csharpInfoTextBox = new System.Windows.Forms.TextBox();
            this.cppModelGroupBox = new System.Windows.Forms.GroupBox();
            this.cppLayoutPanel = new System.Windows.Forms.TableLayoutPanel();
            this.cppStateLabel = new System.Windows.Forms.Label();
            this.cppInfoTextBox = new System.Windows.Forms.TextBox();
            this.statusLabel = new System.Windows.Forms.Label();
            this.mainLayoutPanel.SuspendLayout();
            this.inputLayoutPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.deviceNumericUpDown)).BeginInit();
            this.csharpButtonsPanel.SuspendLayout();
            this.cppButtonsPanel.SuspendLayout();
            this.modelsLayoutPanel.SuspendLayout();
            this.csharpModelGroupBox.SuspendLayout();
            this.csharpLayoutPanel.SuspendLayout();
            this.cppModelGroupBox.SuspendLayout();
            this.cppLayoutPanel.SuspendLayout();
            this.SuspendLayout();
            //
            // mainLayoutPanel
            //
            this.mainLayoutPanel.ColumnCount = 1;
            this.mainLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.mainLayoutPanel.Controls.Add(this.inputLayoutPanel, 0, 0);
            this.mainLayoutPanel.Controls.Add(this.modelsLayoutPanel, 0, 1);
            this.mainLayoutPanel.Controls.Add(this.statusLabel, 0, 2);
            this.mainLayoutPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.mainLayoutPanel.Location = new System.Drawing.Point(0, 0);
            this.mainLayoutPanel.Name = "mainLayoutPanel";
            this.mainLayoutPanel.Padding = new System.Windows.Forms.Padding(12);
            this.mainLayoutPanel.RowCount = 3;
            this.mainLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.mainLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.mainLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.mainLayoutPanel.Size = new System.Drawing.Size(1060, 660);
            this.mainLayoutPanel.TabIndex = 0;
            //
            // inputLayoutPanel
            //
            this.inputLayoutPanel.AutoSize = true;
            this.inputLayoutPanel.ColumnCount = 5;
            this.inputLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.inputLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.inputLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 88F));
            this.inputLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
            this.inputLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 100F));
            this.inputLayoutPanel.Controls.Add(this.pathLabel, 0, 0);
            this.inputLayoutPanel.Controls.Add(this.pathTextBox, 1, 0);
            this.inputLayoutPanel.Controls.Add(this.browseModelButton, 2, 0);
            this.inputLayoutPanel.Controls.Add(this.deviceLabel, 3, 0);
            this.inputLayoutPanel.Controls.Add(this.deviceNumericUpDown, 4, 0);
            this.inputLayoutPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.inputLayoutPanel.Location = new System.Drawing.Point(15, 15);
            this.inputLayoutPanel.Name = "inputLayoutPanel";
            this.inputLayoutPanel.RowCount = 1;
            this.inputLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.inputLayoutPanel.Size = new System.Drawing.Size(1030, 29);
            this.inputLayoutPanel.TabIndex = 0;
            //
            // pathLabel
            //
            this.pathLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.pathLabel.AutoSize = true;
            this.pathLabel.Location = new System.Drawing.Point(3, 6);
            this.pathLabel.Name = "pathLabel";
            this.pathLabel.Size = new System.Drawing.Size(56, 17);
            this.pathLabel.TabIndex = 0;
            this.pathLabel.Text = "模型路径";
            //
            // pathTextBox
            //
            this.pathTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.pathTextBox.Enabled = true;
            this.pathTextBox.Location = new System.Drawing.Point(65, 3);
            this.pathTextBox.Name = "pathTextBox";
            this.pathTextBox.ReadOnly = true;
            this.pathTextBox.Size = new System.Drawing.Size(660, 23);
            this.pathTextBox.TabIndex = 1;
            //
            // browseModelButton
            //
            this.browseModelButton.Dock = System.Windows.Forms.DockStyle.Fill;
            this.browseModelButton.Location = new System.Drawing.Point(731, 3);
            this.browseModelButton.Name = "browseModelButton";
            this.browseModelButton.Size = new System.Drawing.Size(82, 23);
            this.browseModelButton.TabIndex = 2;
            this.browseModelButton.Text = "浏览…";
            this.browseModelButton.UseVisualStyleBackColor = true;
            this.browseModelButton.Click += new System.EventHandler(this.BrowseModelButton_Click);
            //
            // deviceLabel
            //
            this.deviceLabel.Anchor = System.Windows.Forms.AnchorStyles.Left;
            this.deviceLabel.AutoSize = true;
            this.deviceLabel.Location = new System.Drawing.Point(819, 6);
            this.deviceLabel.Name = "deviceLabel";
            this.deviceLabel.Size = new System.Drawing.Size(108, 17);
            this.deviceLabel.TabIndex = 3;
            this.deviceLabel.Text = "设备（-1 为 CPU）";
            //
            // deviceNumericUpDown
            //
            this.deviceNumericUpDown.DecimalPlaces = 0;
            this.deviceNumericUpDown.Dock = System.Windows.Forms.DockStyle.Fill;
            this.deviceNumericUpDown.Enabled = true;
            this.deviceNumericUpDown.Location = new System.Drawing.Point(933, 3);
            this.deviceNumericUpDown.Maximum = new decimal(new int[] { 2147483647, 0, 0, 0 });
            this.deviceNumericUpDown.Minimum = new decimal(new int[] { 1, 0, 0, -2147483648 });
            this.deviceNumericUpDown.Name = "deviceNumericUpDown";
            this.deviceNumericUpDown.Size = new System.Drawing.Size(94, 23);
            this.deviceNumericUpDown.TabIndex = 4;
            this.deviceNumericUpDown.Value = new decimal(new int[] { 0, 0, 0, 0 });
            //
            // csharpButtonsPanel
            //
            this.csharpButtonsPanel.AutoSize = true;
            this.csharpButtonsPanel.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.csharpButtonsPanel.Size = new System.Drawing.Size(460, 44);
            this.csharpButtonsPanel.ColumnCount = 3;
            this.csharpButtonsPanel.RowCount = 1;
            this.csharpButtonsPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.csharpButtonsPanel.Margin = new System.Windows.Forms.Padding(0, 4, 0, 8);
            this.csharpButtonsPanel.Name = "csharpButtonsPanel";
            this.csharpButtonsPanel.TabIndex = 0;
            this.csharpButtonsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.csharpButtonsPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.33333F));
            this.csharpButtonsPanel.Controls.Add(this.loadCSharpButton, 0, 0);
            this.csharpButtonsPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.33333F));
            this.csharpButtonsPanel.Controls.Add(this.getCSharpInfoButton, 1, 0);
            this.csharpButtonsPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 33.33333F));
            this.csharpButtonsPanel.Controls.Add(this.releaseCSharpButton, 2, 0);
            //
            // cppButtonsPanel
            //
            this.cppButtonsPanel.AutoSize = true;
            this.cppButtonsPanel.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            this.cppButtonsPanel.Size = new System.Drawing.Size(460, 44);
            this.cppButtonsPanel.ColumnCount = 4;
            this.cppButtonsPanel.RowCount = 1;
            this.cppButtonsPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.cppButtonsPanel.Margin = new System.Windows.Forms.Padding(0, 4, 0, 8);
            this.cppButtonsPanel.Name = "cppButtonsPanel";
            this.cppButtonsPanel.TabIndex = 0;
            this.cppButtonsPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.cppButtonsPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 25.00000F));
            this.cppButtonsPanel.Controls.Add(this.loadCppButton, 0, 0);
            this.cppButtonsPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 25.00000F));
            this.cppButtonsPanel.Controls.Add(this.convertToCppButton, 1, 0);
            this.cppButtonsPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 25.00000F));
            this.cppButtonsPanel.Controls.Add(this.getCppInfoButton, 2, 0);
            this.cppButtonsPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 25.00000F));
            this.cppButtonsPanel.Controls.Add(this.releaseCppButton, 3, 0);
            //
            // loadCSharpButton
            //
            this.loadCSharpButton.AutoSize = true;
            this.loadCSharpButton.Enabled = true;
            this.loadCSharpButton.Location = new System.Drawing.Point(3, 3);
            this.loadCSharpButton.Name = "loadCSharpButton";
            this.loadCSharpButton.Padding = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.loadCSharpButton.Dock = System.Windows.Forms.DockStyle.Fill;
            this.loadCSharpButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.loadCSharpButton.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(210, 218, 228);
            this.loadCSharpButton.Margin = new System.Windows.Forms.Padding(3);
            this.loadCSharpButton.Size = new System.Drawing.Size(110, 37);
            this.loadCSharpButton.TabIndex = 0;
            this.loadCSharpButton.Text = "加载模型";
            this.loadCSharpButton.UseVisualStyleBackColor = false;
            this.loadCSharpButton.BackColor = System.Drawing.Color.FromArgb(37, 99, 235);
            this.loadCSharpButton.ForeColor = System.Drawing.Color.White;
            this.loadCSharpButton.Click += new System.EventHandler(this.LoadCSharpButton_Click);
            //
            // loadCppButton
            //
            this.loadCppButton.AutoSize = true;
            this.loadCppButton.Location = new System.Drawing.Point(119, 3);
            this.loadCppButton.Name = "loadCppButton";
            this.loadCppButton.Padding = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.loadCppButton.Dock = System.Windows.Forms.DockStyle.Fill;
            this.loadCppButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.loadCppButton.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(210, 218, 228);
            this.loadCppButton.Margin = new System.Windows.Forms.Padding(3);
            this.loadCppButton.Size = new System.Drawing.Size(120, 37);
            this.loadCppButton.TabIndex = 0;
            this.loadCppButton.Text = "加载模型";
            this.loadCppButton.UseVisualStyleBackColor = false;
            this.loadCppButton.BackColor = System.Drawing.Color.FromArgb(37, 99, 235);
            this.loadCppButton.ForeColor = System.Drawing.Color.White;
            this.loadCppButton.Click += new System.EventHandler(this.LoadCppButton_Click);
            //
            // convertToCppButton
            //
            this.convertToCppButton.AutoSize = true;
            this.convertToCppButton.Enabled = false;
            this.convertToCppButton.Location = new System.Drawing.Point(119, 3);
            this.convertToCppButton.Name = "convertToCppButton";
            this.convertToCppButton.Padding = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.convertToCppButton.Dock = System.Windows.Forms.DockStyle.Fill;
            this.convertToCppButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.convertToCppButton.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(210, 218, 228);
            this.convertToCppButton.Margin = new System.Windows.Forms.Padding(3);
            this.convertToCppButton.Size = new System.Drawing.Size(134, 37);
            this.convertToCppButton.TabIndex = 1;
            this.convertToCppButton.Text = "从C#共享";
            this.convertToCppButton.UseVisualStyleBackColor = false;
            this.convertToCppButton.BackColor = System.Drawing.Color.White;
            this.convertToCppButton.ForeColor = System.Drawing.Color.FromArgb(45, 55, 72);
            this.convertToCppButton.Click += new System.EventHandler(this.ConvertToCppButton_Click);
            //
            // getCSharpInfoButton
            //
            this.getCSharpInfoButton.AutoSize = true;
            this.getCSharpInfoButton.Enabled = false;
            this.getCSharpInfoButton.Location = new System.Drawing.Point(259, 3);
            this.getCSharpInfoButton.Name = "getCSharpInfoButton";
            this.getCSharpInfoButton.Padding = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.getCSharpInfoButton.Dock = System.Windows.Forms.DockStyle.Fill;
            this.getCSharpInfoButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.getCSharpInfoButton.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(210, 218, 228);
            this.getCSharpInfoButton.Margin = new System.Windows.Forms.Padding(3);
            this.getCSharpInfoButton.Size = new System.Drawing.Size(134, 37);
            this.getCSharpInfoButton.TabIndex = 1;
            this.getCSharpInfoButton.Text = "获取信息";
            this.getCSharpInfoButton.UseVisualStyleBackColor = false;
            this.getCSharpInfoButton.BackColor = System.Drawing.Color.White;
            this.getCSharpInfoButton.ForeColor = System.Drawing.Color.FromArgb(45, 55, 72);
            this.getCSharpInfoButton.Click += new System.EventHandler(this.GetCSharpInfoButton_Click);
            //
            // getCppInfoButton
            //
            this.getCppInfoButton.AutoSize = true;
            this.getCppInfoButton.Enabled = false;
            this.getCppInfoButton.Location = new System.Drawing.Point(399, 3);
            this.getCppInfoButton.Name = "getCppInfoButton";
            this.getCppInfoButton.Padding = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.getCppInfoButton.Dock = System.Windows.Forms.DockStyle.Fill;
            this.getCppInfoButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.getCppInfoButton.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(210, 218, 228);
            this.getCppInfoButton.Margin = new System.Windows.Forms.Padding(3);
            this.getCppInfoButton.Size = new System.Drawing.Size(144, 37);
            this.getCppInfoButton.TabIndex = 2;
            this.getCppInfoButton.Text = "获取信息";
            this.getCppInfoButton.UseVisualStyleBackColor = false;
            this.getCppInfoButton.BackColor = System.Drawing.Color.White;
            this.getCppInfoButton.ForeColor = System.Drawing.Color.FromArgb(45, 55, 72);
            this.getCppInfoButton.Click += new System.EventHandler(this.GetCppInfoButton_Click);
            //
            // releaseCSharpButton
            //
            this.releaseCSharpButton.AutoSize = true;
            this.releaseCSharpButton.Enabled = false;
            this.releaseCSharpButton.Location = new System.Drawing.Point(549, 3);
            this.releaseCSharpButton.Name = "releaseCSharpButton";
            this.releaseCSharpButton.Padding = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.releaseCSharpButton.Dock = System.Windows.Forms.DockStyle.Fill;
            this.releaseCSharpButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.releaseCSharpButton.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(210, 218, 228);
            this.releaseCSharpButton.Margin = new System.Windows.Forms.Padding(3);
            this.releaseCSharpButton.Size = new System.Drawing.Size(110, 37);
            this.releaseCSharpButton.TabIndex = 2;
            this.releaseCSharpButton.Text = "释放模型";
            this.releaseCSharpButton.UseVisualStyleBackColor = false;
            this.releaseCSharpButton.BackColor = System.Drawing.Color.White;
            this.releaseCSharpButton.ForeColor = System.Drawing.Color.FromArgb(45, 55, 72);
            this.releaseCSharpButton.Click += new System.EventHandler(this.ReleaseCSharpButton_Click);
            //
            // releaseCppButton
            //
            this.releaseCppButton.AutoSize = true;
            this.releaseCppButton.Enabled = false;
            this.releaseCppButton.Location = new System.Drawing.Point(665, 3);
            this.releaseCppButton.Name = "releaseCppButton";
            this.releaseCppButton.Padding = new System.Windows.Forms.Padding(4, 5, 4, 5);
            this.releaseCppButton.Dock = System.Windows.Forms.DockStyle.Fill;
            this.releaseCppButton.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.releaseCppButton.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(210, 218, 228);
            this.releaseCppButton.Margin = new System.Windows.Forms.Padding(3);
            this.releaseCppButton.Size = new System.Drawing.Size(122, 37);
            this.releaseCppButton.TabIndex = 3;
            this.releaseCppButton.Text = "释放模型";
            this.releaseCppButton.UseVisualStyleBackColor = false;
            this.releaseCppButton.BackColor = System.Drawing.Color.White;
            this.releaseCppButton.ForeColor = System.Drawing.Color.FromArgb(45, 55, 72);
            this.releaseCppButton.Click += new System.EventHandler(this.ReleaseCppButton_Click);
            //
            // modelsLayoutPanel
            //
            this.modelsLayoutPanel.ColumnCount = 2;
            this.modelsLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.modelsLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 50F));
            this.modelsLayoutPanel.Controls.Add(this.csharpModelGroupBox, 0, 0);
            this.modelsLayoutPanel.Controls.Add(this.cppModelGroupBox, 1, 0);
            this.modelsLayoutPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.modelsLayoutPanel.Location = new System.Drawing.Point(15, 99);
            this.modelsLayoutPanel.Name = "modelsLayoutPanel";
            this.modelsLayoutPanel.RowCount = 1;
            this.modelsLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.modelsLayoutPanel.Size = new System.Drawing.Size(1030, 521);
            this.modelsLayoutPanel.TabIndex = 2;
            //
            // csharpModelGroupBox
            //
            this.csharpModelGroupBox.Controls.Add(this.csharpLayoutPanel);
            this.csharpModelGroupBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.csharpModelGroupBox.Location = new System.Drawing.Point(3, 3);
            this.csharpModelGroupBox.Name = "csharpModelGroupBox";
            this.csharpModelGroupBox.Padding = new System.Windows.Forms.Padding(12);
            this.csharpModelGroupBox.BackColor = System.Drawing.Color.White;
            this.csharpModelGroupBox.Size = new System.Drawing.Size(509, 515);
            this.csharpModelGroupBox.TabIndex = 0;
            this.csharpModelGroupBox.TabStop = false;
            this.csharpModelGroupBox.Text = "C# 模型";
            //
            // csharpLayoutPanel
            //
            this.csharpLayoutPanel.ColumnCount = 1;
            this.csharpLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.csharpLayoutPanel.Controls.Add(this.csharpButtonsPanel, 0, 0);
            this.csharpLayoutPanel.Controls.Add(this.csharpStateLabel, 0, 1);
            this.csharpLayoutPanel.Controls.Add(this.csharpInfoTextBox, 0, 2);
            this.csharpLayoutPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.csharpLayoutPanel.Location = new System.Drawing.Point(8, 24);
            this.csharpLayoutPanel.Name = "csharpLayoutPanel";
            this.csharpLayoutPanel.RowCount = 3;
            this.csharpLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.csharpLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.csharpLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.csharpLayoutPanel.Size = new System.Drawing.Size(493, 483);
            this.csharpLayoutPanel.TabIndex = 0;
            //
            // csharpStateLabel
            //
            this.csharpStateLabel.AutoSize = true;
            this.csharpStateLabel.ForeColor = System.Drawing.Color.FromArgb(100, 116, 139);
            this.csharpStateLabel.Padding = new System.Windows.Forms.Padding(0, 0, 0, 8);
            this.csharpStateLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.csharpStateLabel.Location = new System.Drawing.Point(3, 0);
            this.csharpStateLabel.Name = "csharpStateLabel";
            this.csharpStateLabel.Size = new System.Drawing.Size(487, 17);
            this.csharpStateLabel.TabIndex = 0;
            this.csharpStateLabel.Text = "未加载，编号：-1";
            //
            // csharpInfoTextBox
            //
            this.csharpInfoTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.csharpInfoTextBox.Location = new System.Drawing.Point(3, 20);
            this.csharpInfoTextBox.MaxLength = 0;
            this.csharpInfoTextBox.Multiline = true;
            this.csharpInfoTextBox.Name = "csharpInfoTextBox";
            this.csharpInfoTextBox.ReadOnly = true;
            this.csharpInfoTextBox.BackColor = System.Drawing.Color.FromArgb(248, 250, 252);
            this.csharpInfoTextBox.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.csharpInfoTextBox.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.csharpInfoTextBox.Size = new System.Drawing.Size(487, 460);
            this.csharpInfoTextBox.TabIndex = 1;
            this.csharpInfoTextBox.WordWrap = false;
            //
            // cppModelGroupBox
            //
            this.cppModelGroupBox.Controls.Add(this.cppLayoutPanel);
            this.cppModelGroupBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.cppModelGroupBox.Location = new System.Drawing.Point(518, 3);
            this.cppModelGroupBox.Name = "cppModelGroupBox";
            this.cppModelGroupBox.Padding = new System.Windows.Forms.Padding(12);
            this.cppModelGroupBox.BackColor = System.Drawing.Color.White;
            this.cppModelGroupBox.Size = new System.Drawing.Size(509, 515);
            this.cppModelGroupBox.TabIndex = 1;
            this.cppModelGroupBox.TabStop = false;
            this.cppModelGroupBox.Text = "C++ 模型";
            //
            // cppLayoutPanel
            //
            this.cppLayoutPanel.ColumnCount = 1;
            this.cppLayoutPanel.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.cppLayoutPanel.Controls.Add(this.cppButtonsPanel, 0, 0);
            this.cppLayoutPanel.Controls.Add(this.cppStateLabel, 0, 1);
            this.cppLayoutPanel.Controls.Add(this.cppInfoTextBox, 0, 2);
            this.cppLayoutPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.cppLayoutPanel.Location = new System.Drawing.Point(8, 24);
            this.cppLayoutPanel.Name = "cppLayoutPanel";
            this.cppLayoutPanel.RowCount = 3;
            this.cppLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.cppLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle());
            this.cppLayoutPanel.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.cppLayoutPanel.Size = new System.Drawing.Size(493, 483);
            this.cppLayoutPanel.TabIndex = 0;
            //
            // cppStateLabel
            //
            this.cppStateLabel.AutoSize = true;
            this.cppStateLabel.ForeColor = System.Drawing.Color.FromArgb(100, 116, 139);
            this.cppStateLabel.Padding = new System.Windows.Forms.Padding(0, 0, 0, 8);
            this.cppStateLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.cppStateLabel.Location = new System.Drawing.Point(3, 0);
            this.cppStateLabel.Name = "cppStateLabel";
            this.cppStateLabel.Size = new System.Drawing.Size(487, 17);
            this.cppStateLabel.TabIndex = 0;
            this.cppStateLabel.Text = "未加载，编号：-1";
            //
            // cppInfoTextBox
            //
            this.cppInfoTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.cppInfoTextBox.Location = new System.Drawing.Point(3, 20);
            this.cppInfoTextBox.MaxLength = 0;
            this.cppInfoTextBox.Multiline = true;
            this.cppInfoTextBox.Name = "cppInfoTextBox";
            this.cppInfoTextBox.ReadOnly = true;
            this.cppInfoTextBox.BackColor = System.Drawing.Color.FromArgb(248, 250, 252);
            this.cppInfoTextBox.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.cppInfoTextBox.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.cppInfoTextBox.Size = new System.Drawing.Size(487, 460);
            this.cppInfoTextBox.TabIndex = 1;
            this.cppInfoTextBox.WordWrap = false;
            //
            // statusLabel
            //
            this.statusLabel.AutoSize = true;
            this.statusLabel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.statusLabel.Location = new System.Drawing.Point(15, 623);
            this.statusLabel.Name = "statusLabel";
            this.statusLabel.Padding = new System.Windows.Forms.Padding(0, 8, 0, 0);
            this.statusLabel.Size = new System.Drawing.Size(1030, 25);
            this.statusLabel.TabIndex = 3;
            this.statusLabel.Text = "浏览选择模型后，可分别加载 C# / C++；转换按钮仅共享已有 C# 模型。";
            this.statusLabel.UseMnemonic = false;
            //
            // MainForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.BackColor = System.Drawing.Color.FromArgb(241, 245, 249);
            this.ForeColor = System.Drawing.Color.FromArgb(30, 41, 59);
            this.ClientSize = new System.Drawing.Size(1060, 660);
            this.Controls.Add(this.mainLayoutPanel);
            this.Font = new System.Drawing.Font("Microsoft YaHei", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MinimumSize = new System.Drawing.Size(960, 540);
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "C# / C++ 模型共享测试";
            this.mainLayoutPanel.ResumeLayout(false);
            this.mainLayoutPanel.PerformLayout();
            this.inputLayoutPanel.ResumeLayout(false);
            this.inputLayoutPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.deviceNumericUpDown)).EndInit();
            this.csharpButtonsPanel.ResumeLayout(false);
            this.csharpButtonsPanel.PerformLayout();
            this.cppButtonsPanel.ResumeLayout(false);
            this.cppButtonsPanel.PerformLayout();
            this.modelsLayoutPanel.ResumeLayout(false);
            this.csharpModelGroupBox.ResumeLayout(false);
            this.csharpLayoutPanel.ResumeLayout(false);
            this.csharpLayoutPanel.PerformLayout();
            this.cppModelGroupBox.ResumeLayout(false);
            this.cppLayoutPanel.ResumeLayout(false);
            this.cppLayoutPanel.PerformLayout();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.TableLayoutPanel mainLayoutPanel;
        private System.Windows.Forms.TableLayoutPanel inputLayoutPanel;
        private System.Windows.Forms.Label pathLabel;
        private System.Windows.Forms.TextBox pathTextBox;
        private System.Windows.Forms.Button browseModelButton;
        private System.Windows.Forms.Label deviceLabel;
        private System.Windows.Forms.NumericUpDown deviceNumericUpDown;
        private System.Windows.Forms.TableLayoutPanel csharpButtonsPanel;
        private System.Windows.Forms.TableLayoutPanel cppButtonsPanel;
        private System.Windows.Forms.Button loadCSharpButton;
        private System.Windows.Forms.Button loadCppButton;
        private System.Windows.Forms.Button convertToCppButton;
        private System.Windows.Forms.Button getCSharpInfoButton;
        private System.Windows.Forms.Button getCppInfoButton;
        private System.Windows.Forms.Button releaseCSharpButton;
        private System.Windows.Forms.Button releaseCppButton;
        private System.Windows.Forms.TableLayoutPanel modelsLayoutPanel;
        private System.Windows.Forms.GroupBox csharpModelGroupBox;
        private System.Windows.Forms.TableLayoutPanel csharpLayoutPanel;
        private System.Windows.Forms.Label csharpStateLabel;
        private System.Windows.Forms.TextBox csharpInfoTextBox;
        private System.Windows.Forms.GroupBox cppModelGroupBox;
        private System.Windows.Forms.TableLayoutPanel cppLayoutPanel;
        private System.Windows.Forms.Label cppStateLabel;
        private System.Windows.Forms.TextBox cppInfoTextBox;
        private System.Windows.Forms.Label statusLabel;
    }
}
