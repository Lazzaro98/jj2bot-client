namespace Jj2
{
    partial class Form1
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
            this.serverListView = new System.Windows.Forms.ListView();
            this.serverNameColumn = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.serverAddressColumn = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.serverPlayersColumn = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.serverGameTypeColumn = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.refreshServersButton = new System.Windows.Forms.Button();
            this.serverStatusLabel = new System.Windows.Forms.Label();
            this.playerNameLabel = new System.Windows.Forms.Label();
            this.playerNameTextBox = new System.Windows.Forms.TextBox();
            this.connectSelectedServerButton = new System.Windows.Forms.Button();
            this.SuspendLayout();
            // serverListView
            //
            this.serverListView.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.serverNameColumn,
            this.serverAddressColumn,
            this.serverPlayersColumn,
            this.serverGameTypeColumn});
            this.serverListView.FullRowSelect = true;
            this.serverListView.GridLines = true;
            this.serverListView.HideSelection = false;
            this.serverListView.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(32)))), ((int)(((byte)(32)))), ((int)(((byte)(32)))));
            this.serverListView.ForeColor = System.Drawing.Color.White;
            this.serverListView.Location = new System.Drawing.Point(12, 39);
            this.serverListView.MultiSelect = false;
            this.serverListView.Name = "serverListView";
            this.serverListView.OwnerDraw = true;
            this.serverListView.Size = new System.Drawing.Size(765, 176);
            this.serverListView.TabIndex = 8;
            this.serverListView.UseCompatibleStateImageBehavior = false;
            this.serverListView.View = System.Windows.Forms.View.Details;
            this.serverListView.DoubleClick += new System.EventHandler(this.serverListView_DoubleClick);
            this.serverListView.DrawColumnHeader += new System.Windows.Forms.DrawListViewColumnHeaderEventHandler(this.serverListView_DrawColumnHeader);
            this.serverListView.DrawItem += new System.Windows.Forms.DrawListViewItemEventHandler(this.serverListView_DrawItem);
            this.serverListView.DrawSubItem += new System.Windows.Forms.DrawListViewSubItemEventHandler(this.serverListView_DrawSubItem);
            //
            // serverNameColumn
            //
            this.serverNameColumn.Text = "Server";
            this.serverNameColumn.Width = 310;
            //
            // serverAddressColumn
            //
            this.serverAddressColumn.Text = "Address";
            this.serverAddressColumn.Width = 145;
            //
            // serverPlayersColumn
            //
            this.serverPlayersColumn.Text = "Players";
            this.serverPlayersColumn.Width = 90;
            //
            // serverGameTypeColumn
            //
            this.serverGameTypeColumn.Text = "Game Type";
            this.serverGameTypeColumn.Width = 215;
            //
            // refreshServersButton
            //
            this.refreshServersButton.Location = new System.Drawing.Point(12, 10);
            this.refreshServersButton.Name = "refreshServersButton";
            this.refreshServersButton.Size = new System.Drawing.Size(100, 23);
            this.refreshServersButton.TabIndex = 9;
            this.refreshServersButton.Text = "Refresh Servers";
            this.refreshServersButton.UseVisualStyleBackColor = true;
            this.refreshServersButton.Click += new System.EventHandler(this.refreshServersButton_Click);
            //
            // connectSelectedServerButton
            //
            this.connectSelectedServerButton.Location = new System.Drawing.Point(118, 10);
            this.connectSelectedServerButton.Name = "connectSelectedServerButton";
            this.connectSelectedServerButton.Size = new System.Drawing.Size(112, 23);
            this.connectSelectedServerButton.TabIndex = 10;
            this.connectSelectedServerButton.Text = "Connect";
            this.connectSelectedServerButton.UseVisualStyleBackColor = true;
            this.connectSelectedServerButton.Click += new System.EventHandler(this.connectSelectedServerButton_Click);
            //
            // serverStatusLabel
            //
            this.serverStatusLabel.AutoSize = true;
            this.serverStatusLabel.Location = new System.Drawing.Point(238, 15);
            this.serverStatusLabel.Name = "serverStatusLabel";
            this.serverStatusLabel.Size = new System.Drawing.Size(103, 13);
            this.serverStatusLabel.TabIndex = 10;
            this.serverStatusLabel.Text = "Server list not loaded.";
            //
            // playerNameLabel
            //
            this.playerNameLabel.AutoSize = true;
            this.playerNameLabel.Location = new System.Drawing.Point(476, 15);
            this.playerNameLabel.Name = "playerNameLabel";
            this.playerNameLabel.Size = new System.Drawing.Size(67, 13);
            this.playerNameLabel.TabIndex = 11;
            this.playerNameLabel.Text = "Player name:";
            //
            // playerNameTextBox
            //
            this.playerNameTextBox.Location = new System.Drawing.Point(549, 12);
            this.playerNameTextBox.MaxLength = 32;
            this.playerNameTextBox.Name = "playerNameTextBox";
            this.playerNameTextBox.Size = new System.Drawing.Size(228, 20);
            this.playerNameTextBox.TabIndex = 12;
            this.playerNameTextBox.Text = "Player";
            //
            // Form1
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(790, 225);
            this.Controls.Add(this.serverStatusLabel);
            this.Controls.Add(this.refreshServersButton);
            this.Controls.Add(this.connectSelectedServerButton);
            this.Controls.Add(this.serverListView);
            this.Controls.Add(this.playerNameTextBox);
            this.Controls.Add(this.playerNameLabel);
            this.Name = "Form1";
            this.Text = "Jj2 Client";
            this.Shown += new System.EventHandler(this.Form1_Shown);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.ListView serverListView;
        private System.Windows.Forms.ColumnHeader serverNameColumn;
        private System.Windows.Forms.ColumnHeader serverAddressColumn;
        private System.Windows.Forms.ColumnHeader serverPlayersColumn;
        private System.Windows.Forms.ColumnHeader serverGameTypeColumn;
        private System.Windows.Forms.Button refreshServersButton;
        private System.Windows.Forms.Label serverStatusLabel;
        private System.Windows.Forms.Label playerNameLabel;
        private System.Windows.Forms.TextBox playerNameTextBox;
        private System.Windows.Forms.Button connectSelectedServerButton;
    }
}
