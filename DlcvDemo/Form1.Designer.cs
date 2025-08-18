namespace DlcvDemo
{
    partial class Form1
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
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            this.button_load_model = new System.Windows.Forms.Button();
            this.button_get_model_info = new System.Windows.Forms.Button();
            this.button_open_image = new System.Windows.Forms.Button();
            this.button_infer = new System.Windows.Forms.Button();
            this.button_thread_test = new System.Windows.Forms.Button();
            this.button_consistency_test = new System.Windows.Forms.Button();
            this.button_free_model = new System.Windows.Forms.Button();
            this.button_github = new System.Windows.Forms.Button();
            this.numericUpDown_num_thread = new System.Windows.Forms.NumericUpDown();
            this.comboBox1 = new System.Windows.Forms.ComboBox();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.numericUpDown_batch_size = new System.Windows.Forms.NumericUpDown();
            this.imagePanel1 = new DLCV.ImageViewer();
            this.richTextBox1 = new System.Windows.Forms.RichTextBox();
            this.label4 = new System.Windows.Forms.Label();
            this.numericUpDown_threshold = new System.Windows.Forms.NumericUpDown();
            this.button_check_dog = new System.Windows.Forms.Button();
            this.button_load_sliding_window_model = new System.Windows.Forms.Button();
            this.button_free_all_model = new System.Windows.Forms.Button();
            this.button_load_ocr_model = new System.Windows.Forms.Button();
            this.checkBox_rpc_mode = new System.Windows.Forms.CheckBox();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown_num_thread)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown_batch_size)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown_threshold)).BeginInit();
            this.SuspendLayout();
            // 
            // button_load_model
            // 
            this.button_load_model.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.button_load_model.Location = new System.Drawing.Point(12, 19);
            this.button_load_model.Margin = new System.Windows.Forms.Padding(4);
            this.button_load_model.Name = "button_load_model";
            this.button_load_model.Size = new System.Drawing.Size(140, 60);
            this.button_load_model.TabIndex = 1;
            this.button_load_model.Text = "加载模型";
            this.button_load_model.UseVisualStyleBackColor = true;
            this.button_load_model.Click += new System.EventHandler(this.button_loadmodel_Click);
            // 
            // button_get_model_info
            // 
            this.button_get_model_info.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.button_get_model_info.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.button_get_model_info.Location = new System.Drawing.Point(935, 161);
            this.button_get_model_info.Margin = new System.Windows.Forms.Padding(4);
            this.button_get_model_info.Name = "button_get_model_info";
            this.button_get_model_info.Size = new System.Drawing.Size(140, 60);
            this.button_get_model_info.TabIndex = 2;
            this.button_get_model_info.Text = "获取模型信息";
            this.button_get_model_info.UseVisualStyleBackColor = true;
            this.button_get_model_info.Click += new System.EventHandler(this.button_getmodelinfo_Click);
            // 
            // button_open_image
            // 
            this.button_open_image.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.button_open_image.Location = new System.Drawing.Point(1083, 19);
            this.button_open_image.Name = "button_open_image";
            this.button_open_image.Size = new System.Drawing.Size(140, 60);
            this.button_open_image.TabIndex = 3;
            this.button_open_image.Text = "打开图片推理";
            this.button_open_image.UseVisualStyleBackColor = true;
            this.button_open_image.Click += new System.EventHandler(this.button_openimage_Click);
            // 
            // button_infer
            // 
            this.button_infer.Location = new System.Drawing.Point(311, 87);
            this.button_infer.Name = "button_infer";
            this.button_infer.Size = new System.Drawing.Size(140, 60);
            this.button_infer.TabIndex = 4;
            this.button_infer.Text = "单次推理";
            this.button_infer.UseVisualStyleBackColor = true;
            this.button_infer.Click += new System.EventHandler(this.button_infer_Click);
            // 
            // button_thread_test
            // 
            this.button_thread_test.Location = new System.Drawing.Point(12, 154);
            this.button_thread_test.Name = "button_thread_test";
            this.button_thread_test.Size = new System.Drawing.Size(140, 60);
            this.button_thread_test.TabIndex = 5;
            this.button_thread_test.Text = "多线程测试";
            this.button_thread_test.UseVisualStyleBackColor = true;
            this.button_thread_test.Click += new System.EventHandler(this.button_threadtest_Click);
            // 
            // button_consistency_test
            // 
            this.button_consistency_test.Location = new System.Drawing.Point(165, 154);
            this.button_consistency_test.Name = "button_consistency_test";
            this.button_consistency_test.Size = new System.Drawing.Size(140, 60);
            this.button_consistency_test.TabIndex = 23;
            this.button_consistency_test.Text = "一致性测试";
            this.button_consistency_test.UseVisualStyleBackColor = true;
            this.button_consistency_test.Click += new System.EventHandler(this.button_consistency_test_Click);
            // 
            // button_free_model
            // 
            this.button_free_model.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.button_free_model.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.button_free_model.Location = new System.Drawing.Point(1083, 93);
            this.button_free_model.Margin = new System.Windows.Forms.Padding(4);
            this.button_free_model.Name = "button_free_model";
            this.button_free_model.Size = new System.Drawing.Size(140, 60);
            this.button_free_model.TabIndex = 8;
            this.button_free_model.Text = "释放模型";
            this.button_free_model.UseVisualStyleBackColor = true;
            this.button_free_model.Click += new System.EventHandler(this.button_freemodel_Click);
            // 
            // button_github
            // 
            this.button_github.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.button_github.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.button_github.Location = new System.Drawing.Point(787, 161);
            this.button_github.Margin = new System.Windows.Forms.Padding(4);
            this.button_github.Name = "button_github";
            this.button_github.Size = new System.Drawing.Size(140, 60);
            this.button_github.TabIndex = 9;
            this.button_github.Text = "文档";
            this.button_github.UseVisualStyleBackColor = true;
            this.button_github.Click += new System.EventHandler(this.button_github_Click);
            // 
            // numericUpDown_num_thread
            // 
            this.numericUpDown_num_thread.Location = new System.Drawing.Point(388, 170);
            this.numericUpDown_num_thread.Maximum = new decimal(new int[] {
            32,
            0,
            0,
            0});
            this.numericUpDown_num_thread.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numericUpDown_num_thread.Name = "numericUpDown_num_thread";
            this.numericUpDown_num_thread.Size = new System.Drawing.Size(83, 39);
            this.numericUpDown_num_thread.TabIndex = 11;
            this.numericUpDown_num_thread.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
            // 
            // comboBox1
            // 
            this.comboBox1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.comboBox1.FormattingEnabled = true;
            this.comboBox1.Location = new System.Drawing.Point(249, 34);
            this.comboBox1.Name = "comboBox1";
            this.comboBox1.Size = new System.Drawing.Size(760, 39);
            this.comboBox1.TabIndex = 12;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(161, 37);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(110, 31);
            this.label1.TabIndex = 13;
            this.label1.Text = "选择显卡";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(318, 172);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(86, 31);
            this.label2.TabIndex = 14;
            this.label2.Text = "线程数";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(640, 105);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(132, 31);
            this.label3.TabIndex = 15;
            this.label3.Text = "batch_size";
            // 
            // numericUpDown_batch_size
            // 
            this.numericUpDown_batch_size.Location = new System.Drawing.Point(745, 103);
            this.numericUpDown_batch_size.Maximum = new decimal(new int[] {
            1024,
            0,
            0,
            0});
            this.numericUpDown_batch_size.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numericUpDown_batch_size.Name = "numericUpDown_batch_size";
            this.numericUpDown_batch_size.Size = new System.Drawing.Size(63, 39);
            this.numericUpDown_batch_size.TabIndex = 16;
            this.numericUpDown_batch_size.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
            // 
            // imagePanel1
            // 
            this.imagePanel1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.imagePanel1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.imagePanel1.image = null;
            this.imagePanel1.Location = new System.Drawing.Point(441, 228);
            this.imagePanel1.MaxScale = 100F;
            this.imagePanel1.MinScale = 0.5F;
            this.imagePanel1.Name = "imagePanel1";
            this.imagePanel1.ShowStatusText = false;
            this.imagePanel1.Size = new System.Drawing.Size(782, 604);
            this.imagePanel1.TabIndex = 17;
            // 
            // richTextBox1
            // 
            this.richTextBox1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.richTextBox1.Location = new System.Drawing.Point(12, 228);
            this.richTextBox1.Margin = new System.Windows.Forms.Padding(3, 3, 3, 16);
            this.richTextBox1.Name = "richTextBox1";
            this.richTextBox1.Size = new System.Drawing.Size(423, 604);
            this.richTextBox1.TabIndex = 7;
            this.richTextBox1.Text = "";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(457, 105);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(123, 31);
            this.label4.TabIndex = 18;
            this.label4.Text = "threshold";
            // 
            // numericUpDown_threshold
            // 
            this.numericUpDown_threshold.DecimalPlaces = 2;
            this.numericUpDown_threshold.Increment = new decimal(new int[] {
            5,
            0,
            0,
            131072});
            this.numericUpDown_threshold.Location = new System.Drawing.Point(555, 103);
            this.numericUpDown_threshold.Maximum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.numericUpDown_threshold.Name = "numericUpDown_threshold";
            this.numericUpDown_threshold.Size = new System.Drawing.Size(79, 39);
            this.numericUpDown_threshold.TabIndex = 19;
            this.numericUpDown_threshold.Value = new decimal(new int[] {
            5,
            0,
            0,
            65536});
            // 
            // button_check_dog
            // 
            this.button_check_dog.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.button_check_dog.Location = new System.Drawing.Point(935, 93);
            this.button_check_dog.Name = "button_check_dog";
            this.button_check_dog.Size = new System.Drawing.Size(140, 60);
            this.button_check_dog.TabIndex = 20;
            this.button_check_dog.Text = "检查加密狗";
            this.button_check_dog.UseVisualStyleBackColor = true;
            this.button_check_dog.Click += new System.EventHandler(this.button1_Click);
            // 
            // button_load_sliding_window_model
            // 
            this.button_load_sliding_window_model.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.button_load_sliding_window_model.Location = new System.Drawing.Point(165, 87);
            this.button_load_sliding_window_model.Margin = new System.Windows.Forms.Padding(4);
            this.button_load_sliding_window_model.Name = "button_load_sliding_window_model";
            this.button_load_sliding_window_model.Size = new System.Drawing.Size(140, 60);
            this.button_load_sliding_window_model.TabIndex = 21;
            this.button_load_sliding_window_model.Text = "加载\r\n滑窗裁图模型";
            this.button_load_sliding_window_model.UseVisualStyleBackColor = true;
            this.button_load_sliding_window_model.Click += new System.EventHandler(this.button_load_sliding_window_model_Click);
            // 
            // button_free_all_model
            // 
            this.button_free_all_model.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.button_free_all_model.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.button_free_all_model.Location = new System.Drawing.Point(1083, 161);
            this.button_free_all_model.Margin = new System.Windows.Forms.Padding(4);
            this.button_free_all_model.Name = "button_free_all_model";
            this.button_free_all_model.Size = new System.Drawing.Size(140, 60);
            this.button_free_all_model.TabIndex = 22;
            this.button_free_all_model.Text = "释放所有模型";
            this.button_free_all_model.UseVisualStyleBackColor = true;
            this.button_free_all_model.Click += new System.EventHandler(this.button_free_all_model_Click);
            // 
            // button_load_ocr_model
            // 
            this.button_load_ocr_model.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.button_load_ocr_model.Location = new System.Drawing.Point(12, 87);
            this.button_load_ocr_model.Margin = new System.Windows.Forms.Padding(4);
            this.button_load_ocr_model.Name = "button_load_ocr_model";
            this.button_load_ocr_model.Size = new System.Drawing.Size(140, 60);
            this.button_load_ocr_model.TabIndex = 23;
            this.button_load_ocr_model.Text = "加载OCR模型";
            this.button_load_ocr_model.UseVisualStyleBackColor = true;
            this.button_load_ocr_model.Click += new System.EventHandler(this.button_load_ocr_model_Click);
            // 
            // checkBox_rpc_mode
            // 
            this.checkBox_rpc_mode.AutoSize = true;
            this.checkBox_rpc_mode.Location = new System.Drawing.Point(493, 171);
            this.checkBox_rpc_mode.Name = "checkBox_rpc_mode";
            this.checkBox_rpc_mode.Size = new System.Drawing.Size(191, 35);
            this.checkBox_rpc_mode.TabIndex = 24;
            this.checkBox_rpc_mode.Text = "RPC模式";
            this.checkBox_rpc_mode.UseVisualStyleBackColor = true;
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(14F, 31F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1238, 844);
            this.Controls.Add(this.checkBox_rpc_mode);
            this.Controls.Add(this.button_load_ocr_model);
            this.Controls.Add(this.button_free_all_model);
            this.Controls.Add(this.button_load_sliding_window_model);
            this.Controls.Add(this.button_check_dog);
            this.Controls.Add(this.numericUpDown_threshold);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.imagePanel1);
            this.Controls.Add(this.numericUpDown_batch_size);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.comboBox1);
            this.Controls.Add(this.numericUpDown_num_thread);
            this.Controls.Add(this.button_github);
            this.Controls.Add(this.button_free_model);
            this.Controls.Add(this.richTextBox1);
            this.Controls.Add(this.button_thread_test);
            this.Controls.Add(this.button_consistency_test);
            this.Controls.Add(this.button_infer);
            this.Controls.Add(this.button_open_image);
            this.Controls.Add(this.button_get_model_info);
            this.Controls.Add(this.button_load_model);
            this.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(4);
            this.MinimumSize = new System.Drawing.Size(1200, 900);
            this.Name = "Form1";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "C# 测试程序";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.Form1_FormClosing);
            this.Load += new System.EventHandler(this.Form1_Load);
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown_num_thread)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown_batch_size)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown_threshold)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.Button button_load_model;
        private System.Windows.Forms.Button button_get_model_info;
        private System.Windows.Forms.Button button_open_image;
        private System.Windows.Forms.Button button_infer;
        private System.Windows.Forms.Button button_thread_test;
        private System.Windows.Forms.Button button_consistency_test;
        private System.Windows.Forms.Button button_free_model;
        private System.Windows.Forms.Button button_github;
        private System.Windows.Forms.NumericUpDown numericUpDown_num_thread;
        private System.Windows.Forms.ComboBox comboBox1;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.NumericUpDown numericUpDown_batch_size;
        private DLCV.ImageViewer imagePanel1;
        private System.Windows.Forms.RichTextBox richTextBox1;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.NumericUpDown numericUpDown_threshold;
        private System.Windows.Forms.Button button_check_dog;
        private System.Windows.Forms.Button button_load_sliding_window_model;
        private System.Windows.Forms.Button button_free_all_model;
        private System.Windows.Forms.Button button_load_ocr_model;
        private System.Windows.Forms.CheckBox checkBox_rpc_mode;
    }
}

