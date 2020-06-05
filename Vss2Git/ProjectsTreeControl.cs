using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.IO;
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

		[Browsable(false)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public string VSSDirectory
		{
			get;
			set;
		}

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
				CollectCheckedNodeNames(collection, this.tvProjects.Nodes);
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

		public void RefreshProjects()
		{
			VssDatabaseFactory df = new VssDatabaseFactory(this.VSSDirectory);
			df.Encoding = this.Encoding;
			VssDatabase db = df.Open();

			this.tvProjects.Nodes.Clear();
			if (db.RootProject != null)
			{
				TreeNode node = this.AddNode(this.tvProjects.Nodes, db.RootProject, db.RootProject.Path);
				node.Expand();
			}

			this.tvProjects.Enabled = true;
		}

		public void UpdateNodes()
		{
			this.internalUpdate = true;
			this.UpdateNodes(this.selectedPaths, this.tvProjects.Nodes);
			this.internalUpdate = false;
		}

		private static void CollectCheckedNodeNames(StringCollection collection, TreeNodeCollection nodes)
		{
			foreach (TreeNode node in nodes)
			{
				if (node.Checked)
				{
					NodeInfo info = (NodeInfo)node.Tag;
					collection.Add(info.Project.Path);
				}

				CollectCheckedNodeNames(collection, node.Nodes);
			}
		}

		private void UpdateNodes(StringCollection collection, TreeNodeCollection nodes)
		{
			foreach (TreeNode node in nodes)
			{
				NodeInfo info = (NodeInfo)node.Tag;
				if (info != null)
				{
					string path = MainForm.GetProjectPath(this.outputDirectory, info.Project.Path, true);
					info.AlreadyExists = Directory.Exists(path);
					if (info.AlreadyExists)
					{
						node.Text = $"{info.Name} [done]";
						node.ForeColor = Color.DarkGray;
					}
					else
					{
						node.Text = info.Name;
						node.ForeColor = Color.Black;
					}

					foreach (string projPath in collection)
					{
						if (projPath == info.Project.Path)
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

			bool hasProjects = false;
			foreach (VssProject p in project.Projects)
			{
				if (p.IsProject)
				{
					hasProjects = true;
					break;
				}
			}

			if (hasProjects)
			{
				node.Nodes.Add("");
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

				if (needUpdate)
				{
					this.UpdateNodes();
				}
			}
		}

		private bool internalUpdate;
		private string outputDirectory;

		private void tvProjects_AfterCheck(object sender, TreeViewEventArgs e)
		{
			if (this.internalUpdate) return;
			this.CheckedChanged?.Invoke(this, EventArgs.Empty);
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
		}
	}
}
