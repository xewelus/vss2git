using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Hpdi.VssLogicalLib;

namespace Hpdi.Vss2Git
{
	public partial class ProjectsTreeControl : UserControl
	{
		public ProjectsTreeControl()
		{
			this.InitializeComponent();
		}

		public event EventHandler CheckedChanged;
		private bool internalUpdate;

		[Browsable(false)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string VSSDirectory
		{
			get;
			set;
		}

		private string outputDirectory;
		[Browsable(false)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string OutputDirectory
		{
			get
			{
				return this.outputDirectory;
			}
			set
			{
				this.outputDirectory = value;
				this.UpdateNodes();
			}
		}

		private StringCollection selectedPaths;

		[Browsable(false)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public StringCollection SelectedPaths
		{
			get
			{
				StringCollection collection = new StringCollection();
				CollectChecked(collection, null, this.tvProjects.Nodes, this.projectsByPaths);
				this.selectedPaths = collection;
				return collection;
			}
			set
			{
				this.selectedPaths = value;
				this.UpdateNodes();
			}
		}

		[Browsable(false)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public Encoding Encoding
		{
			get;
			set;
		}

		private bool canCheck = true;
		[Browsable(false)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public bool CanCheck
		{
			get => this.canCheck;
			set => this.canCheck = value;
		}

		private readonly Dictionary<string, List<VssProject>> projectsByPaths = new Dictionary<string, List<VssProject>>();

		public void RefreshProjects()
		{
			try
			{
				this.internalUpdate = true;

				VssDatabaseFactory df = new VssDatabaseFactory(this.VSSDirectory);
				df.Encoding = this.Encoding;
				VssDatabase db = df.Open();

				this.projectsByPaths.Clear();
				this.tvProjects.Nodes.Clear();
				if (db.RootProject != null)
				{
					TreeNode node = this.AddNode(this.tvProjects.Nodes, db.RootProject, db.RootProject.Path);
					node.Expand();
				}

				this.tvProjects.Enabled = true;
			}
			finally
			{
				this.internalUpdate = false;
			}
		}

		public void UpdateNodes()
		{
			bool prev = this.internalUpdate;
			try
			{
				this.internalUpdate = true;
				this.UpdateNodes(this.selectedPaths, this.tvProjects.Nodes);
			}
			finally
			{
				this.internalUpdate = prev;
			}
		}

		public List<MainForm.ProjectInfo> GetSelectedProjects()
		{
			List<MainForm.ProjectInfo> projects = new List<MainForm.ProjectInfo>();
			CollectChecked(null, projects, this.tvProjects.Nodes, this.projectsByPaths);
			return projects;
		}

		private static void CollectChecked(StringCollection collection, List<MainForm.ProjectInfo> projects, TreeNodeCollection nodes, Dictionary<string, List<VssProject>> projectsByPaths)
		{
			foreach (TreeNode node in nodes)
			{
				if (node.Checked)
				{
					NodeInfo info = (NodeInfo)node.Tag;
					if (info.Project != null)
					{
						MainForm.VssKey vssKey = info.GetKey(projectsByPaths);
						if (collection != null)
						{
							collection.Add(vssKey.ToCombinedPath());
						}

						if (projects != null)
						{
							projects.Add(new MainForm.ProjectInfo(vssKey, info.Project));
						}
					}
				}

				CollectChecked(collection, projects, node.Nodes, projectsByPaths);
			}
		}

		private void UpdateNodes(StringCollection collection, TreeNodeCollection nodes)
		{
			foreach (TreeNode node in nodes)
			{
				NodeInfo info = (NodeInfo)node.Tag;
				if (info?.Project != null)
				{
					MainForm.VssKey key = info.GetKey(this.projectsByPaths);

					string name = info.Name;
					if (key.PhysicalName != null)
					{
						name += $" ({key.PhysicalName})";
					}

					string path = MainForm.GetProjectPath(this.outputDirectory, key, true);
					info.AlreadyExists = Directory.Exists(path);
					if (info.AlreadyExists)
					{
						node.Text = $"{name} [done]";
						node.ForeColor = Color.DarkGray;
					}
					else
					{
						node.Text = name;
						node.ForeColor = Color.Black;
					}

					foreach (string projPath in collection)
					{
						string combinedPath = key.ToCombinedPath();
						if (projPath == combinedPath)
						{
							node.Checked = true;
						}
						else if (projPath.Contains(info.Project.Path + "/"))
						{
							node.Expand();
						}
					}
				}
				this.UpdateNodes(collection, node.Nodes);
			}
		}

		private void btnRefreshProjects_Click(object sender, EventArgs e)
		{
			this.RefreshProjects();
		}

		private TreeNode AddNode(TreeNodeCollection nodes, VssProject project, string name)
		{
			TreeNode node = nodes.Add(name);
			NodeInfo info = new NodeInfo(project, name);
			if (project == null)
			{
				node.ForeColor = Color.DarkGray;
			}
			else
			{
				bool hasSome = false;
				foreach (VssProject p in project.Projects)
				{
					if (p.IsProject)
					{
						hasSome = true;
						break;
					}
				}
				if (project.Files.Any())
				{
					hasSome = true;
				}

				if (hasSome)
				{
					node.Nodes.Add("");
				}

				List<VssProject> projects;
				if (!this.projectsByPaths.TryGetValue(project.Path, out projects))
				{
					projects = new List<VssProject>();
					this.projectsByPaths.Add(project.Path, projects);
				}
				projects.Add(project);
			}

			node.Tag = info;
			return node;
		}

		private void tvProjects_BeforeExpand(object sender, TreeViewCancelEventArgs e)
		{
			NodeInfo info = (NodeInfo)e.Node.Tag;
			if (!info.IsFilled)
			{
				info.IsFilled = true;

				bool needUpdate = false;
				e.Node.Nodes.Clear();
				foreach (VssProject p in info.Project.Projects)
				{
					if (p.IsProject)
					{
						this.AddNode(e.Node.Nodes, p, p.Name);
						needUpdate = true;
					}
				}

				foreach (VssFile file in info.Project.Files)
				{
					this.AddNode(e.Node.Nodes, null, file.Name);
				}

				if (needUpdate)
				{
					this.UpdateNodes();
				}
			}
		}

		private void tvProjects_AfterCheck(object sender, TreeViewEventArgs e)
		{
			if (this.internalUpdate) return;
			this.CheckedChanged?.Invoke(this, EventArgs.Empty);
		}

		private void tvProjects_BeforeCheck(object sender, TreeViewCancelEventArgs e)
		{
			if (this.internalUpdate) return;
			if (!this.CanCheck)
			{
				e.Cancel = true;
				return;
			}

			NodeInfo info = (NodeInfo)e.Node.Tag;
			if (info.Project == null)
			{
				e.Cancel = true;
			}
		}

		private class NodeInfo
		{
			public readonly VssProject Project;
			public readonly string Name;
			public bool IsFilled;
			public bool AlreadyExists;
			public NodeInfo(VssProject project, string name)
			{
				this.Project = project;
				this.Name = name;
			}

			public MainForm.VssKey GetKey(Dictionary<string, List<VssProject>> projectsByPaths)
			{
				List<VssProject> projects;
				if (projectsByPaths.TryGetValue(this.Project.Path, out projects))
				{
					foreach (VssProject project in projects)
					{
						if (project != this.Project)
						{
							return new MainForm.VssKey(this.Project.Path, this.Project.PhysicalName);
						}
					}
				}
				return new MainForm.VssKey(this.Project.Path, null);
			}
		}
	}

	public class FixesTreeView : TreeView
	{
		protected override void WndProc(ref Message m)
		{
			if (m.Msg == 0x203) // identified double click
			{
				var localPos = this.PointToClient(Cursor.Position);
				var hitTestInfo = this.HitTest(localPos);
				if (hitTestInfo.Location == TreeViewHitTestLocations.StateImage)
					m.Result = IntPtr.Zero;
				else
					base.WndProc(ref m);
			}
			else base.WndProc(ref m);
		}
	}
}
