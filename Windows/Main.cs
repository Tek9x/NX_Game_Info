﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Application = System.Windows.Forms.Application;
using Bluegrams.Application;
using BrightIdeasSoftware;
using LibHac;
using FsTitle = LibHac.Title;
using Title = NX_Game_Info.Common.Title;

#pragma warning disable IDE1006 // Naming rule violation: These words must begin with upper case characters

namespace NX_Game_Info
{
    public partial class Main : Form
    {
        internal AboutBox aboutBox = new AboutBox();
        internal IProgressDialog progressDialog;

        public enum Worker
        {
            File,
            Directory,
            SDCard,
            Invalid = -1
        }

        private List<Title> titles = new List<Title>();

        public Main()
        {
            InitializeComponent();

            PortableSettingsProvider.SettingsFileName = Common.USER_SETTINGS;
            PortableSettingsProviderBase.SettingsDirectory = Process.path_prefix;
            PortableSettingsProvider.ApplyProvider(Common.Settings.Default, Common.History.Default);

            Common.Settings.Default.Upgrade();
            Common.History.Default.Upgrade();

            debugLogToolStripMenuItem.Checked = Common.Settings.Default.DebugLog;

            aboutToolStripMenuItem.Text = String.Format("&About {0}", Application.ProductName);

            bool init = Process.initialize(out List<string> messages);

            foreach (var message in messages)
            {
                MessageBox.Show(message, Application.ProductName);
            }

            if (!init)
            {
                Environment.Exit(-1);
            }

            titles = Process.processHistory();

            reloadData();

            toolStripStatusLabel.Text = String.Format("{0} files", titles.Count);
        }

        public void reloadData()
        {
            uint index = 0, count = (uint)titles.Count;

            objectListView.SetObjects(titles);

            foreach (OLVListItem listItem in objectListView.Items)
            {
                Title title = listItem.RowObject as Title;

                progressDialog?.SetLine(2, title.titleName, true, IntPtr.Zero);
                progressDialog?.SetProgress(index++, count);

                string titleID = title.type == TitleType.AddOnContent ? title.titleID : title.titleIDApplication;

                Process.latestVersions.TryGetValue(titleID, out uint latestVersion);
                Process.versionList.TryGetValue(titleID, out uint version);
                Process.titleVersions.TryGetValue(titleID, out uint titleVersion);

                if (latestVersion < version || latestVersion < titleVersion)
                {
                    listItem.BackColor = title.signature != true ? Color.OldLace : Color.LightYellow;
                }
                else if (title.signature != true)
                {
                    listItem.BackColor = Color.WhiteSmoke;
                }

                if (title.permission == Title.Permission.Dangerous)
                {
                    listItem.ForeColor = Color.DarkRed;
                }
                else if (title.permission == Title.Permission.Unsafe)
                {
                    listItem.ForeColor = Color.Indigo;
                }
            }
        }

        public void saveWindowState()
        {
            if (WindowState == FormWindowState.Normal)
            {
                Common.Settings.Default.WindowLocation = Location;
                Common.Settings.Default.WindowSize = Size;
            }
            else
            {
                Common.Settings.Default.WindowLocation = RestoreBounds.Location;
                Common.Settings.Default.WindowSize = RestoreBounds.Size;
            }

            Common.Settings.Default.Save();
        }

        private void Main_Load(object sender, EventArgs e)
        {
            Location = Common.Settings.Default.WindowLocation;
            Size = Common.Settings.Default.WindowSize;
        }

        private void Main_FormClosing(object sender, FormClosingEventArgs e)
        {
            saveWindowState();
        }

        private void openFileToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (backgroundWorkerProcess.IsBusy)
            {
                MessageBox.Show("Please wait until the current process is finished and try again.", Application.ProductName);
                return;
            }

            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Title = "Open NX Game Files";
            openFileDialog.Filter = "NX Game Files (*.xci;*.nsp;*.nro)|*.xci;*.nsp;*.nro|Gamecard Files (*.xci)|*.xci|Package Files (*.nsp)|*.nsp|Homebrew Files (*.nro)|*.nro|All Files (*.*)|*.*";
            openFileDialog.Multiselect = true;
            openFileDialog.InitialDirectory = !String.IsNullOrEmpty(Common.Settings.Default.InitialDirectory) && Directory.Exists(Common.Settings.Default.InitialDirectory) ? Common.Settings.Default.InitialDirectory : Directory.GetDirectoryRoot(Directory.GetCurrentDirectory());

            Process.log?.WriteLine("\nOpen File");

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                objectListView.Items.Clear();
                toolStripStatusLabel.Text = "";

                Common.Settings.Default.InitialDirectory = Path.GetDirectoryName(openFileDialog.FileNames.First());
                Common.Settings.Default.Save();

                progressDialog = (IProgressDialog)new ProgressDialog();
                progressDialog.StartProgressDialog(Handle, "Opening files");

                backgroundWorkerProcess.RunWorkerAsync((Worker.File, openFileDialog.FileNames));
            }
        }

        private void openDirectoryToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (backgroundWorkerProcess.IsBusy)
            {
                MessageBox.Show("Please wait until the current process is finished and try again.", Application.ProductName);
                return;
            }

            FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
            folderBrowserDialog.SelectedPath = !String.IsNullOrEmpty(Common.Settings.Default.InitialDirectory) && Directory.Exists(Common.Settings.Default.InitialDirectory) ? Common.Settings.Default.InitialDirectory : Directory.GetDirectoryRoot(Directory.GetCurrentDirectory());

            Process.log?.WriteLine("\nOpen Directory");

            if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
            {
                objectListView.Items.Clear();
                toolStripStatusLabel.Text = "";

                Common.Settings.Default.InitialDirectory = folderBrowserDialog.SelectedPath;
                Common.Settings.Default.Save();

                progressDialog = (IProgressDialog)new ProgressDialog();
                progressDialog.StartProgressDialog(Handle, String.Format("Opening files from directory {0}", folderBrowserDialog.SelectedPath));

                backgroundWorkerProcess.RunWorkerAsync((Worker.Directory, folderBrowserDialog.SelectedPath));
            }
        }

        private void openSDCardToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (Process.keyset?.SdSeed?.All(b => b == 0) ?? true)
            {
                string error = "sd_seed is missing from Console Keys";
                Process.log?.WriteLine(error);

                MessageBox.Show(String.Format("{0}.\nOpen SD Card will not be available.", error), Application.ProductName);
                return;
            }

            if ((Process.keyset?.SdCardKekSource?.All(b => b == 0) ?? true) || (Process.keyset?.SdCardKeySources?[1]?.All(b => b == 0) ?? true))
            {
                Process.log?.WriteLine("Keyfile missing required keys");
                Process.log?.WriteLine(" - {0} ({1}exists)", "sd_card_kek_source", (bool)Process.keyset?.SdCardKekSource?.Any(b => b != 0) ? "" : "not ");
                Process.log?.WriteLine(" - {0} ({1}exists)", "sd_card_nca_key_source", (bool)Process.keyset?.SdCardKeySources?[1]?.Any(b => b != 0) ? "" : "not ");

                MessageBox.Show("sd_card_kek_source and sd_card_nca_key_source are missing from Keyfile.\nOpen SD Card will not be available.", Application.ProductName);
                return;
            }

            if (backgroundWorkerProcess.IsBusy)
            {
                MessageBox.Show("Please wait until the current process is finished and try again.", Application.ProductName);
                return;
            }

            FolderBrowserDialog folderBrowserDialog = new FolderBrowserDialog();
            folderBrowserDialog.SelectedPath = !String.IsNullOrEmpty(Common.Settings.Default.SDCardDirectory) && Directory.Exists(Common.Settings.Default.SDCardDirectory) ? Common.Settings.Default.SDCardDirectory : Directory.GetDirectoryRoot(Directory.GetCurrentDirectory());

            Process.log?.WriteLine("\nOpen SD Card");

            if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
            {
                objectListView.Items.Clear();
                toolStripStatusLabel.Text = "";

                Common.Settings.Default.SDCardDirectory = folderBrowserDialog.SelectedPath;
                Common.Settings.Default.Save();

                Process.log?.WriteLine("SD card selected");

                progressDialog = (IProgressDialog)new ProgressDialog();
                progressDialog.StartProgressDialog(Handle, String.Format("Opening SD card on {0}", folderBrowserDialog.SelectedPath));

                backgroundWorkerProcess.RunWorkerAsync((Worker.SDCard, folderBrowserDialog.SelectedPath));
            }
        }

        private void exportToolStripMenuItem_Click(object sender, EventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();
            saveFileDialog.Title = "Export Titles";
            saveFileDialog.Filter = "Text Documents (*.txt)|*.txt|All Files (*.*)|*.*";

            Process.log?.WriteLine("\nExport Titles");

            if (saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                using (var writer = new StreamWriter(saveFileDialog.FileName))
                {
                    progressDialog = (IProgressDialog)new ProgressDialog();
                    progressDialog.StartProgressDialog(Handle, "Exporting titles");

                    writer.WriteLine("{0} {1}", aboutBox.AssemblyTitle, aboutBox.AssemblyVersion);
                    writer.WriteLine("--------------------------------------------------------------\n");

                    writer.WriteLine("Export titles starts at {0}\n", String.Format("{0:F}", DateTime.Now));

                    uint index = 0, count = (uint)titles.Count;

                    foreach (var title in titles)
                    {
                        if (progressDialog.HasUserCancelled())
                        {
                            break;
                        }

                        progressDialog.SetLine(2, title.titleName, true, IntPtr.Zero);
                        progressDialog.SetProgress(index++, count);

                        writer.WriteLine("{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}|{9}|{10}|{11}|{12}|{13}|{14}",
                            title.titleID,
                            title.titleName,
                            title.displayVersion,
                            title.versionString,
                            title.latestVersionString,
                            title.firmware,
                            title.masterkeyString,
                            title.filename,
                            title.filesizeString,
                            title.typeString,
                            title.distribution,
                            title.structureString,
                            title.signatureString,
                            title.permissionString,
                            title.error);
                    }

                    writer.WriteLine("\n{0} of {1} titles exported", index, titles.Count);

                    Process.log?.WriteLine("\n{0} of {1} titles exported", index, titles.Count);

                    progressDialog.StopProgressDialog();
                    Activate();

                    MessageBox.Show(String.Format("{0} of {1} titles exported", index, titles.Count), Application.ProductName);
                }
            }
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            saveWindowState();

            Environment.Exit(-1);
        }

        private void updateVersionListToolStripMenuItem_Click(object sender, EventArgs e)
        {
            progressDialog = (IProgressDialog)new ProgressDialog();
            progressDialog.StartProgressDialog(Handle, "Downloading version list");

            progressDialog.SetLine(2, String.Format("Downloading from {0}", Common.TAGAYA_VERSIONLIST), true, IntPtr.Zero);

            if (Process.updateVersionList())
            {
                uint count = 0;

                foreach (var title in titles)
                {
                    if (title.type == TitleType.Application || title.type == TitleType.Patch)
                    {
                        if (Process.versionList.TryGetValue(title.titleIDApplication, out uint version))
                        {
                            if (title.latestVersion == unchecked((uint)-1) || version > title.latestVersion)
                            {
                                title.latestVersion = version;
                                count++;
                            }
                        }
                    }
                }

                if (count != 0)
                {
                    reloadData();

                    Common.History.Default.Titles.Add(titles.ToList());
                    if (Common.History.Default.Titles.Count > Common.HISTORY_SIZE)
                    {
                        Common.History.Default.Titles.RemoveRange(0, Common.History.Default.Titles.Count - Common.HISTORY_SIZE);
                    }
                    Common.History.Default.Save();
                }

                Process.log?.WriteLine("\n{0} titles have updated version", count);

                progressDialog.StopProgressDialog();
                Activate();

                MessageBox.Show(String.Format("{0} titles have updated version", count), Application.ProductName);
            }
            else
            {
                progressDialog.StopProgressDialog();
                Activate();

                MessageBox.Show("Failed to download version list", Application.ProductName);
            }
        }

        private void debugLogToolStripMenuItem_CheckedChanged(object sender, EventArgs e)
        {
            Common.Settings.Default.DebugLog = debugLogToolStripMenuItem.Checked;
            Common.Settings.Default.Save();

            if (Common.Settings.Default.DebugLog)
            {
                try
                {
                    Process.log = File.AppendText(Process.path_prefix + Common.LOG_FILE);
                    Process.log.AutoFlush = true;
                }
                catch { }
            }
            else
            {
                Process.log?.Close();
                Process.log = null;
            }
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            aboutBox.Show();
        }

        private void backgroundWorkerProcess_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            BackgroundWorker worker = sender as BackgroundWorker;

            titles.Clear();

            if (e.Argument is ValueTuple<Worker, string[]> argumentFile)
            {
                if (argumentFile.Item1 == Worker.File && argumentFile.Item2 is string[] files)
                {
                    List<string> filenames = files.ToList();
                    filenames.Sort();

                    Process.log?.WriteLine("{0} files selected", filenames.Count);

                    worker.ReportProgress(-1, String.Format("Opening {0} files", filenames.Count));

                    int count = filenames.Count, index = 0;

                    foreach (var filename in filenames)
                    {
                        if (worker.CancellationPending) break;

                        worker.ReportProgress(100 * index++ / count, filename);

                        Title title = Process.processFile(filename);
                        if (title != null)
                        {
                            titles.Add(title);
                        }
                    }

                    if (!worker.CancellationPending)
                    {
                        worker.ReportProgress(100, "");
                    }

                    Process.log?.WriteLine("\n{0} titles processed", titles.Count);
                }
            }
            else if (e.Argument is ValueTuple<Worker, string> argumentPath)
            {
                if (argumentPath.Item1 == Worker.Directory && argumentPath.Item2 is string path)
                {
                    List<string> filenames = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                        .Where(filename => filename.ToLower().EndsWith(".xci") || filename.ToLower().EndsWith(".nsp") || filename.ToLower().EndsWith(".nro")).ToList();
                    filenames.Sort();

                    Process.log?.WriteLine("{0} files selected", filenames.Count);

                    worker.ReportProgress(-1, String.Format("Opening {0} files from directory {1}", filenames.Count, path));

                    int count = filenames.Count, index = 0;

                    foreach (var filename in filenames)
                    {
                        if (worker.CancellationPending) break;

                        worker.ReportProgress(100 * index++ / count, filename);

                        Title title = Process.processFile(filename);
                        if (title != null)
                        {
                            titles.Add(title);
                        }
                    }

                    if (!worker.CancellationPending)
                    {
                        worker.ReportProgress(100, "");
                    }

                    Process.log?.WriteLine("\n{0} titles processed", titles.Count);
                }
                else if (argumentPath.Item1 == Worker.SDCard && argumentPath.Item2 is string pathSd)
                {
                    List<FsTitle> fsTitles = Process.processSd(pathSd);

                    if (fsTitles != null)
                    {
                        int count = fsTitles.Count, index = 0;

                        foreach (var fsTitle in fsTitles)
                        {
                            if (worker.CancellationPending) break;

                            worker.ReportProgress(100 * index++ / count, fsTitle.MainNca?.Filename);

                            Title title = Process.processTitle(fsTitle);
                            if (title != null)
                            {
                                titles.Add(title);
                            }
                        }

                        if (!worker.CancellationPending)
                        {
                            worker.ReportProgress(100, "");
                        }

                        Process.log?.WriteLine("\n{0} titles processed", titles.Count);
                    }
                    else
                    {
                        worker.ReportProgress(0, "");

                        string error = "SD card \"Contents\" directory could not be found";
                        Process.log?.WriteLine(error);

                        e.Result = error;
                        return;
                    }
                }
            }

            e.Result = titles;
        }

        private void backgroundWorkerProcess_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            if (progressDialog.HasUserCancelled())
            {
                if (backgroundWorkerProcess.IsBusy)
                {
                    backgroundWorkerProcess.CancelAsync();
                }
            }

            if (e.ProgressPercentage == -1)
            {
                progressDialog.SetLine(1, e.UserState as string, false, IntPtr.Zero);
            }
            else
            {
                progressDialog.SetLine(2, e.UserState as string, true, IntPtr.Zero);
                progressDialog.SetProgress((uint)e.ProgressPercentage, 100);
            }
        }

        private void backgroundWorkerProcess_RunWorkerCompleted(object sender, System.ComponentModel.RunWorkerCompletedEventArgs e)
        {
            if (e.Result is List<Title> titles)
            {
                reloadData();

                Common.History.Default.Titles.Add(titles.ToList());
                if (Common.History.Default.Titles.Count > Common.HISTORY_SIZE)
                {
                    Common.History.Default.Titles.RemoveRange(0, Common.History.Default.Titles.Count - Common.HISTORY_SIZE);
                }
                Common.History.Default.Save();

                toolStripStatusLabel.Text = String.Format("{0} files", titles.Count);

                progressDialog.StopProgressDialog();
                Activate();
            }
            else if (e.Result is string error)
            {
                progressDialog.StopProgressDialog();

                MessageBox.Show(String.Format("{0}.", error), Application.ProductName);
            }
        }
    }

    // IProgressDialog Credits to Alex J https://stackoverflow.com/a/37393363
    [Flags]
    public enum IPD_Flags : uint
    {
        Normal = 0x00000000,
        Modal = 0x00000001,
        AutoTime = 0x00000002,
        NoTime = 0x00000004,
        NoMinimize = 0x00000008,
        NoProgressBar = 0x00000010,
    }

    [Flags]
    public enum IPDTIMER_Flags : uint
    {
        Reset = 0x00000001,
        Pause = 0x00000002,
        Resume = 0x00000003,
    }

    [ComImport]
    [Guid("F8383852-FCD3-11d1-A6B9-006097DF5BD4")]
    internal class ProgressDialog
    {

    }

    [ComImport]
    [Guid("EBBC7C04-315E-11d2-B62F-006097DF5BD4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IProgressDialog
    {
        [PreserveSig]
        void StartProgressDialog(IntPtr hwndParent
        , [MarshalAs(UnmanagedType.IUnknown)] object punkEnableModless
        , uint dwFlags
        , IntPtr pvResevered);

        [PreserveSig]
        void StopProgressDialog();

        [PreserveSig]
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pwzTitle);

        [PreserveSig]
        void SetAnimation(IntPtr hInstAnimation, ushort idAnimation);

        [PreserveSig]
        [return: MarshalAs(UnmanagedType.Bool)]
        bool HasUserCancelled();

        [PreserveSig]
        void SetProgress(uint dwCompleted, uint dwTotal);

        [PreserveSig]
        void SetProgress64(ulong ullCompleted, ulong ullTotal);

        [PreserveSig]
        void SetLine(uint dwLineNum
            , [MarshalAs(UnmanagedType.LPWStr)] string pwzString
            , [MarshalAs(UnmanagedType.VariantBool)] bool fCompactPath
            , IntPtr pvResevered);

        [PreserveSig]
        void SetCancelMsg([MarshalAs(UnmanagedType.LPWStr)]string pwzCancelMsg, object pvResevered);

        [PreserveSig]
        void Timer(uint dwTimerAction, object pvResevered);
    }

    public static class ProgressDialogExtension
    {
        internal static void StartProgressDialog(this IProgressDialog progressDialog, IntPtr hwndParent, string pwzString)
        {
            progressDialog.SetTitle(Application.ProductName);
            progressDialog.SetCancelMsg("Please wait until the current process is finished", IntPtr.Zero);
            progressDialog.SetLine(1, pwzString, false, IntPtr.Zero);

            progressDialog.StartProgressDialog(hwndParent, null, (uint)(IPD_Flags.Modal | IPD_Flags.AutoTime | IPD_Flags.NoMinimize), IntPtr.Zero);
            progressDialog.SetProgress(0, 100);
        }
    }
}
