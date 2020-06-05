/* Copyright 2009 HPDI, LLC
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 * 
 *     http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Reflection;
using System.Security.AccessControl;
using System.Text;
using System.Windows.Forms;
using Hpdi.VssLogicalLib;

namespace Hpdi.Vss2Git
{
    /// <summary>
    /// Main form for the application.
    /// </summary>
    /// <author>Trevor Robinson</author>
    public partial class MainForm : Form
    {
        private readonly Dictionary<int, EncodingInfo> codePages = new Dictionary<int, EncodingInfo>();
        private readonly WorkQueue workQueue = new WorkQueue(1);
        private RevisionAnalyzer revisionAnalyzer;
        private ChangesetBuilder changesetBuilder;
        private Encoding selectedEncoding = Encoding.Default;

		public MainForm()
        {
            InitializeComponent();
        }

        private Logger OpenLog(string filename)
        {
            return string.IsNullOrEmpty(filename) ? Logger.Null : new Logger(filename, null);
        }

	    private static void ClearDir(DirectoryInfo info, bool deleteSelf, Func<string, bool> needDeleteFunc = null)
	    {
		    foreach (FileInfo file in info.GetFiles())
		    {
			    if (needDeleteFunc == null || needDeleteFunc(file.Name))
			    {
				    File.SetAttributes(file.FullName, FileAttributes.Normal);
				    file.SetAccessControl(new FileSecurity());
				    file.Delete();
			    }
		    }
		    foreach (DirectoryInfo dir in info.GetDirectories())
		    {
			    if (needDeleteFunc == null || needDeleteFunc(dir.Name))
			    {
				    ClearDir(dir, true);
			    }
		    }

		    if (deleteSelf)
		    {
			    info.Delete();
		    }
	    }


		private void goButton_Click(object sender, EventArgs e)
        {
	        this.WriteSettings();

			string outputDir;
	        if (outDirTextBox.TextLength == 0)
	        {
		        outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
	        }
	        else
	        {
		        outputDir = outDirTextBox.Text;
	        }

	        if (!Directory.Exists(outputDir))
	        {
		        Directory.CreateDirectory(outputDir);
	        }

	        string timeStr = DateTime.Now.ToString("yyyyMMddHHmmssfff");

			string commonLogFile = Path.Combine(outputDir, $"_log{timeStr}.log");
	        Logger commonLogger = OpenLog(commonLogFile);

	        string errorLogFile = Path.Combine(outputDir, $"_errors{timeStr}.log");
	        Logger errorLogger = OpenLog(errorLogFile);

			try
            {
                commonLogger.WriteLine("VSS2Git version {0}", Assembly.GetExecutingAssembly().GetName().Version);

	            Encoding encoding = this.selectedEncoding;
              
                commonLogger.WriteLine("VSS encoding: {0} (CP: {1}, IANA: {2})", encoding.EncodingName, encoding.CodePage, encoding.WebName);
                commonLogger.WriteLine("Comment transcoding: {0}", this.transcodeCheckBox.Checked ? "enabled" : "disabled");
                commonLogger.WriteLine("Ignore errors: {0}", this.ignoreErrorsCheckBox.Checked ? "enabled" : "disabled");

                var df = new VssDatabaseFactory(this.vssDirTextBox.Text);
                df.Encoding = this.selectedEncoding;
                var db = df.Open();

	            const string PROCESS_DIR_NAME = "_p";
	            const string SUCCESS_DIR_NAME = "_success";
	            const string FAIL_DIR_NAME = "_fail";

				// clear processing folder and delete processing log file
				string processDir = Path.Combine(outputDir, PROCESS_DIR_NAME);
		        string logFile = Path.Combine(outputDir, PROCESS_DIR_NAME + ".log");

				foreach (string vssPath in this.projectsTreeControl.SelectedPaths)
	            {
		            this.DeleteProcessingFolderAndLog(processDir, logFile);

			        Logger logger = new Logger(logFile, commonLogger);
		            bool isSuccess = false;
		            try
		            {

			            this.ProcessPath(db, vssPath, this.selectedEncoding, logger, processDir);
			            isSuccess = true;
		            }
		            catch (Exception ex)
		            {
			            string msg = $"ERROR: {vssPath}\r\n{ex}";

			            errorLogger.WriteLine(msg);
			            logger.WriteLine(msg);

			            logger.Dispose();
		            }

		            try
		            {
			            string moveDir = isSuccess ? SUCCESS_DIR_NAME : FAIL_DIR_NAME;
			            moveDir = Path.Combine(outputDir, moveDir);

			            PostProcessRepoFolder(processDir, moveDir, vssPath);
					}
		            catch (Exception ex)
		            {
			            errorLogger.WriteLine($"POSTPROCESS ERROR: {vssPath}\r\n{ex}");
			            throw;
					}
	            }
			}
            catch (Exception ex)
            {
			    string msg = $"GLOBAL ERROR: {ex}";

				errorLogger.WriteLine(msg);
	            commonLogger.WriteLine(msg);

	            errorLogger.Dispose();
	            commonLogger.Dispose();

				this.ShowException(ex);
            }
        }

	    private void DeleteProcessingFolderAndLog(string processDir, string logFile)
	    {
		    // clear processing folder
		    try
		    {
			    if (Directory.Exists(processDir))
			    {
				    ClearDir(new DirectoryInfo(processDir), true);
			    }
		    }
		    catch (Exception ex)
		    {
			    throw new Exception($"Ошибка при удалении папки '{processDir}'.", ex);
		    }

		    // delete processing log file
		    try
		    {
			    if (File.Exists(logFile))
			    {
				    File.Delete(logFile);
			    }
		    }
		    catch (Exception ex)
		    {
			    throw new Exception($"Ошибка при удалении файла '{logFile}'.", ex);
		    }
		}

	    private static void PostProcessRepoFolder(string repoDir, string moveDir, string vssPath)
	    {
			ClearDir(new DirectoryInfo(repoDir), false, path => !path.StartsWith(".git"));

		    if (!Directory.Exists(moveDir))
		    {
			    Directory.CreateDirectory(moveDir);
		    }

			string projFolder = GetSafeDirName(vssPath);
		    string projPath = Path.Combine(moveDir, projFolder);
			Directory.Move(repoDir, projPath);
	    }

	    private void ProcessPath(
		    VssDatabase db, 
		    string vssPath, 
		    Encoding encoding,
		    Logger logger,
		    string processDir)
	    {
			VssItem item;
			try
			{
				item = db.GetItem(vssPath);
			}
			catch (VssPathException ex)
			{
				MessageBox.Show(ex.Message, "Invalid project path",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			var project = item as VssProject;
			if (project == null)
			{
				MessageBox.Show(vssPath + " is not a project", "Invalid project path",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			revisionAnalyzer = new RevisionAnalyzer(workQueue, logger, db);
			if (!string.IsNullOrEmpty(excludeTextBox.Text))
			{
				revisionAnalyzer.ExcludeFiles = excludeTextBox.Text;
			}
			revisionAnalyzer.AddItem(project);

			changesetBuilder = new ChangesetBuilder(workQueue, logger, revisionAnalyzer);
			changesetBuilder.AnyCommentThreshold = TimeSpan.FromSeconds((double)anyCommentUpDown.Value);
			changesetBuilder.SameCommentThreshold = TimeSpan.FromSeconds((double)sameCommentUpDown.Value);
			changesetBuilder.BuildChangesets();

		    var gitExporter = new GitExporter(workQueue, logger,
		                                      revisionAnalyzer, changesetBuilder);
		    if (!string.IsNullOrEmpty(domainTextBox.Text))
		    {
			    gitExporter.EmailDomain = domainTextBox.Text;
		    }
		    if (!string.IsNullOrEmpty(commentTextBox.Text))
		    {
			    gitExporter.DefaultComment = commentTextBox.Text;
		    }
		    if (!transcodeCheckBox.Checked)
		    {
			    gitExporter.CommitEncoding = encoding;
		    }
		    gitExporter.IgnoreErrors = ignoreErrorsCheckBox.Checked;
		    gitExporter.ExportToGit(processDir);

			workQueue.Idle += delegate
			{
				logger.Dispose();
				logger = Logger.Null;
			};

			statusTimer.Enabled = true;
			goButton.Enabled = false;
		}

        private void cancelButton_Click(object sender, EventArgs e)
        {
            workQueue.Abort();
        }

        private void statusTimer_Tick(object sender, EventArgs e)
        {
            statusLabel.Text = workQueue.LastStatus ?? "Idle";
            timeLabel.Text = string.Format("Elapsed: {0:HH:mm:ss}",
                new DateTime(workQueue.ActiveTime.Ticks));

            if (revisionAnalyzer != null)
            {
                fileLabel.Text = "Files: " + revisionAnalyzer.FileCount;
                revisionLabel.Text = "Revisions: " + revisionAnalyzer.RevisionCount;
            }

            if (changesetBuilder != null)
            {
                changeLabel.Text = "Changesets: " + changesetBuilder.Changesets.Count;
            }

            if (workQueue.IsIdle)
            {
                revisionAnalyzer = null;
                changesetBuilder = null;

                statusTimer.Enabled = false;
                goButton.Enabled = true;
            }

            var exceptions = workQueue.FetchExceptions();
            if (exceptions != null)
            {
                foreach (var exception in exceptions)
                {
                    ShowException(exception);
                }
            }
        }

        private void ShowException(Exception exception)
        {
	        bool isKnown;
	        var message = ExceptionFormatter.Format(exception, out isKnown);
            //logger.WriteLine("ERROR: {0}", message);
            //logger.WriteLine(exception);

	        if (!isKnown)
	        {
		        message = exception.ToString();
	        }
	        MessageBox.Show(message, "Unhandled Exception", MessageBoxButtons.OK, MessageBoxIcon.Error);
		}

		private void MainForm_Load(object sender, EventArgs e)
        {
            this.Text += " " + Assembly.GetExecutingAssembly().GetName().Version;

            var defaultCodePage = Encoding.Default.CodePage;
            var description = string.Format("System default - {0}", Encoding.Default.EncodingName);
            var defaultIndex = encodingComboBox.Items.Add(description);
            encodingComboBox.SelectedIndex = defaultIndex;

            var encodings = Encoding.GetEncodings();
            foreach (var encoding in encodings)
            {
                var codePage = encoding.CodePage;
                description = string.Format("CP{0} - {1}", codePage, encoding.DisplayName);
                var index = encodingComboBox.Items.Add(description);
                codePages[index] = encoding;
                if (codePage == defaultCodePage)
                {
                    codePages[defaultIndex] = encoding;
                }
            }

            ReadSettings();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            WriteSettings();

            workQueue.Abort();
            workQueue.WaitIdle();
        }

        private void ReadSettings()
        {
            var settings = Properties.Settings.Default;
            vssDirTextBox.Text = settings.VssDirectory;
            excludeTextBox.Text = settings.VssExcludePaths;
            outDirTextBox.Text = settings.GitDirectory;
            domainTextBox.Text = settings.DefaultEmailDomain;
            commentTextBox.Text = settings.DefaultComment;
            transcodeCheckBox.Checked = settings.TranscodeComments;
            forceAnnotatedCheckBox.Checked = settings.ForceAnnotatedTags;
            anyCommentUpDown.Value = settings.AnyCommentSeconds;
            sameCommentUpDown.Value = settings.SameCommentSeconds;
	        this.projectsTreeControl.SelectedPaths = settings.Projects;
        }

        private void WriteSettings()
        {
            var settings = Properties.Settings.Default;
            settings.VssDirectory = vssDirTextBox.Text;
            settings.VssExcludePaths = excludeTextBox.Text;
            settings.GitDirectory = outDirTextBox.Text;
            settings.DefaultEmailDomain = domainTextBox.Text;
            settings.TranscodeComments = transcodeCheckBox.Checked;
            settings.ForceAnnotatedTags = forceAnnotatedCheckBox.Checked;
            settings.AnyCommentSeconds = (int)anyCommentUpDown.Value;
            settings.SameCommentSeconds = (int)sameCommentUpDown.Value;
	        settings.Projects = this.projectsTreeControl.SelectedPaths;
            settings.Save();
        }

	    private static string GetSafeDirName(string name)
	    {
		    name = name.Replace("$/", "");
		    name = name.Replace("/", "_");
		    return name;
	    }

		private void vssDirTextBox_TextChanged(object sender, EventArgs e)
		{
			this.projectsTreeControl.VSSDirectory = this.vssDirTextBox.Text.Trim();
		}

		private void encodingComboBox_SelectedIndexChanged(object sender, EventArgs e)
		{
			Encoding encoding = Encoding.Default;
			EncodingInfo encodingInfo;
			if (codePages.TryGetValue(encodingComboBox.SelectedIndex, out encodingInfo))
			{
				encoding = encodingInfo.GetEncoding();
			}
			this.selectedEncoding = encoding;
			this.projectsTreeControl.Encoding = encoding;
		}

		private void projectsTreeControl_CheckedChanged(object sender, EventArgs e)
		{
			this.WriteSettings();
		}
	}
}
