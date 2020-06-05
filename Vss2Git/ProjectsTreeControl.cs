using System;
using System.Collections.Specialized;
using System.ComponentModel;
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

		private StringCollection selectedPaths;
		[Browsable(false)]
		[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
		public StringCollection SelectedPaths
		{
			get
			{
				StringCollection collection = new StringCollection();
				CollectCheckedNodeNames(collection, this.tvProjects.Nodes);
				return collection;
			}
			set
			{
				this.selectedPaths = value;
				this.UpdateCheckedNodeNames();
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

		private void UpdateCheckedNodeNames()
		{
			this.internalUpdate = true;
			this.UpdateCheckedNodeNames(this.selectedPaths, this.tvProjects.Nodes);
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

		private void UpdateCheckedNodeNames(StringCollection collection, TreeNodeCollection nodes)
		{
			foreach (TreeNode node in nodes)
			{
				NodeInfo info = (NodeInfo)node.Tag;
				if (info != null)
				{
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
				this.UpdateCheckedNodeNames(collection, node.Nodes);
			}
		}

		private void btnRefreshProjects_Click(object sender, EventArgs e)
		{
			this.RefreshProjects();
		}

		private TreeNode AddNode(TreeNodeCollection nodes, VssProject project, string name)
		{
			TreeNode node = nodes.Add(name);
			NodeInfo info = new NodeInfo(project);

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

		private bool isExpanding;
		private void tvProjects_BeforeExpand(object sender, TreeViewCancelEventArgs e)
		{
			this.isExpanding = true;
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
					this.UpdateCheckedNodeNames();
				}
			}
			this.isExpanding = false;
		}

		private bool internalUpdate;
		private void tvProjects_AfterCheck(object sender, TreeViewEventArgs e)
		{
			if (this.internalUpdate) return;
			this.CheckedChanged?.Invoke(this, EventArgs.Empty);
		}

		private class NodeInfo
		{
			public readonly VssProject Project;
			public bool IsFilled;
			public NodeInfo(VssProject project)
			{
				this.Project = project;
			}
		}
	}
}
