namespace ForensicWhisperDeskZH
{
    partial class MainRibbon : Microsoft.Office.Tools.Ribbon.RibbonBase
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        public MainRibbon()
            : base(Globals.Factory.GetRibbonFactory())
        {
            InitializeComponent();
        }

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

        #region Component Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.tab1 = this.Factory.CreateRibbonTab();
            this.group2 = this.Factory.CreateRibbonGroup();
            this.group1 = this.Factory.CreateRibbonGroup();
            this.MicrophoneCheckBox = this.Factory.CreateRibbonComboBox();
            this.LanguageSelection = this.Factory.CreateRibbonComboBox();
            this.ModelSelection = this.Factory.CreateRibbonComboBox();
            this.AdvancedSettings = this.Factory.CreateRibbonGroup();
            this.SilenceThreshold = this.Factory.CreateRibbonEditBox();
            this.MinChunkSizeInSeconds = this.Factory.CreateRibbonEditBox();
            this.ListenModeButton = this.Factory.CreateRibbonButton();
            this.StartTranscriptionButton = this.Factory.CreateRibbonButton();
            this.ResetButton = this.Factory.CreateRibbonButton();
            this.tab1.SuspendLayout();
            this.group2.SuspendLayout();
            this.group1.SuspendLayout();
            this.AdvancedSettings.SuspendLayout();
            this.SuspendLayout();
            // 
            // tab1
            // 
            this.tab1.ControlId.ControlIdType = Microsoft.Office.Tools.Ribbon.RibbonControlIdType.Office;
            this.tab1.Groups.Add(this.group2);
            this.tab1.Groups.Add(this.group1);
            this.tab1.Groups.Add(this.AdvancedSettings);
            this.tab1.Label = "TabAddIns";
            this.tab1.Name = "tab1";
            // 
            // group2
            // 
            this.group2.Items.Add(this.ListenModeButton);
            this.group2.Items.Add(this.StartTranscriptionButton);
            this.group2.Label = "Steuerung";
            this.group2.Name = "group2";
            // 
            // group1
            // 
            this.group1.Items.Add(this.MicrophoneCheckBox);
            this.group1.Items.Add(this.LanguageSelection);
            this.group1.Items.Add(this.ModelSelection);
            this.group1.Label = "Einstellungen";
            this.group1.Name = "group1";
            // 
            // MicrophoneCheckBox
            // 
            this.MicrophoneCheckBox.Label = "Mikrophonauswahl ";
            this.MicrophoneCheckBox.Name = "MicrophoneCheckBox";
            this.MicrophoneCheckBox.Text = null;
            this.MicrophoneCheckBox.TextChanged += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.MicrophoneCheckBox_TextChanged);
            // 
            // LanguageSelection
            // 
            this.LanguageSelection.Label = "Sprachauswahl       ";
            this.LanguageSelection.Name = "LanguageSelection";
            this.LanguageSelection.Text = null;
            this.LanguageSelection.TextChanged += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.LanguageSelection_TextChanged);
            // 
            // ModelSelection
            // 
            this.ModelSelection.Label = "Transkription Model";
            this.ModelSelection.Name = "ModelSelection";
            this.ModelSelection.Text = null;
            this.ModelSelection.TextChanged += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.ModelSelection_TextChanged);
            // 
            // AdvancedSettings
            // 
            this.AdvancedSettings.Items.Add(this.SilenceThreshold);
            this.AdvancedSettings.Items.Add(this.MinChunkSizeInSeconds);
            this.AdvancedSettings.Items.Add(this.ResetButton);
            this.AdvancedSettings.Label = "Erweiterte Einstellungen";
            this.AdvancedSettings.Name = "AdvancedSettings";
            // 
            // SilenceThreshold
            // 
            this.SilenceThreshold.Label = "Dauer der Stille zwischen Wörtern";
            this.SilenceThreshold.Name = "SilenceThreshold";
            this.SilenceThreshold.Text = null;
            this.SilenceThreshold.TextChanged += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.SilenceThreshold_TextChanged);
            // 
            // MinChunkSizeInSeconds
            // 
            this.MinChunkSizeInSeconds.Label = "Minimale Dauer von Audioblöcken";
            this.MinChunkSizeInSeconds.Name = "MinChunkSizeInSeconds";
            this.MinChunkSizeInSeconds.Text = null;
            this.MinChunkSizeInSeconds.TextChanged += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.MaxChunkSizeInSeconds_TextChanged);
            // 
            // ListenModeButton
            // 
            this.ListenModeButton.ControlSize = Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge;
            this.ListenModeButton.Label = "Zuhören Starten";
            this.ListenModeButton.Name = "ListenModeButton";
            this.ListenModeButton.OfficeImageId = "SpeechMicrophone";
            this.ListenModeButton.ShowImage = true;
            this.ListenModeButton.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.ListenModeButton_Click);
            // 
            // StartTranscriptionButton
            // 
            this.StartTranscriptionButton.ControlSize = Microsoft.Office.Core.RibbonControlSize.RibbonControlSizeLarge;
            this.StartTranscriptionButton.Label = "Diktat Starten";
            this.StartTranscriptionButton.Name = "StartTranscriptionButton";
            this.StartTranscriptionButton.OfficeImageId = "AudioRecordingInsert";
            this.StartTranscriptionButton.ShowImage = true;
            this.StartTranscriptionButton.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.StartTranscriptionButton_Click);
            // 
            // ResetButton
            // 
            this.ResetButton.Label = "Einstellungen zurücksetzen";
            this.ResetButton.Name = "ResetButton";
            this.ResetButton.OfficeImageId = "RecordsRefreshRecords";
            this.ResetButton.ShowImage = true;
            this.ResetButton.Click += new Microsoft.Office.Tools.Ribbon.RibbonControlEventHandler(this.ResetButton_Click);
            // 
            // MainRibbon
            // 
            this.Name = "MainRibbon";
            this.RibbonType = "Microsoft.Word.Document";
            this.Tabs.Add(this.tab1);
            this.Load += new Microsoft.Office.Tools.Ribbon.RibbonUIEventHandler(this.TestRibbon_Load);
            this.tab1.ResumeLayout(false);
            this.tab1.PerformLayout();
            this.group2.ResumeLayout(false);
            this.group2.PerformLayout();
            this.group1.ResumeLayout(false);
            this.group1.PerformLayout();
            this.AdvancedSettings.ResumeLayout(false);
            this.AdvancedSettings.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        internal Microsoft.Office.Tools.Ribbon.RibbonTab tab1;
        internal Microsoft.Office.Tools.Ribbon.RibbonGroup group1;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton StartTranscriptionButton;
        internal Microsoft.Office.Tools.Ribbon.RibbonComboBox MicrophoneCheckBox;
        internal Microsoft.Office.Tools.Ribbon.RibbonGroup group2;
        internal Microsoft.Office.Tools.Ribbon.RibbonGroup AdvancedSettings;
        internal Microsoft.Office.Tools.Ribbon.RibbonComboBox LanguageSelection;
        internal Microsoft.Office.Tools.Ribbon.RibbonEditBox MinChunkSizeInSeconds;
        internal Microsoft.Office.Tools.Ribbon.RibbonComboBox ModelSelection;
        internal Microsoft.Office.Tools.Ribbon.RibbonEditBox SilenceThreshold;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton ResetButton;
        internal Microsoft.Office.Tools.Ribbon.RibbonButton ListenModeButton;
    }

    partial class ThisRibbonCollection
    {
        internal MainRibbon MainRibbon
        {
            get { return this.GetRibbon<MainRibbon>(); }
        }
    }
}
