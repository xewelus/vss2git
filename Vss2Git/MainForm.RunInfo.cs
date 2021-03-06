﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Text;
using System.Windows.Forms;
using Hpdi.VssLogicalLib;

namespace Hpdi.Vss2Git
{
	public partial class MainForm
	{
		private class RunInfo : IDisposable
		{
			private readonly MainForm form;
			private readonly string outputDir;
			private readonly string processDir;
			private readonly string logFile;
			private readonly Logger commonLogger;
			private readonly VssDatabase db;
			private readonly Encoding selectedEncoding;
			private readonly Logger errorLogger;
			private readonly WorkQueue workQueue;
			private readonly Queue<ProjectInfo> toProcess;
			private RepoInfo repoInfo;
			private bool Canceled;

			public RevisionAnalyzer RevisionAnalyzer => this.repoInfo?.RevisionAnalyzer;
			public ChangesetBuilder ChangesetBuilder => this.repoInfo?.ChangesetBuilder;

			private const string PROCESS_DIR_NAME = "_p";

			public RunInfo(
				MainForm form,
				string outputDir,
				Logger commonLogger,
				VssDatabase db,
				Encoding selectedEncoding,
				Logger errorLogger,
				WorkQueue workQueue,
				Queue<ProjectInfo> toProcess)
			{
				this.form = form;
				this.outputDir = outputDir;
				this.processDir = Path.Combine(outputDir, PROCESS_DIR_NAME);
				this.logFile = Path.Combine(this.outputDir, PROCESS_DIR_NAME + ".log");
				this.commonLogger = commonLogger;
				this.db = db;
				this.selectedEncoding = selectedEncoding;
				this.errorLogger = errorLogger;
				this.workQueue = workQueue;
				this.toProcess = toProcess;
			}

			public bool StartNext()
			{
				if (this.Canceled) return false;
				if (this.toProcess.Count <= 0) return false;

				ProjectInfo projectInfo = this.toProcess.Dequeue();

				this.Process(projectInfo);
				return true;
			}

			private void Process(ProjectInfo projectInfo)
			{
				// clear processing folder and delete processing log file
				this.DeleteProcessingFolderAndLog();

				Logger logger = new Logger(this.logFile, this.commonLogger);

				try
				{
					this.ProcessProject(projectInfo, logger);
				}
				catch (Exception ex)
				{
					string msg = $"ERROR: {projectInfo.VssKey}\r\n{ex}";

					this.errorLogger.WriteLine(msg);
					logger.WriteLine(msg);

					logger.Dispose();
				}
			}

			private void ProcessProject(ProjectInfo projectInfo, Logger logger)
			{
				this.repoInfo = new RepoInfo(logger, projectInfo.VssKey, projectInfo.Project);

				this.repoInfo.RevisionAnalyzer = new RevisionAnalyzer(this.workQueue, logger, this.db);
				if (!string.IsNullOrEmpty(this.form.excludeTextBox.Text))
				{
					this.repoInfo.RevisionAnalyzer.ExcludeFiles = this.form.excludeTextBox.Text;
				}
				this.repoInfo.RevisionAnalyzer.AddItem(projectInfo.Project);

				this.repoInfo.ChangesetBuilder = new ChangesetBuilder(this.workQueue, logger, this.repoInfo.RevisionAnalyzer);
				this.repoInfo.ChangesetBuilder.AnyCommentThreshold = TimeSpan.FromSeconds((double)this.form.anyCommentUpDown.Value);
				this.repoInfo.ChangesetBuilder.SameCommentThreshold = TimeSpan.FromSeconds((double)this.form.sameCommentUpDown.Value);
				this.repoInfo.ChangesetBuilder.BuildChangesets();

				GitExporter gitExporter = new GitExporter(this.workQueue, logger, this.repoInfo.RevisionAnalyzer, this.repoInfo.ChangesetBuilder);
				if (!string.IsNullOrEmpty(this.form.domainTextBox.Text))
				{
					gitExporter.EmailDomain = this.form.domainTextBox.Text;
				}
				if (!string.IsNullOrEmpty(this.form.commentTextBox.Text))
				{
					gitExporter.DefaultComment = this.form.commentTextBox.Text;
				}
				if (!this.form.transcodeCheckBox.Checked)
				{
					gitExporter.CommitEncoding = this.selectedEncoding;
				}
				gitExporter.IgnoreErrors = this.form.ignoreErrorsCheckBox.Checked;
				gitExporter.ExportToGit(processDir);
			}

			private void DeleteProcessingFolderAndLog()
			{
				// clear processing folder
				try
				{
					if (Directory.Exists(this.processDir))
					{
						ClearDir(new DirectoryInfo(this.processDir), true);
					}
				}
				catch (Exception ex)
				{
					throw new Exception($"Ошибка при удалении папки '{this.processDir}'.", ex);
				}

				// delete processing log file
				try
				{
					if (File.Exists(this.logFile))
					{
						File.Delete(this.logFile);
					}
				}
				catch (Exception ex)
				{
					throw new Exception($"Ошибка при удалении файла '{this.logFile}'.", ex);
				}
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

			public void PostProcess()
			{
				if (this.repoInfo == null) return;

				try
				{
					if (this.repoInfo.Canceled) return;

					ClearDir(new DirectoryInfo(this.processDir), false, path => !path.StartsWith(".git"));

					ICollection<Exception> exceptions = this.workQueue.FetchExceptions();
					if (exceptions != null)
					{
						foreach (Exception exception in exceptions)
						{
							string msg = $"[ERROR] {exception}";
							this.repoInfo.Logger.WriteLine(msg);
							this.errorLogger.WriteLine(msg);
						}
					}

					bool isSuccess = exceptions == null || exceptions.Count == 0;
					string projPath = GetProjectPath(this.outputDir, this.repoInfo.VssKey, isSuccess);

					string moveDir = Path.GetDirectoryName(projPath);
					if (moveDir != null && !Directory.Exists(moveDir))
					{
						Directory.CreateDirectory(moveDir);
					}

					try
					{
						Directory.Move(this.processDir, projPath);
					}
					catch (Exception ex)
					{
						throw new Exception($"Ошибка при перемещении папки '{this.processDir}' в '{projPath}'.", ex);
					}
				
					// move log file
					this.repoInfo.Dispose();

					if (File.Exists(this.logFile))
					{
						string newLogPath = Path.Combine(projPath, "_vss2git.log");
						File.Move(this.logFile, newLogPath);
					}
				}
				catch (Exception ex)
				{
					string msg = $"POSTPROCESS ERROR: {this.repoInfo.VssKey}\r\n{ex}";
					if (this.repoInfo.Logger != null)
					{
						this.repoInfo.Logger.WriteLine(msg);
					}
					this.errorLogger.WriteLine(msg);
					throw;
				}
				finally
				{
					if (this.repoInfo != null)
					{
						this.repoInfo.Dispose();
						this.repoInfo = null;
					}
				}
			}

			public string GetProject()
			{
				return this.repoInfo?.VssKey.ToString();
			}

			public void SetCancelled()
			{
				this.Canceled = true;
				if (this.repoInfo != null)
				{
					this.repoInfo.Canceled = true;
				}
			}

			public void Dispose()
			{
				this.errorLogger.Dispose();
				FileInfo fileInfo = new FileInfo(this.errorLogger.Filename);
				if (fileInfo.Length == 0)
				{
					fileInfo.Delete();
				}

				this.commonLogger.Dispose();

				if (this.repoInfo != null)
				{
					this.repoInfo.Dispose();
					this.repoInfo = null;
				}
			}

			private class RepoInfo : IDisposable
			{
				public RevisionAnalyzer RevisionAnalyzer;
				public ChangesetBuilder ChangesetBuilder;
				public Logger Logger { get; private set; }
				public readonly VssKey VssKey;
				public readonly VssProject Project;
				public bool Canceled;

				public RepoInfo(Logger logger, VssKey vssKey, VssProject project)
				{
					this.Logger = logger;
					this.VssKey = vssKey;
					this.Project = project;
				}

				public void Dispose()
				{
					if (this.Logger != null)
					{
						this.Logger.Dispose();
						this.Logger = null;
					}
				}
			}
		}

		public class VssKey
		{
			public readonly string VssPath;
			public readonly string PhysicalName;

			public VssKey(string vssPath, string physicalName)
			{
				this.VssPath = vssPath;
				this.PhysicalName = physicalName;
			}

			public static VssKey FromCombinedPath(string path)
			{
				string[] parts = path.Split('|');
				string vssPath = parts[0];
				string physicalName = null;
				if (parts.Length > 1)
				{
					physicalName = parts[1];
				}
				return new VssKey(vssPath, physicalName);
			}

			public string ToCombinedPath()
			{
				if (this.PhysicalName == null)
				{
					return this.VssPath;
				}
				return $"{this.VssPath}|{this.PhysicalName}";
			}

			public override string ToString()
			{
				if (string.IsNullOrEmpty(this.PhysicalName))
				{
					return this.VssPath;
				}
				return $"{this.VssPath} ({this.PhysicalName})";
			}
		}

		public class ProjectInfo
		{
			public readonly VssKey VssKey;
			public readonly VssProject Project;

			public ProjectInfo(VssKey vssKey, VssProject project)
			{
				this.VssKey = vssKey;
				this.Project = project;
			}
		}
	}
}
