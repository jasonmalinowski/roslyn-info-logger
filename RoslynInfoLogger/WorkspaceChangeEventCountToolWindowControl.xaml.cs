﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.Shell;

namespace RoslynInfoLogger
{
    public partial class WorkspaceChangeEventCountToolWindowControl : UserControl
    {
        private readonly TaskScheduler _foregroundTaskScheduler;

        private readonly object _gate = new object();
        private bool _refreshPending = false;
        private readonly Dictionary<string, int> _workspaceChangesByKind = new Dictionary<string, int>();

        public WorkspaceChangeEventCountToolWindowControl(Workspace workspace)
        {
            this.InitializeComponent();

            _foregroundTaskScheduler = TaskScheduler.FromCurrentSynchronizationContext();
            workspace.WorkspaceChanged += Workspace_WorkspaceChanged;

            RefreshDisplay();
        }

        private void RefreshDisplay()
        {
            lock (_gate)
            {
                StringBuilder builder = new StringBuilder();

                foreach (var changesByKind in _workspaceChangesByKind)
                {
                    builder.AppendLine($"{changesByKind.Key}: {changesByKind.Value}");
                }

                WorkspaceEvents.Text = builder.ToString();

                _refreshPending = false;
            }
        }

        private void QueueRefreshWhileHoldingLock()
        {
            if (!_refreshPending)
            {
                _refreshPending = true;

                System.Threading.Tasks.Task.Factory.StartNew(RefreshDisplay, CancellationToken.None, TaskCreationOptions.None, _foregroundTaskScheduler);
            }
        }

        private void Workspace_WorkspaceChanged(object sender, WorkspaceChangeEventArgs e)
        {
            lock (_gate)
            {
                string workspaceChangeKindString = e.Kind.ToString();

                // Let's see if we can classify this better
                if (e.Kind == WorkspaceChangeKind.ProjectChanged)
                {
                    var oldProject = e.OldSolution.GetProject(e.ProjectId);
                    var newProject = e.NewSolution.GetProject(e.ProjectId);

                    var projectChangeKinds = new List<string>();

                    var changes = newProject.GetChanges(oldProject);

                    if (changes.GetAddedProjectReferences().Any())
                    {
                        projectChangeKinds.Add("added ProjectReferences");
                    }

                    if (changes.GetRemovedProjectReferences().Any())
                    {
                        projectChangeKinds.Add("removed ProjectReferences");
                    }

                    if (changes.GetAddedMetadataReferences().Any())
                    {
                        projectChangeKinds.Add("add MetadataReferences");
                    }
                    
                    if (changes.GetRemovedMetadataReferences().Any())
                    {
                        projectChangeKinds.Add("removed MetadataReferences");
                    }
                    
                    if (newProject.SupportsCompilation && !object.Equals(oldProject.CompilationOptions, newProject.CompilationOptions))
                    {
                        projectChangeKinds.Add("CompilationOptions changed");
                    }

                    if (newProject.SupportsCompilation && !object.Equals(oldProject.ParseOptions, newProject.ParseOptions))
                    {
                        projectChangeKinds.Add("ParseOptions changed");
                    }

                    if (oldProject.OutputFilePath != newProject.OutputFilePath)
                    {
                        projectChangeKinds.Add("OutputFilePath changed");
                    }

                    if (projectChangeKinds.Count == 0)
                    {
                        projectChangeKinds.Add("unclassified");
                    }

                    workspaceChangeKindString += " (" + string.Join(", ", projectChangeKinds) + ")";
                }

                int workspaceChangesOfKind;
                _workspaceChangesByKind.TryGetValue(workspaceChangeKindString, out workspaceChangesOfKind);
                _workspaceChangesByKind[workspaceChangeKindString] = workspaceChangesOfKind + 1;

                QueueRefreshWhileHoldingLock();
            }
        }

        private void ResetButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            lock (_gate)
            {
                _workspaceChangesByKind.Clear();

                QueueRefreshWhileHoldingLock();
            }
        }
    }
}