using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Linq;
using System.Threading;
using System.Xml.Linq;

namespace RoslynInfoLogger
{
    internal static class LogWorkspaceCommand
    {
        public static void LogInfo(IServiceProvider serviceProvider, string path)
        {
            var componentModel = (IComponentModel)serviceProvider.GetService(typeof(SComponentModel));
            var workspace = componentModel.GetService<VisualStudioWorkspace>();
            var solution = workspace.CurrentSolution;

            var threadedWaitDialog = (IVsThreadedWaitDialog3)serviceProvider.GetService(typeof(SVsThreadedWaitDialog));
            var threadedWaitCallback = new ThreadedWaitCallback();

            int projectsProcessed = 0;
            threadedWaitDialog.StartWaitDialogWithCallback("Roslyn Info Logger", "Logging workspace information...", null, null, null, true, 0, true, solution.ProjectIds.Count, 0, threadedWaitCallback);

            try
            {
                var document = new XDocument();
                var workspaceElement = new XElement("workspace");
                document.Add(workspaceElement);

                foreach (var project in solution.GetProjectDependencyGraph().GetTopologicallySortedProjects(threadedWaitCallback.CancellationToken).Select(solution.GetProject))
                {
                    var projectElement = new XElement("project");
                    workspaceElement.Add(projectElement);

                    projectElement.SetAttributeValue("id", project.Id.ToString());
                    projectElement.SetAttributeValue("name", project.Name);
                    projectElement.SetAttributeValue("assemblyName", project.AssemblyName);
                    projectElement.SetAttributeValue("language", project.Language);
                    projectElement.SetAttributeValue("path", project.FilePath ?? "(none)");

                    var workspaceReferencesElement = new XElement("workspaceReferences");
                    projectElement.Add(workspaceReferencesElement);

                    foreach (var metadataReference in project.MetadataReferences)
                    {
                        workspaceReferencesElement.Add(CreateElementForPortableExecutableReference(metadataReference));
                    }

                    foreach (var projectReference in project.AllProjectReferences)
                    {
                        var referenceElement = new XElement("projectReference", new XAttribute("id", projectReference.ProjectId.ToString()));

                        if (!project.ProjectReferences.Contains(projectReference))
                        {
                            referenceElement.SetAttributeValue("missingInSolution", "true");
                        }

                        workspaceReferencesElement.Add(referenceElement);
                    }

                    var compilation = project.GetCompilationAsync(threadedWaitCallback.CancellationToken).Result;
                    var compilationReferencesElement = new XElement("compilationReferences");
                    projectElement.Add(compilationReferencesElement);

                    foreach (var reference in compilation.References)
                    {
                        compilationReferencesElement.Add(CreateElementForPortableExecutableReference(reference));
                    }

                    var diagnosticsElement = new XElement("diagnostics");
                    projectElement.Add(diagnosticsElement);

                    foreach (var diagnostic in compilation.GetDiagnostics(threadedWaitCallback.CancellationToken))
                    {
                        diagnosticsElement.Add(
                            new XElement("diagnostic", 
                                new XAttribute("severity", diagnostic.Severity.ToString()),
                                diagnostic.GetMessage()));
                    }

                    projectsProcessed++;

                    bool cancelled;
                    threadedWaitDialog.UpdateProgress(null, null, null, projectsProcessed, solution.ProjectIds.Count, false, out cancelled);
                }

                document.Save(path);
            }
            catch (OperationCanceledException)
            {
                // They cancelled
            }
            finally
            {
                int cancelled;
                threadedWaitDialog.EndWaitDialog(out cancelled);
            }
        }

        private static XElement CreateElementForPortableExecutableReference(MetadataReference reference)
        {
            var compilationReference = reference as CompilationReference;
            var portableExecutableReference = reference as PortableExecutableReference;

            if (compilationReference != null)
            {
                return new XElement("compilationReference", new XAttribute("assemblyName", compilationReference.Compilation.AssemblyName));
            }
            else if (portableExecutableReference != null)
            {
                return new XElement("peReference",
                    new XAttribute("file", portableExecutableReference.FilePath ?? "(none)"),
                    new XAttribute("display", portableExecutableReference.Display));
            }
            else
            {
                return new XElement("metadataReference", new XAttribute("display", reference.Display));
            }
        }

        private sealed class ThreadedWaitCallback : IVsThreadedWaitDialogCallback
        {
            private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

            public CancellationToken CancellationToken
            {
                get { return _cancellationTokenSource.Token; }
            }

            public void OnCanceled()
            {
                _cancellationTokenSource.Cancel();
            }
        }
    }
}