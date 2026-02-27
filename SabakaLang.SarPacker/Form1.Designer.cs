namespace SabakaLang.SarPacker;

partial class Form1
{
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    ///  Clean up any resources being used.
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
        rtbLog = new System.Windows.Forms.RichTextBox();
        txtSrc = new System.Windows.Forms.TextBox();
        txtOut = new System.Windows.Forms.TextBox();
        btnBuild = new System.Windows.Forms.Button();
        SuspendLayout();
        // 
        // rtbLog
        // 
        rtbLog.BackColor = System.Drawing.Color.FromArgb(((int)((byte)80)), ((int)((byte)80)), ((int)((byte)80)));
        rtbLog.BorderStyle = System.Windows.Forms.BorderStyle.None;
        rtbLog.ForeColor = System.Drawing.SystemColors.Window;
        rtbLog.Location = new System.Drawing.Point(12, 62);
        rtbLog.Name = "rtbLog";
        rtbLog.ReadOnly = true;
        rtbLog.Size = new System.Drawing.Size(680, 367);
        rtbLog.TabIndex = 0;
        rtbLog.Text = "";
        // 
        // txtSrc
        // 
        txtSrc.BackColor = System.Drawing.Color.FromArgb(((int)((byte)80)), ((int)((byte)80)), ((int)((byte)80)));
        txtSrc.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
        txtSrc.ForeColor = System.Drawing.SystemColors.Window;
        txtSrc.Location = new System.Drawing.Point(12, 12);
        txtSrc.Name = "txtSrc";
        txtSrc.PlaceholderText = "Enter source path";
        txtSrc.Size = new System.Drawing.Size(220, 23);
        txtSrc.TabIndex = 1;
        // 
        // txtOut
        // 
        txtOut.BackColor = System.Drawing.Color.FromArgb(((int)((byte)80)), ((int)((byte)80)), ((int)((byte)80)));
        txtOut.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
        txtOut.ForeColor = System.Drawing.SystemColors.Window;
        txtOut.Location = new System.Drawing.Point(238, 12);
        txtOut.Name = "txtOut";
        txtOut.PlaceholderText = "Enter output path";
        txtOut.Size = new System.Drawing.Size(220, 23);
        txtOut.TabIndex = 2;
        // 
        // btnBuild
        // 
        btnBuild.BackColor = System.Drawing.Color.Transparent;
        btnBuild.FlatAppearance.BorderSize = 0;
        btnBuild.ForeColor = System.Drawing.SystemColors.Desktop;
        btnBuild.Location = new System.Drawing.Point(465, 12);
        btnBuild.Name = "btnBuild";
        btnBuild.Size = new System.Drawing.Size(226, 22);
        btnBuild.TabIndex = 3;
        btnBuild.Text = "Build";
        btnBuild.UseVisualStyleBackColor = false;
        btnBuild.Click += BtnBuildOnClick;
        // 
        // Form1
        // 
        AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        BackColor = System.Drawing.Color.FromArgb(((int)((byte)56)), ((int)((byte)56)), ((int)((byte)56)));
        ClientSize = new System.Drawing.Size(704, 441);
        Controls.Add(btnBuild);
        Controls.Add(txtOut);
        Controls.Add(txtSrc);
        Controls.Add(rtbLog);
        Text = "SabakaLang SAR Packer Utility";
        ResumeLayout(false);
        PerformLayout();
    }

    private System.Windows.Forms.RichTextBox rtbLog;
    private System.Windows.Forms.TextBox txtSrc;
    private System.Windows.Forms.TextBox txtOut;
    private System.Windows.Forms.Button btnBuild;

    #endregion
}