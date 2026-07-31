// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

namespace LibreHardwareMonitor.UI
{
    partial class InterfacePortForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(InterfacePortForm));
            this.portOKButton = new System.Windows.Forms.Button();
            this.portCancelButton = new System.Windows.Forms.Button();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.webServerLinkLabel = new System.Windows.Forms.LinkLabel();
            this.portNumericUpDn = new System.Windows.Forms.NumericUpDown();
            this.label5 = new System.Windows.Forms.Label();
            this.interfaceLabel = new System.Windows.Forms.Label();
            this.interfaceComboBox = new System.Windows.Forms.ComboBox();
            ((System.ComponentModel.ISupportInitialize)(this.portNumericUpDn)).BeginInit();
            this.SuspendLayout();
            // 
            // portOKButton
            // 
            this.portOKButton.Location = new System.Drawing.Point(244, 162);
            this.portOKButton.Name = "portOKButton";
            this.portOKButton.Size = new System.Drawing.Size(75, 23);
            this.portOKButton.TabIndex = 0;
            this.portOKButton.Text = "确定";
            this.portOKButton.UseVisualStyleBackColor = true;
            this.portOKButton.Click += new System.EventHandler(this.PortOKButton_Click);
            // 
            // portCancelButton
            // 
            this.portCancelButton.DialogResult = System.Windows.Forms.DialogResult.Cancel;
            this.portCancelButton.Location = new System.Drawing.Point(162, 162);
            this.portCancelButton.Name = "portCancelButton";
            this.portCancelButton.Size = new System.Drawing.Size(75, 23);
            this.portCancelButton.TabIndex = 1;
            this.portCancelButton.Text = "取消";
            this.portCancelButton.UseVisualStyleBackColor = true;
            this.portCancelButton.Click += new System.EventHandler(this.PortCancelButton_Click);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(13, 131);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(377, 13);
            this.label1.TabIndex = 3;
            this.label1.Text = "注意：需要在操作系统防火墙中开放此端口。";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(13, 34);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(190, 13);
            this.label2.TabIndex = 4;
            this.label2.Text = "远程 Web 服务端口：";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Location = new System.Drawing.Point(13, 64);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(443, 13);
            this.label3.TabIndex = 5;
            this.label3.Text = "如果 Web 服务正在运行，需要重启服务才能使端口更改生效。";
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(13, 86);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(262, 13);
            this.label4.TabIndex = 6;
            this.label4.Text = "可在浏览器中通过以下地址访问 Web 服务：";
            // 
            // webServerLinkLabel
            // 
            this.webServerLinkLabel.AutoSize = true;
            this.webServerLinkLabel.Location = new System.Drawing.Point(269, 86);
            this.webServerLinkLabel.Name = "webServerLinkLabel";
            this.webServerLinkLabel.Size = new System.Drawing.Size(55, 13);
            this.webServerLinkLabel.TabIndex = 7;
            this.webServerLinkLabel.TabStop = true;
            this.webServerLinkLabel.Text = "linkLabel1";
            this.webServerLinkLabel.LinkClicked += new System.Windows.Forms.LinkLabelLinkClickedEventHandler(this.WebServerLinkLabel_LinkClicked);
            // 
            // portNumericUpDn
            // 
            this.portNumericUpDn.Location = new System.Drawing.Point(208, 32);
            this.portNumericUpDn.Maximum = new decimal(new int[] {
            20000,
            0,
            0,
            0});
            this.portNumericUpDn.Minimum = new decimal(new int[] {
            8080,
            0,
            0,
            0});
            this.portNumericUpDn.Name = "portNumericUpDn";
            this.portNumericUpDn.Size = new System.Drawing.Size(75, 20);
            this.portNumericUpDn.TabIndex = 8;
            this.portNumericUpDn.Value = new decimal(new int[] {
            8080,
            0,
            0,
            0});
            this.portNumericUpDn.ValueChanged += new System.EventHandler(this.PortNumericUpDn_ValueChanged);
            this.portNumericUpDn.KeyUp += new System.Windows.Forms.KeyEventHandler(this.PortNumericUpDn_KeyUp);
            // 
            // label5
            // 
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(13, 109);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(304, 13);
            this.label5.TabIndex = 9;
            this.label5.Text = "需要在菜单中单击“运行”来启动服务。";
            // 
            // interfaceLabel
            // 
            this.interfaceLabel.AutoSize = true;
            this.interfaceLabel.Location = new System.Drawing.Point(14, 8);
            this.interfaceLabel.Name = "interfaceLabel";
            this.interfaceLabel.Size = new System.Drawing.Size(217, 13);
            this.interfaceLabel.TabIndex = 10;
            this.interfaceLabel.Text = "远程 Web 服务网络接口：";
            // 
            // interfaceComboBox
            // 
            this.interfaceComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.interfaceComboBox.FormattingEnabled = true;
            this.interfaceComboBox.Location = new System.Drawing.Point(233, 6);
            this.interfaceComboBox.Margin = new System.Windows.Forms.Padding(2);
            this.interfaceComboBox.Name = "interfaceComboBox";
            this.interfaceComboBox.Size = new System.Drawing.Size(221, 21);
            this.interfaceComboBox.TabIndex = 11;
            this.interfaceComboBox.SelectedIndexChanged += new System.EventHandler(this.interfaceComboBox_SelectedIndexChanged);
            // 
            // InterfacePortForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.portCancelButton;
            this.ClientSize = new System.Drawing.Size(466, 202);
            this.Controls.Add(this.interfaceComboBox);
            this.Controls.Add(this.interfaceLabel);
            this.Controls.Add(this.label5);
            this.Controls.Add(this.portNumericUpDn);
            this.Controls.Add(this.webServerLinkLabel);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.portCancelButton);
            this.Controls.Add(this.portOKButton);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "InterfacePortForm";
            this.SizeGripStyle = System.Windows.Forms.SizeGripStyle.Hide;
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "设置接口和端口";
            this.Load += new System.EventHandler(this.PortForm_Load);
            ((System.ComponentModel.ISupportInitialize)(this.portNumericUpDn)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Button portOKButton;
        private System.Windows.Forms.Button portCancelButton;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.LinkLabel webServerLinkLabel;
        private System.Windows.Forms.NumericUpDown portNumericUpDn;
        private System.Windows.Forms.Label label5;
        private System.Windows.Forms.Label interfaceLabel;
        private System.Windows.Forms.ComboBox interfaceComboBox;
    }
}
