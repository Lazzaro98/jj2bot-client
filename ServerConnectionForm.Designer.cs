namespace Jj2
{
    partial class ServerConnectionForm
    {
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Releases the controls owned by this connection window.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null)
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.serverLabel = new System.Windows.Forms.Label();
            this.connectionStatusLabel = new System.Windows.Forms.Label();
            this.chatLog = new System.Windows.Forms.RichTextBox();
            this.playerList = new System.Windows.Forms.RichTextBox();
            this.messageInput = new System.Windows.Forms.RichTextBox();
            this.sendButton = new Jj2Button();
            this.connectionToggleButton = new Jj2Button();
            this.SuspendLayout();
            //
            // serverLabel
            //
            this.serverLabel.AutoEllipsis = true;
            this.serverLabel.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Bold);
            this.serverLabel.Location = new System.Drawing.Point(12, 12);
            this.serverLabel.Name = "serverLabel";
            this.serverLabel.Size = new System.Drawing.Size(390, 18);
            this.serverLabel.TabIndex = 0;
            //
            // connectionStatusLabel
            //
            this.connectionStatusLabel.AutoSize = true;
            this.connectionStatusLabel.Location = new System.Drawing.Point(12, 34);
            this.connectionStatusLabel.Name = "connectionStatusLabel";
            this.connectionStatusLabel.Size = new System.Drawing.Size(69, 13);
            this.connectionStatusLabel.TabIndex = 1;
            this.connectionStatusLabel.Text = "Connecting...";
            //
            // chatLog
            //
            this.chatLog.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(32)))), ((int)(((byte)(32)))), ((int)(((byte)(32)))));
            this.chatLog.ForeColor = System.Drawing.Color.White;
            this.chatLog.HideSelection = false;
            this.chatLog.Location = new System.Drawing.Point(12, 58);
            this.chatLog.Name = "chatLog";
            this.chatLog.ReadOnly = true;
            this.chatLog.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.Vertical;
            this.chatLog.Size = new System.Drawing.Size(565, 258);
            this.chatLog.TabIndex = 2;
            this.chatLog.Text = "";
            //
            // playerList
            //
            this.playerList.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(32)))), ((int)(((byte)(32)))), ((int)(((byte)(32)))));
            this.playerList.ForeColor = System.Drawing.Color.White;
            this.playerList.Location = new System.Drawing.Point(583, 58);
            this.playerList.Name = "playerList";
            this.playerList.ReadOnly = true;
            this.playerList.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.Vertical;
            this.playerList.Size = new System.Drawing.Size(170, 258);
            this.playerList.TabIndex = 3;
            this.playerList.Text = "";
            //
            // messageInput
            //
            this.messageInput.Location = new System.Drawing.Point(12, 324);
            this.messageInput.Multiline = false;
            this.messageInput.Name = "messageInput";
            this.messageInput.Size = new System.Drawing.Size(565, 24);
            this.messageInput.TabIndex = 4;
            this.messageInput.Text = "";
            //
            // sendButton
            //
            this.sendButton.Location = new System.Drawing.Point(583, 324);
            this.sendButton.Name = "sendButton";
            this.sendButton.Size = new System.Drawing.Size(170, 24);
            this.sendButton.TabIndex = 5;
            this.sendButton.Text = "Send";
            this.sendButton.UseVisualStyleBackColor = true;
            this.sendButton.Click += new System.EventHandler(this.sendButton_Click);
            //
            // connectionToggleButton
            //
            this.connectionToggleButton.Location = new System.Drawing.Point(583, 12);
            this.connectionToggleButton.Name = "connectionToggleButton";
            this.connectionToggleButton.Size = new System.Drawing.Size(170, 30);
            this.connectionToggleButton.TabIndex = 6;
            this.connectionToggleButton.Text = "Disconnect";
            this.connectionToggleButton.UseVisualStyleBackColor = true;
            this.connectionToggleButton.Click += new System.EventHandler(this.connectionToggleButton_Click);
            //
            // ServerConnectionForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(765, 360);
            this.Controls.Add(this.connectionToggleButton);
            this.Controls.Add(this.sendButton);
            this.Controls.Add(this.messageInput);
            this.Controls.Add(this.playerList);
            this.Controls.Add(this.chatLog);
            this.Controls.Add(this.connectionStatusLabel);
            this.Controls.Add(this.serverLabel);
            this.MinimumSize = new System.Drawing.Size(781, 399);
            this.Name = "ServerConnectionForm";
            this.Shown += new System.EventHandler(this.ServerConnectionForm_Shown);
            this.FormClosed += new System.Windows.Forms.FormClosedEventHandler(this.ServerConnectionForm_FormClosed);
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label serverLabel;
        private System.Windows.Forms.Label connectionStatusLabel;
        private System.Windows.Forms.RichTextBox chatLog;
        private System.Windows.Forms.RichTextBox playerList;
        private System.Windows.Forms.RichTextBox messageInput;
        private Jj2Button sendButton;
        private Jj2Button connectionToggleButton;
    }
}
