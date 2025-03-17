namespace demo
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
            this.label_speed = new System.Windows.Forms.Label();
            this.button_free_model = new System.Windows.Forms.Button();
            this.button_github = new System.Windows.Forms.Button();
            this.button_doc = new System.Windows.Forms.Button();
            this.numericUpDown_num_thread = new System.Windows.Forms.NumericUpDown();
            this.comboBox1 = new System.Windows.Forms.ComboBox();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.numericUpDown_batch_size = new System.Windows.Forms.NumericUpDown();
            this.imagePanel1 = new demo.ImagePanel();
            this.richTextBox1 = new System.Windows.Forms.RichTextBox();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown_num_thread)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown_batch_size)).BeginInit();
            this.SuspendLayout();
            // 
            // button_load_model
            // 
            this.button_load_model.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.button_load_model.Location = new System.Drawing.Point(14, 19);
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
            this.button_get_model_info.Location = new System.Drawing.Point(553, 92);
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
            this.button_open_image.Location = new System.Drawing.Point(1103, 19);
            this.button_open_image.Name = "button_open_image";
            this.button_open_image.Size = new System.Drawing.Size(140, 60);
            this.button_open_image.TabIndex = 3;
            this.button_open_image.Text = "打开图片推理";
            this.button_open_image.UseVisualStyleBackColor = true;
            this.button_open_image.Click += new System.EventHandler(this.button_openimage_Click);
            // 
            // button_infer
            // 
            this.button_infer.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.button_infer.Location = new System.Drawing.Point(700, 92);
            this.button_infer.Name = "button_infer";
            this.button_infer.Size = new System.Drawing.Size(140, 60);
            this.button_infer.TabIndex = 4;
            this.button_infer.Text = "推理";
            this.button_infer.UseVisualStyleBackColor = true;
            this.button_infer.Click += new System.EventHandler(this.button_infer_Click);
            // 
            // button_thread_test
            // 
            this.button_thread_test.Location = new System.Drawing.Point(12, 92);
            this.button_thread_test.Name = "button_thread_test";
            this.button_thread_test.Size = new System.Drawing.Size(140, 60);
            this.button_thread_test.TabIndex = 5;
            this.button_thread_test.Text = "多线程测试";
            this.button_thread_test.UseVisualStyleBackColor = true;
            this.button_thread_test.Click += new System.EventHandler(this.button_threadtest_Click);
            // 
            // label_speed
            // 
            this.label_speed.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.label_speed.AutoSize = true;
            this.label_speed.Location = new System.Drawing.Point(12, 155);
            this.label_speed.Name = "label_speed";
            this.label_speed.Size = new System.Drawing.Size(46, 24);
            this.label_speed.TabIndex = 6;
            this.label_speed.Text = "测速";
            // 
            // button_free_model
            // 
            this.button_free_model.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.button_free_model.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.button_free_model.Location = new System.Drawing.Point(847, 92);
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
            this.button_github.Location = new System.Drawing.Point(995, 92);
            this.button_github.Margin = new System.Windows.Forms.Padding(4);
            this.button_github.Name = "button_github";
            this.button_github.Size = new System.Drawing.Size(120, 60);
            this.button_github.TabIndex = 9;
            this.button_github.Text = "文档";
            this.button_github.UseVisualStyleBackColor = true;
            this.button_github.Click += new System.EventHandler(this.button_github_Click);
            // 
            // button_doc
            // 
            this.button_doc.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.button_doc.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.button_doc.Location = new System.Drawing.Point(1123, 92);
            this.button_doc.Margin = new System.Windows.Forms.Padding(4);
            this.button_doc.Name = "button_doc";
            this.button_doc.Size = new System.Drawing.Size(120, 60);
            this.button_doc.TabIndex = 10;
            this.button_doc.Text = "论坛文档";
            this.button_doc.UseVisualStyleBackColor = true;
            this.button_doc.Click += new System.EventHandler(this.button_bbs_Click);
            // 
            // numericUpDown_num_thread
            // 
            this.numericUpDown_num_thread.Location = new System.Drawing.Point(231, 108);
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
            this.numericUpDown_num_thread.Size = new System.Drawing.Size(63, 31);
            this.numericUpDown_num_thread.TabIndex = 11;
            this.numericUpDown_num_thread.Value = new decimal(new int[] {
            4,
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
            this.comboBox1.Size = new System.Drawing.Size(564, 32);
            this.comboBox1.TabIndex = 12;
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(161, 37);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(82, 24);
            this.label1.TabIndex = 13;
            this.label1.Text = "选择显卡";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(161, 110);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(64, 24);
            this.label2.TabIndex = 14;
            this.label2.Text = "线程数";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(300, 110);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(99, 24);
            this.label3.TabIndex = 15;
            this.label3.Text = "batch_size";
            // 
            // numericUpDown_batch_size
            // 
            this.numericUpDown_batch_size.Location = new System.Drawing.Point(405, 108);
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
            this.numericUpDown_batch_size.Size = new System.Drawing.Size(63, 31);
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
            this.imagePanel1.Location = new System.Drawing.Point(304, 182);
            this.imagePanel1.MaxScale = 100F;
            this.imagePanel1.MinScale = 0.5F;
            this.imagePanel1.Name = "imagePanel1";
            this.imagePanel1.Size = new System.Drawing.Size(939, 649);
            this.imagePanel1.TabIndex = 17;
            // 
            // richTextBox1
            // 
            this.richTextBox1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.richTextBox1.Location = new System.Drawing.Point(12, 182);
            this.richTextBox1.Margin = new System.Windows.Forms.Padding(3, 3, 3, 16);
            this.richTextBox1.Name = "richTextBox1";
            this.richTextBox1.Size = new System.Drawing.Size(282, 649);
            this.richTextBox1.TabIndex = 7;
            this.richTextBox1.Text = "";
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(11F, 24F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1258, 844);
            this.Controls.Add(this.imagePanel1);
            this.Controls.Add(this.numericUpDown_batch_size);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.comboBox1);
            this.Controls.Add(this.numericUpDown_num_thread);
            this.Controls.Add(this.button_doc);
            this.Controls.Add(this.button_github);
            this.Controls.Add(this.button_free_model);
            this.Controls.Add(this.richTextBox1);
            this.Controls.Add(this.label_speed);
            this.Controls.Add(this.button_thread_test);
            this.Controls.Add(this.button_infer);
            this.Controls.Add(this.button_open_image);
            this.Controls.Add(this.button_get_model_info);
            this.Controls.Add(this.button_load_model);
            this.Font = new System.Drawing.Font("微软雅黑", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Margin = new System.Windows.Forms.Padding(4);
            this.MinimumSize = new System.Drawing.Size(1280, 900);
            this.Name = "Form1";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "深度视觉部署SDK C# 测试程序";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.Form1_FormClosing);
            this.Load += new System.EventHandler(this.Form1_Load);
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown_num_thread)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numericUpDown_batch_size)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.Button button_load_model;
        private System.Windows.Forms.Button button_get_model_info;
        private System.Windows.Forms.Button button_open_image;
        private System.Windows.Forms.Button button_infer;
        private System.Windows.Forms.Button button_thread_test;
        private System.Windows.Forms.Label label_speed;
        private System.Windows.Forms.Button button_free_model;
        private System.Windows.Forms.Button button_github;
        private System.Windows.Forms.Button button_doc;
        private System.Windows.Forms.NumericUpDown numericUpDown_num_thread;
        private System.Windows.Forms.ComboBox comboBox1;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.NumericUpDown numericUpDown_batch_size;
        private demo.ImagePanel imagePanel1;
        private System.Windows.Forms.RichTextBox richTextBox1;
    }
}

