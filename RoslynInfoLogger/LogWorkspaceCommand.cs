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
            var dte = (EnvDTE.DTE)serviceProvider.GetService(typeof(SDTE));

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

                    projectElement.SetAttributeValue("id", SanitizePath(project.Id.ToString()));
                    projectElement.SetAttributeValue("name", project.Name);
                    projectElement.SetAttributeValue("assemblyName", project.AssemblyName);
                    projectElement.SetAttributeValue("language", project.Language);
                    projectElement.SetAttributeValue("path", SanitizePath(project.FilePath ?? "(none)"));

                    // Can we find a matching DTE project?
                    var langProjProject = TryFindLangProjProject(dte, project);

                    if (langProjProject != null)
                    {
                        var dteReferences = new XElement("dteReferences");
                        projectElement.Add(dteReferences);

                        foreach (var reference in langProjProject.References.Cast<VSLangProj.Reference>())
                        {
                            if (reference.SourceProject != null)
                            {
                                dteReferences.Add(new XElement("projectReference", new XAttribute("projectName", reference.SourceProject.Name)));
                            }
                            else
                            {
                                dteReferences.Add(new XElement("metadataReference", new XAttribute("path", SanitizePath(reference.Path))));
                            }
                        }
                    }

                    var workspaceReferencesElement = new XElement("workspaceReferences");
                    projectElement.Add(workspaceReferencesElement);

                    foreach (var metadataReference in project.MetadataReferences)
                    {
                        workspaceReferencesElement.Add(CreateElementForPortableExecutableReference(metadataReference));
                    }

                    foreach (var projectReference in project.AllProjectReferences)
                    {
                        var referenceElement = new XElement("projectReference", new XAttribute("id", SanitizePath(projectReference.ProjectId.ToString())));

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

        private static VSLangProj.VSProject TryFindLangProjProject(EnvDTE.DTE dte, Project project)
        {
            var dteProject = dte.Solution.Projects.Cast<EnvDTE.Project>().FirstOrDefault(p => string.Equals(p.FullName, project.FilePath, StringComparison.OrdinalIgnoreCase));

            return dteProject?.Object as VSLangProj.VSProject;
        }

        private static string SanitizePath(string s)
        {
            return ReplacePathComponent(s, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%");
        }

        private static string ReplacePathComponent(string s, string oldValue, string newValue)
        {
            while (true)
            {
                int index = s.IndexOf(oldValue, StringComparison.OrdinalIgnoreCase);
                if (index == -1)
                {
                    return s;
                }

                s = s.Substring(0, index) + newValue + s.Substring(index + oldValue.Length);
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
                    new XAttribute("file", SanitizePath(portableExecutableReference.FilePath ?? "(none)")),
                    new XAttribute("display", SanitizePath(portableExecutableReference.Display)));
            }
            else
            {
                return new XElement("metadataReference", new XAttribute("display", SanitizePath(reference.Display)));
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