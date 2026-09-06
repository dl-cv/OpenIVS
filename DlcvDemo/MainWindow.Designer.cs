using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using DLCV;

namespace DlcvDemo
{
    public partial class MainWindow
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            if (disposing)
            {
                Icon?.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法，不要在代码编辑器中修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.rootLayout = new System.Windows.Forms.TableLayoutPanel();
            this.topCard = new System.Windows.Forms.Panel();
            this.topLayout = new System.Windows.Forms.TableLayoutPanel();
            this.leftOpsTable = new System.Windows.Forms.TableLayoutPanel();
            this.button_load_model = new System.Windows.Forms.Button();
            this.button_infer = new System.Windows.Forms.Button();
            this.button_infer_json = new System.Windows.Forms.Button();
            this.button_thread_test = new System.Windows.Forms.Button();
            this.button_consistency_test = new System.Windows.Forms.Button();
            this.paramsTable = new System.Windows.Forms.TableLayoutPanel();
            this.label_choose_gpu = new System.Windows.Forms.Label();
            this.comboBox1 = new System.Windows.Forms.ComboBox();
            this.label_batch_size = new System.Windows.Forms.Label();
            this.numericUpDown_batch_size = new System.Windows.Forms.NumericUpDown();
            this.label_threshold = new System.Windows.Forms.Label();
            this.numericUpDown_threshold = new System.Windows.Forms.NumericUpDown();
            this.label_num_thread = new System.Windows.Forms.Label();
            this.numericUpDown_num_thread = new System.Windows.Forms.NumericUpDown();
            this.checkBox_calc_mean = new System.Windows.Forms.CheckBox();
            this.rightOpsTable = new System.Windows.Forms.TableLayoutPanel();
            this.checkBox_rpc_mode = new System.Windows.Forms.CheckBox();
            this.button_open_image = new System.Windows.Forms.Button();
            this.button_save_img = new System.Windows.Forms.Button();
            this.button_free_model = new System.Windows.Forms.Button();
            this.button_free_all_model = new System.Windows.Forms.Button();
            this.button_check_environment = new System.Windows.Forms.Button();
            this.button_check_dog = new System.Windows.Forms.Button();
            this.button_github = new System.Windows.Forms.Button();
            this.button_get_model_info = new System.Windows.Forms.Button();
            this.splitContainerMain = new System.Windows.Forms.SplitContainer();
            this.resultCard = new System.Windows.Forms.Panel();
            this.resultLayout = new System.Windows.Forms.TableLayoutPanel();
            this.label_result = new System.Windows.Forms.Label();
            this.richTextBox1 = new System.Windows.Forms.TextBox();
            this.imageCard = new System.Windows.Forms.Panel();
            this.imagePanel1 = new DLCV.ImageViewer();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainerMain)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown_batch_size)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown_threshold)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown_num_thread)).BeginInit();
            this.splitContainerMain.Panel1.SuspendLayout();
            this.splitContainerMain.Panel2.SuspendLayout();
            this.splitContainerMain.SuspendLayout();
            this.resultCard.SuspendLayout();
            this.resultLayout.SuspendLayout();
            this.imageCard.SuspendLayout();
            this.rootLayout.SuspendLayout();
            this.topCard.SuspendLayout();
            this.topLayout.SuspendLayout();
            this.leftOpsTable.SuspendLayout();
            this.paramsTable.SuspendLayout();
            this.rightOpsTable.SuspendLayout();
            this.SuspendLayout();
            //
            // rootLayout
            //
            this.rootLayout.BackColor = Color.FromArgb(243, 245, 247);
            this.rootLayout.ColumnCount = 1;
            this.rootLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rootLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rootLayout.Padding = new System.Windows.Forms.Padding(8);
            this.rootLayout.Name = "rootLayout";
            this.rootLayout.RowCount = 2;
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.rootLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            //
            // topCard
            //
            this.topCard.BackColor = Color.White;
            this.topCard.Dock = System.Windows.Forms.DockStyle.Fill;
            this.topCard.Margin = new System.Windows.Forms.Padding(6);
            this.topCard.Name = "topCard";
            this.topCard.Padding = new System.Windows.Forms.Padding(10);
            this.topCard.Size = new System.Drawing.Size(1100, 160);
            //
            // topLayout
            //
            this.topLayout.BackColor = Color.White;
            this.topLayout.ColumnCount = 5;
            this.topLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            this.topLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 16F));
            this.topLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.topLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 16F));
            this.topLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            this.topLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.topLayout.Name = "topLayout";
            this.topLayout.RowCount = 1;
            this.topLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            //
            // leftOpsTable
            //
            this.leftOpsTable.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.leftOpsTable.BackColor = Color.White;
            this.leftOpsTable.ColumnCount = 2;
            this.leftOpsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            this.leftOpsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            this.leftOpsTable.Name = "leftOpsTable";
            this.leftOpsTable.Size = new System.Drawing.Size(224, 140);
            this.leftOpsTable.RowCount = 3;
            this.leftOpsTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.leftOpsTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.leftOpsTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            //
            // button_load_model
            //
            this.button_load_model.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.button_load_model.BackColor = Color.FromArgb(25, 118, 210);
            this.button_load_model.Cursor = System.Windows.Forms.Cursors.Hand;
            this.button_load_model.FlatAppearance.BorderSize = 0;
            this.button_load_model.FlatAppearance.MouseDownBackColor = Color.FromArgb(13, 71, 161);
            this.button_load_model.FlatAppearance.MouseOverBackColor = Color.FromArgb(21, 101, 192);
            this.button_load_model.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button_load_model.ForeColor = Color.White;
            this.button_load_model.Margin = new System.Windows.Forms.Padding(4);
            this.button_load_model.Name = "button_load_model";
            this.button_load_model.Size = new System.Drawing.Size(216, 38);
            this.button_load_model.TabIndex = 6;
            this.button_load_model.Text = "加载模型";
            this.button_load_model.UseVisualStyleBackColor = false;
            //
            // button_infer
            //
            this.button_infer.BackColor = Color.FromArgb(25, 118, 210);
            this.button_infer.Cursor = System.Windows.Forms.Cursors.Hand;
            this.button_infer.FlatAppearance.BorderSize = 0;
            this.button_infer.FlatAppearance.MouseDownBackColor = Color.FromArgb(13, 71, 161);
            this.button_infer.FlatAppearance.MouseOverBackColor = Color.FromArgb(21, 101, 192);
            this.button_infer.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button_infer.ForeColor = Color.White;
            this.button_infer.Margin = new System.Windows.Forms.Padding(4);
            this.button_infer.Name = "button_infer";
            this.button_infer.Size = new System.Drawing.Size(104, 38);
            this.button_infer.TabIndex = 7;
            this.button_infer.Text = "单次推理";
            this.button_infer.UseVisualStyleBackColor = false;
            //
            // button_infer_json
            //
            this.button_infer_json.BackColor = Color.FromArgb(231, 235, 239);
            this.button_infer_json.Cursor = System.Windows.Forms.Cursors.Hand;
            this.button_infer_json.FlatAppearance.BorderSize = 0;
            this.button_infer_json.FlatAppearance.MouseDownBackColor = Color.FromArgb(199, 207, 215);
            this.button_infer_json.FlatAppearance.MouseOverBackColor = Color.FromArgb(216, 222, 228);
            this.button_infer_json.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button_infer_json.ForeColor = Color.FromArgb(38, 50, 56);
            this.button_infer_json.Margin = new System.Windows.Forms.Padding(4);
            this.button_infer_json.Name = "button_infer_json";
            this.button_infer_json.Size = new System.Drawing.Size(104, 38);
            this.button_infer_json.TabIndex = 8;
            this.button_infer_json.Text = "推理 JSON";
            this.button_infer_json.UseVisualStyleBackColor = false;
            //
            // button_thread_test
            //
            this.button_thread_test.BackColor = Color.FromArgb(231, 235, 239);
            this.button_thread_test.Cursor = System.Windows.Forms.Cursors.Hand;
            this.button_thread_test.FlatAppearance.BorderSize = 0;
            this.button_thread_test.FlatAppearance.MouseDownBackColor = Color.FromArgb(199, 207, 215);
            this.button_thread_test.FlatAppearance.MouseOverBackColor = Color.FromArgb(216, 222, 228);
            this.button_thread_test.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button_thread_test.ForeColor = Color.FromArgb(38, 50, 56);
            this.button_thread_test.Margin = new System.Windows.Forms.Padding(4);
            this.button_thread_test.Name = "button_thread_test";
            this.button_thread_test.Size = new System.Drawing.Size(104, 38);
            this.button_thread_test.TabIndex = 9;
            this.button_thread_test.Text = "多线程测试";
            this.button_thread_test.UseVisualStyleBackColor = false;
            //
            // button_consistency_test
            //
            this.button_consistency_test.BackColor = Color.FromArgb(231, 235, 239);
            this.button_consistency_test.Cursor = System.Windows.Forms.Cursors.Hand;
            this.button_consistency_test.FlatAppearance.BorderSize = 0;
            this.button_consistency_test.FlatAppearance.MouseDownBackColor = Color.FromArgb(199, 207, 215);
            this.button_consistency_test.FlatAppearance.MouseOverBackColor = Color.FromArgb(216, 222, 228);
            this.button_consistency_test.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button_consistency_test.ForeColor = Color.FromArgb(38, 50, 56);
            this.button_consistency_test.Margin = new System.Windows.Forms.Padding(4);
            this.button_consistency_test.Name = "button_consistency_test";
            this.button_consistency_test.Size = new System.Drawing.Size(104, 38);
            this.button_consistency_test.TabIndex = 10;
            this.button_consistency_test.Text = "一致性测试";
            this.button_consistency_test.UseVisualStyleBackColor = false;
            //
            // paramsTable
            //
            this.paramsTable.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.paramsTable.BackColor = Color.White;
            this.paramsTable.ColumnCount = 5;
            this.paramsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            this.paramsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            this.paramsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            this.paramsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            this.paramsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.paramsTable.Name = "paramsTable";
            this.paramsTable.Size = new System.Drawing.Size(400, 140);
            this.paramsTable.RowCount = 3;
            this.paramsTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.paramsTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.paramsTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            //
            // label_choose_gpu
            //
            this.label_choose_gpu.AutoSize = true;
            this.label_choose_gpu.BackColor = Color.White;
            this.label_choose_gpu.ForeColor = Color.FromArgb(55, 71, 79);
            this.label_choose_gpu.Margin = new System.Windows.Forms.Padding(0, 13, 8, 13);
            this.label_choose_gpu.Name = "label_choose_gpu";
            this.label_choose_gpu.Text = "选择显卡";
            //
            // comboBox1
            //
            this.comboBox1.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.comboBox1.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBox1.Margin = new System.Windows.Forms.Padding(0, 6, 0, 6);
            this.comboBox1.Name = "comboBox1";
            this.comboBox1.Size = new System.Drawing.Size(300, 34);
            this.comboBox1.TabIndex = 0;
            //
            // label_batch_size
            //
            this.label_batch_size.AutoSize = true;
            this.label_batch_size.BackColor = Color.White;
            this.label_batch_size.ForeColor = Color.FromArgb(55, 71, 79);
            this.label_batch_size.Margin = new System.Windows.Forms.Padding(0, 13, 8, 13);
            this.label_batch_size.Name = "label_batch_size";
            this.label_batch_size.Text = "batch_size";
            //
            // numericUpDown_batch_size
            //
            this.numericUpDown_batch_size.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.numericUpDown_batch_size.Margin = new System.Windows.Forms.Padding(0, 6, 0, 6);
            this.numericUpDown_batch_size.Maximum = new decimal(new int[] { 1024, 0, 0, 0 });
            this.numericUpDown_batch_size.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_batch_size.Name = "numericUpDown_batch_size";
            this.numericUpDown_batch_size.Size = new System.Drawing.Size(110, 34);
            this.numericUpDown_batch_size.TabIndex = 2;
            this.numericUpDown_batch_size.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.numericUpDown_batch_size.Value = new decimal(new int[] { 1, 0, 0, 0 });
            //
            // label_threshold
            //
            this.label_threshold.AutoSize = true;
            this.label_threshold.BackColor = Color.White;
            this.label_threshold.ForeColor = Color.FromArgb(55, 71, 79);
            this.label_threshold.Margin = new System.Windows.Forms.Padding(12, 13, 8, 13);
            this.label_threshold.Name = "label_threshold";
            this.label_threshold.Text = "threshold";
            //
            // numericUpDown_threshold
            //
            this.numericUpDown_threshold.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.numericUpDown_threshold.DecimalPlaces = 2;
            this.numericUpDown_threshold.Increment = 0.05m;
            this.numericUpDown_threshold.Margin = new System.Windows.Forms.Padding(0, 6, 0, 6);
            this.numericUpDown_threshold.Maximum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_threshold.Name = "numericUpDown_threshold";
            this.numericUpDown_threshold.Size = new System.Drawing.Size(110, 34);
            this.numericUpDown_threshold.TabIndex = 3;
            this.numericUpDown_threshold.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.numericUpDown_threshold.Value = new decimal(new int[] { 5, 0, 0, 65536 });
            //
            // label_num_thread
            //
            this.label_num_thread.AutoSize = true;
            this.label_num_thread.BackColor = Color.White;
            this.label_num_thread.ForeColor = Color.FromArgb(55, 71, 79);
            this.label_num_thread.Margin = new System.Windows.Forms.Padding(0, 13, 8, 13);
            this.label_num_thread.Name = "label_num_thread";
            this.label_num_thread.Text = "线程数";
            //
            // numericUpDown_num_thread
            //
            this.numericUpDown_num_thread.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.numericUpDown_num_thread.Margin = new System.Windows.Forms.Padding(0, 6, 0, 6);
            this.numericUpDown_num_thread.Maximum = new decimal(new int[] { 32, 0, 0, 0 });
            this.numericUpDown_num_thread.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.numericUpDown_num_thread.Name = "numericUpDown_num_thread";
            this.numericUpDown_num_thread.Size = new System.Drawing.Size(110, 34);
            this.numericUpDown_num_thread.TabIndex = 4;
            this.numericUpDown_num_thread.TextAlign = System.Windows.Forms.HorizontalAlignment.Center;
            this.numericUpDown_num_thread.Value = new decimal(new int[] { 1, 0, 0, 0 });
            //
            // checkBox_calc_mean
            //
            this.checkBox_calc_mean.AutoSize = true;
            this.checkBox_calc_mean.BackColor = Color.White;
            this.checkBox_calc_mean.ThreeState = true;
            this.checkBox_calc_mean.CheckState = System.Windows.Forms.CheckState.Indeterminate;
            this.checkBox_calc_mean.ForeColor = Color.FromArgb(55, 71, 79);
            this.checkBox_calc_mean.Margin = new System.Windows.Forms.Padding(12, 11, 0, 11);
            this.checkBox_calc_mean.Name = "checkBox_calc_mean";
            this.checkBox_calc_mean.TabIndex = 5;
            this.checkBox_calc_mean.Text = "计算均值：默认";
            this.checkBox_calc_mean.UseVisualStyleBackColor = false;
            //
            // rightOpsTable
            //
            this.rightOpsTable.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.rightOpsTable.BackColor = Color.White;
            this.rightOpsTable.ColumnCount = 4;
            this.rightOpsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            this.rightOpsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            this.rightOpsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            this.rightOpsTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.AutoSize));
            this.rightOpsTable.Name = "rightOpsTable";
            this.rightOpsTable.Size = new System.Drawing.Size(448, 140);
            this.rightOpsTable.RowCount = 3;
            this.rightOpsTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.rightOpsTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.rightOpsTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            //
            // checkBox_rpc_mode
            //
            this.checkBox_rpc_mode.AutoSize = true;
            this.checkBox_rpc_mode.BackColor = Color.White;
            this.checkBox_rpc_mode.ForeColor = Color.FromArgb(55, 71, 79);
            this.checkBox_rpc_mode.Margin = new System.Windows.Forms.Padding(0, 11, 0, 11);
            this.checkBox_rpc_mode.Name = "checkBox_rpc_mode";
            this.checkBox_rpc_mode.TabIndex = 11;
            this.checkBox_rpc_mode.Text = "RPC 模式";
            this.checkBox_rpc_mode.UseVisualStyleBackColor = false;
            //
            // button_open_image
            //
            this.button_open_image.Anchor = System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.button_open_image.BackColor = Color.FromArgb(25, 118, 210);
            this.button_open_image.Cursor = System.Windows.Forms.Cursors.Hand;
            this.button_open_image.FlatAppearance.BorderSize = 0;
            this.button_open_image.FlatAppearance.MouseDownBackColor = Color.FromArgb(13, 71, 161);
            this.button_open_image.FlatAppearance.MouseOverBackColor = Color.FromArgb(21, 101, 192);
            this.button_open_image.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button_open_image.ForeColor = Color.White;
            this.button_open_image.Margin = new System.Windows.Forms.Padding(4);
            this.button_open_image.Name = "button_open_image";
            this.button_open_image.Size = new System.Drawing.Size(216, 38);
            this.button_open_image.TabIndex = 12;
            this.button_open_image.Text = "打开图片推理";
            this.button_open_image.UseVisualStyleBackColor = false;
            //
            // button_save_img
            //
            this.button_save_img.BackColor = Color.FromArgb(231, 235, 239);
            this.button_save_img.Cursor = System.Windows.Forms.Cursors.Hand;
            this.button_save_img.FlatAppearance.BorderSize = 0;
            this.button_save_img.FlatAppearance.MouseDownBackColor = Color.FromArgb(199, 207, 215);
            this.button_save_img.FlatAppearance.MouseOverBackColor = Color.FromArgb(216, 222, 228);
            this.button_save_img.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button_save_img.ForeColor = Color.FromArgb(38, 50, 56);
            this.button_save_img.Margin = new System.Windows.Forms.Padding(4);
            this.button_save_img.Name = "button_save_img";
            this.button_save_img.Size = new System.Drawing.Size(104, 38);
            this.button_save_img.TabIndex = 13;
            this.button_save_img.Text = "保存图像";
            this.button_save_img.UseVisualStyleBackColor = false;
            //
            // button_free_model
            //
            this.button_free_model.BackColor = Color.FromArgb(239, 83, 80);
            this.button_free_model.Cursor = System.Windows.Forms.Cursors.Hand;
            this.button_free_model.FlatAppearance.BorderSize = 0;
            this.button_free_model.FlatAppearance.MouseDownBackColor = Color.FromArgb(198, 40, 40);
            this.button_free_model.FlatAppearance.MouseOverBackColor = Color.FromArgb(229, 57, 53);
            this.button_free_model.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button_free_model.ForeColor = Color.White;
            this.button_free_model.Margin = new System.Windows.Forms.Padding(4);
            this.button_free_model.Name = "button_free_model";
            this.button_free_model.Size = new System.Drawing.Size(104, 38);
            this.button_free_model.TabIndex = 14;
            this.button_free_model.Text = "释放模型";
            this.button_free_model.UseVisualStyleBackColor = false;
            //
            // button_free_all_model
            //
            this.button_free_all_model.BackColor = Color.FromArgb(239, 83, 80);
            this.button_free_all_model.Cursor = System.Windows.Forms.Cursors.Hand;
            this.button_free_all_model.FlatAppearance.BorderSize = 0;
            this.button_free_all_model.FlatAppearance.MouseDownBackColor = Color.FromArgb(198, 40, 40);
            this.button_free_all_model.FlatAppearance.MouseOverBackColor = Color.FromArgb(229, 57, 53);
            this.button_free_all_model.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button_free_all_model.ForeColor = Color.White;
            this.button_free_all_model.Margin = new System.Windows.Forms.Padding(4);
            this.button_free_all_model.Name = "button_free_all_model";
            this.button_free_all_model.Size = new System.Drawing.Size(104, 38);
            this.button_free_all_model.TabIndex = 15;
            this.button_free_all_model.Text = "释放所有模型";
            this.button_free_all_model.UseVisualStyleBackColor = false;
            //
            // button_check_environment
            //
            this.button_check_environment.BackColor = Color.FromArgb(231, 235, 239);
            this.button_check_environment.Cursor = System.Windows.Forms.Cursors.Hand;
            this.button_check_environment.FlatAppearance.BorderSize = 0;
            this.button_check_environment.FlatAppearance.MouseDownBackColor = Color.FromArgb(199, 207, 215);
            this.button_check_environment.FlatAppearance.MouseOverBackColor = Color.FromArgb(216, 222, 228);
            this.button_check_environment.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button_check_environment.ForeColor = Color.FromArgb(38, 50, 56);
            this.button_check_environment.Margin = new System.Windows.Forms.Padding(4);
            this.button_check_environment.Name = "button_check_environment";
            this.button_check_environment.Size = new System.Drawing.Size(104, 38);
            this.button_check_environment.TabIndex = 16;
            this.button_check_environment.Text = "检查环境";
            this.button_check_environment.UseVisualStyleBackColor = false;
            //
            // button_check_dog
            //
            this.button_check_dog.BackColor = Color.FromArgb(231, 235, 239);
            this.button_check_dog.Cursor = System.Windows.Forms.Cursors.Hand;
            this.button_check_dog.FlatAppearance.BorderSize = 0;
            this.button_check_dog.FlatAppearance.MouseDownBackColor = Color.FromArgb(199, 207, 215);
            this.button_check_dog.FlatAppearance.MouseOverBackColor = Color.FromArgb(216, 222, 228);
            this.button_check_dog.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button_check_dog.ForeColor = Color.FromArgb(38, 50, 56);
            this.button_check_dog.Margin = new System.Windows.Forms.Padding(4);
            this.button_check_dog.Name = "button_check_dog";
            this.button_check_dog.Size = new System.Drawing.Size(104, 38);
            this.button_check_dog.TabIndex = 17;
            this.button_check_dog.Text = "检查加密狗";
            this.button_check_dog.UseVisualStyleBackColor = false;
            //
            // button_github
            //
            this.button_github.BackColor = Color.FromArgb(231, 235, 239);
            this.button_github.Cursor = System.Windows.Forms.Cursors.Hand;
            this.button_github.FlatAppearance.BorderSize = 0;
            this.button_github.FlatAppearance.MouseDownBackColor = Color.FromArgb(199, 207, 215);
            this.button_github.FlatAppearance.MouseOverBackColor = Color.FromArgb(216, 222, 228);
            this.button_github.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button_github.ForeColor = Color.FromArgb(38, 50, 56);
            this.button_github.Margin = new System.Windows.Forms.Padding(4);
            this.button_github.Name = "button_github";
            this.button_github.Size = new System.Drawing.Size(104, 38);
            this.button_github.TabIndex = 18;
            this.button_github.Text = "文档";
            this.button_github.UseVisualStyleBackColor = false;
            //
            // button_get_model_info
            //
            this.button_get_model_info.BackColor = Color.FromArgb(231, 235, 239);
            this.button_get_model_info.Cursor = System.Windows.Forms.Cursors.Hand;
            this.button_get_model_info.FlatAppearance.BorderSize = 0;
            this.button_get_model_info.FlatAppearance.MouseDownBackColor = Color.FromArgb(199, 207, 215);
            this.button_get_model_info.FlatAppearance.MouseOverBackColor = Color.FromArgb(216, 222, 228);
            this.button_get_model_info.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.button_get_model_info.ForeColor = Color.FromArgb(38, 50, 56);
            this.button_get_model_info.Margin = new System.Windows.Forms.Padding(4);
            this.button_get_model_info.Name = "button_get_model_info";
            this.button_get_model_info.Size = new System.Drawing.Size(104, 38);
            this.button_get_model_info.TabIndex = 19;
            this.button_get_model_info.Text = "获取模型信息";
            this.button_get_model_info.UseVisualStyleBackColor = false;
            //
            // splitContainerMain
            //
            this.splitContainerMain.BackColor = Color.FromArgb(243, 245, 247);
            this.splitContainerMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainerMain.FixedPanel = System.Windows.Forms.FixedPanel.Panel1;
            this.splitContainerMain.Name = "splitContainerMain";
            this.splitContainerMain.Orientation = System.Windows.Forms.Orientation.Vertical;
            this.splitContainerMain.Panel1.BackColor = Color.FromArgb(243, 245, 247);
            this.splitContainerMain.Panel1MinSize = 240;
            this.splitContainerMain.Panel2.BackColor = Color.FromArgb(243, 245, 247);
            this.splitContainerMain.Panel2MinSize = 360;
            this.splitContainerMain.Size = new System.Drawing.Size(900, 560);
            this.splitContainerMain.SplitterDistance = 380;
            this.splitContainerMain.SplitterWidth = 6;
            this.splitContainerMain.TabIndex = 20;
            //
            // resultCard
            //
            this.resultCard.BackColor = Color.White;
            this.resultCard.Dock = System.Windows.Forms.DockStyle.Fill;
            this.resultCard.Margin = new System.Windows.Forms.Padding(6);
            this.resultCard.Name = "resultCard";
            this.resultCard.Padding = new System.Windows.Forms.Padding(12);
            //
            // resultLayout
            //
            this.resultLayout.BackColor = Color.White;
            this.resultLayout.ColumnCount = 1;
            this.resultLayout.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.resultLayout.Dock = System.Windows.Forms.DockStyle.Fill;
            this.resultLayout.Name = "resultLayout";
            this.resultLayout.RowCount = 2;
            this.resultLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize));
            this.resultLayout.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            //
            // label_result
            //
            this.label_result.AutoSize = true;
            this.label_result.BackColor = Color.White;
            this.label_result.Font = new System.Drawing.Font("Microsoft YaHei UI", 11.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.label_result.ForeColor = Color.FromArgb(55, 71, 79);
            this.label_result.Margin = new System.Windows.Forms.Padding(2, 0, 0, 8);
            this.label_result.Name = "label_result";
            this.label_result.Text = "运行结果";
            //
            // richTextBox1
            //
            this.richTextBox1.AccessibleName = "运行结果输出区";
            this.richTextBox1.AcceptsReturn = true;
            this.richTextBox1.BackColor = Color.FromArgb(250, 251, 252);
            this.richTextBox1.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.richTextBox1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.richTextBox1.Font = new System.Drawing.Font("Consolas", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.richTextBox1.Multiline = true;
            this.richTextBox1.Name = "richTextBox1";
            this.richTextBox1.ReadOnly = true;
            this.richTextBox1.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.richTextBox1.TabIndex = 21;
            this.richTextBox1.WordWrap = true;
            //
            // imageCard
            //
            this.imageCard.BackColor = Color.White;
            this.imageCard.Dock = System.Windows.Forms.DockStyle.Fill;
            this.imageCard.Margin = new System.Windows.Forms.Padding(6);
            this.imageCard.Name = "imageCard";
            this.imageCard.Padding = new System.Windows.Forms.Padding(6);
            //
            // imagePanel1
            //
            this.imagePanel1.AccessibleName = "图像显示区";
            this.imagePanel1.BackColor = Color.FromArgb(32, 36, 40);
            this.imagePanel1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.imagePanel1.MaxScale = 100F;
            this.imagePanel1.MinScale = 0.5F;
            this.imagePanel1.Name = "imagePanel1";
            this.imagePanel1.ShowStatusText = false;
            this.imagePanel1.ShowVisualization = true;
            this.imagePanel1.TabIndex = 22;
            //
            // 左侧操作区
            //
            this.leftOpsTable.Controls.Add(this.button_load_model, 0, 0);
            this.leftOpsTable.SetColumnSpan(this.button_load_model, 2);
            this.leftOpsTable.Controls.Add(this.button_infer, 0, 1);
            this.leftOpsTable.Controls.Add(this.button_infer_json, 1, 1);
            this.leftOpsTable.Controls.Add(this.button_thread_test, 0, 2);
            this.leftOpsTable.Controls.Add(this.button_consistency_test, 1, 2);
            //
            // 中间参数区
            //
            this.paramsTable.Controls.Add(this.label_choose_gpu, 0, 0);
            this.paramsTable.Controls.Add(this.comboBox1, 1, 0);
            this.paramsTable.SetColumnSpan(this.comboBox1, 4);
            this.paramsTable.Controls.Add(this.label_batch_size, 0, 1);
            this.paramsTable.Controls.Add(this.numericUpDown_batch_size, 1, 1);
            this.paramsTable.Controls.Add(this.label_threshold, 2, 1);
            this.paramsTable.Controls.Add(this.numericUpDown_threshold, 3, 1);
            this.paramsTable.Controls.Add(this.label_num_thread, 0, 2);
            this.paramsTable.Controls.Add(this.numericUpDown_num_thread, 1, 2);
            this.paramsTable.Controls.Add(this.checkBox_calc_mean, 2, 2);
            this.paramsTable.SetColumnSpan(this.checkBox_calc_mean, 2);
            //
            // 右侧操作区
            //
            this.rightOpsTable.Controls.Add(this.checkBox_rpc_mode, 0, 0);
            this.rightOpsTable.Controls.Add(this.button_open_image, 1, 0);
            this.rightOpsTable.SetColumnSpan(this.button_open_image, 3);
            this.rightOpsTable.Controls.Add(this.button_save_img, 1, 1);
            this.rightOpsTable.Controls.Add(this.button_free_model, 2, 1);
            this.rightOpsTable.Controls.Add(this.button_free_all_model, 3, 1);
            this.rightOpsTable.Controls.Add(this.button_check_environment, 0, 2);
            this.rightOpsTable.Controls.Add(this.button_check_dog, 1, 2);
            this.rightOpsTable.Controls.Add(this.button_github, 2, 2);
            this.rightOpsTable.Controls.Add(this.button_get_model_info, 3, 2);
            //
            // topLayout
            //
            this.topLayout.Controls.Add(this.leftOpsTable, 0, 0);
            this.topLayout.Controls.Add(this.paramsTable, 2, 0);
            this.topLayout.Controls.Add(this.rightOpsTable, 4, 0);
            //
            // topCard
            //
            this.topCard.Controls.Add(this.topLayout);
            //
            // resultLayout
            //
            this.resultLayout.Controls.Add(this.label_result, 0, 0);
            this.resultLayout.Controls.Add(this.richTextBox1, 0, 1);
            //
            // resultCard
            //
            this.resultCard.Controls.Add(this.resultLayout);
            //
            // imageCard
            //
            this.imageCard.Controls.Add(this.imagePanel1);
            //
            // splitContainerMain
            //
            this.splitContainerMain.Panel1.Controls.Add(this.resultCard);
            this.splitContainerMain.Panel2.Controls.Add(this.imageCard);
            //
            // rootLayout
            //
            this.rootLayout.Controls.Add(this.topCard, 0, 0);
            this.rootLayout.Controls.Add(this.splitContainerMain, 0, 1);
            //
            // MainWindow
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.BackColor = Color.FromArgb(243, 245, 247);
            this.Size = new System.Drawing.Size(1280, 820);
            this.Controls.Add(this.rootLayout);
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.MinimumSize = new System.Drawing.Size(1040, 500);
            this.Name = "MainWindow";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "C# 测试程序";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.Form1_FormClosing);
            this.Load += new System.EventHandler(this.Form1_Load);
            this.button_load_model.Click += new System.EventHandler(this.button_loadmodel_Click);
            this.button_infer.Click += new System.EventHandler(this.button_infer_Click);
            this.button_infer_json.Click += new System.EventHandler(this.button_infer_json_Click);
            this.button_thread_test.Click += new System.EventHandler(this.button_threadtest_Click);
            this.button_consistency_test.Click += new System.EventHandler(this.button_consistency_test_Click);
            this.button_open_image.Click += new System.EventHandler(this.button_openimage_Click);
            this.button_save_img.Click += new System.EventHandler(this.button_save_img_Click);
            this.button_free_model.Click += new System.EventHandler(this.button_freemodel_Click);
            this.button_free_all_model.Click += new System.EventHandler(this.button_free_all_model_Click);
            this.button_check_environment.Click += new System.EventHandler(this.button_check_environment_Click);
            this.button_check_dog.Click += new System.EventHandler(this.button1_Click);
            this.button_github.Click += new System.EventHandler(this.button_github_Click);
            this.button_get_model_info.Click += new System.EventHandler(this.button_getmodelinfo_Click);
            this.numericUpDown_threshold.ValueChanged += new System.EventHandler(this.numericUpDown_threshold_ValueChanged);
            this.checkBox_calc_mean.CheckStateChanged += new System.EventHandler(this.checkBox_calc_mean_StateChanged);
            this.resultLayout.ResumeLayout(false);
            this.resultLayout.PerformLayout();
            this.imageCard.ResumeLayout(false);
            this.resultCard.ResumeLayout(false);
            this.resultCard.PerformLayout();
            this.splitContainerMain.Panel1.ResumeLayout(false);
            this.splitContainerMain.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainerMain)).EndInit();
            this.splitContainerMain.ResumeLayout(false);
            this.rootLayout.ResumeLayout(false);
            this.topCard.ResumeLayout(false);
            this.topLayout.ResumeLayout(false);
            this.leftOpsTable.ResumeLayout(false);
            this.paramsTable.ResumeLayout(false);
            this.rightOpsTable.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown_batch_size)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown_threshold)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown_num_thread)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
            this.CreateResultTextBoxContextMenu();
        }

        #endregion

        /// <summary>
        /// 生成运行结果区右键菜单，保留撤销/剪切/复制/粘贴/删除/全选，并附加自动换行开关。
        /// 只读文本框下仅保留复制与全选等可用操作。
        /// </summary>
        private void CreateResultTextBoxContextMenu()
        {
            System.Windows.Forms.ContextMenuStrip resultMenu = new System.Windows.Forms.ContextMenuStrip(this.components);
            System.Windows.Forms.ToolStripMenuItem undoItem = new System.Windows.Forms.ToolStripMenuItem("撤销");
            System.Windows.Forms.ToolStripMenuItem cutItem = new System.Windows.Forms.ToolStripMenuItem("剪切");
            System.Windows.Forms.ToolStripMenuItem copyItem = new System.Windows.Forms.ToolStripMenuItem("复制");
            System.Windows.Forms.ToolStripMenuItem pasteItem = new System.Windows.Forms.ToolStripMenuItem("粘贴");
            System.Windows.Forms.ToolStripMenuItem deleteItem = new System.Windows.Forms.ToolStripMenuItem("删除");
            System.Windows.Forms.ToolStripMenuItem selectAllItem = new System.Windows.Forms.ToolStripMenuItem("全选");
            System.Windows.Forms.ToolStripMenuItem wordWrapItem = new System.Windows.Forms.ToolStripMenuItem("自动换行");

            undoItem.Click += delegate
            {
                if (this.richTextBox1.CanUndo)
                {
                    this.richTextBox1.Undo();
                }
            };
            cutItem.Click += delegate
            {
                if (!this.richTextBox1.ReadOnly && this.richTextBox1.SelectionLength > 0)
                {
                    this.richTextBox1.Cut();
                }
            };
            copyItem.Click += delegate
            {
                if (this.richTextBox1.SelectionLength > 0)
                {
                    this.richTextBox1.Copy();
                }
            };
            pasteItem.Click += delegate
            {
                if (!this.richTextBox1.ReadOnly && ResultTextBoxClipboardHasText())
                {
                    this.richTextBox1.Paste();
                }
            };
            deleteItem.Click += delegate
            {
                if (!this.richTextBox1.ReadOnly && this.richTextBox1.SelectionLength > 0)
                {
                    this.richTextBox1.SelectedText = string.Empty;
                }
            };
            selectAllItem.Click += delegate
            {
                if (this.richTextBox1.TextLength > 0)
                {
                    this.richTextBox1.SelectAll();
                }
            };

            wordWrapItem.CheckOnClick = true;
            wordWrapItem.Checked = this.richTextBox1.WordWrap;
            wordWrapItem.Click += new System.EventHandler(this.resultTextWordWrapMenuItem_Click);

            resultMenu.Opening += delegate
            {
                bool hasSelection = this.richTextBox1.SelectionLength > 0;
                bool canEdit = !this.richTextBox1.ReadOnly;
                undoItem.Enabled = canEdit && this.richTextBox1.CanUndo;
                cutItem.Enabled = canEdit && hasSelection;
                copyItem.Enabled = hasSelection;
                pasteItem.Enabled = canEdit && ResultTextBoxClipboardHasText();
                deleteItem.Enabled = canEdit && hasSelection;
                selectAllItem.Enabled = this.richTextBox1.TextLength > 0;
                wordWrapItem.Checked = this.richTextBox1.WordWrap;
            };

            resultMenu.Items.Add(undoItem);
            resultMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            resultMenu.Items.Add(cutItem);
            resultMenu.Items.Add(copyItem);
            resultMenu.Items.Add(pasteItem);
            resultMenu.Items.Add(deleteItem);
            resultMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            resultMenu.Items.Add(selectAllItem);
            resultMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            resultMenu.Items.Add(wordWrapItem);

            this.richTextBox1.ContextMenuStrip = resultMenu;
        }

        /// <summary>
        /// 判断剪贴板是否包含文本内容。
        /// </summary>
        private static bool ResultTextBoxClipboardHasText()
        {
            try
            {
                return System.Windows.Forms.Clipboard.ContainsText();
            }
            catch
            {
                return false;
            }
        }

        private System.Windows.Forms.TableLayoutPanel rootLayout;
        private System.Windows.Forms.Panel topCard;
        private System.Windows.Forms.TableLayoutPanel topLayout;
        private System.Windows.Forms.TableLayoutPanel leftOpsTable;
        private System.Windows.Forms.Button button_load_model;
        private System.Windows.Forms.Button button_infer;
        private System.Windows.Forms.Button button_infer_json;
        private System.Windows.Forms.Button button_thread_test;
        private System.Windows.Forms.Button button_consistency_test;
        private System.Windows.Forms.TableLayoutPanel paramsTable;
        private System.Windows.Forms.Label label_choose_gpu;
        private System.Windows.Forms.ComboBox comboBox1;
        private System.Windows.Forms.Label label_batch_size;
        private System.Windows.Forms.NumericUpDown numericUpDown_batch_size;
        private System.Windows.Forms.Label label_threshold;
        private System.Windows.Forms.NumericUpDown numericUpDown_threshold;
        private System.Windows.Forms.Label label_num_thread;
        private System.Windows.Forms.NumericUpDown numericUpDown_num_thread;
        private System.Windows.Forms.CheckBox checkBox_calc_mean;
        private System.Windows.Forms.TableLayoutPanel rightOpsTable;
        private System.Windows.Forms.CheckBox checkBox_rpc_mode;
        private System.Windows.Forms.Button button_open_image;
        private System.Windows.Forms.Button button_save_img;
        private System.Windows.Forms.Button button_free_model;
        private System.Windows.Forms.Button button_free_all_model;
        private System.Windows.Forms.Button button_check_environment;
        private System.Windows.Forms.Button button_check_dog;
        private System.Windows.Forms.Button button_github;
        private System.Windows.Forms.Button button_get_model_info;
        private System.Windows.Forms.SplitContainer splitContainerMain;
        private System.Windows.Forms.Panel resultCard;
        private System.Windows.Forms.TableLayoutPanel resultLayout;
        private System.Windows.Forms.Label label_result;
        private System.Windows.Forms.TextBox richTextBox1;
        private System.Windows.Forms.Panel imageCard;
        private DLCV.ImageViewer imagePanel1;
    }
}
