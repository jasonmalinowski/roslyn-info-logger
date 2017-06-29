using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Xml.Linq;
using System.Threading.Tasks;

namespace RoslynInfoLogger
{
    internal static class LogWorkspaceStructureCommand
    {
        private static int s_NextCompilationId;
        private static readonly ConditionalWeakTable<Compilation, StrongBox<int>> s_CompilationIds = new ConditionalWeakTable<Compilation, StrongBox<int>>();

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
                    projectElement.SetAttributeValue("outputPath", SanitizePath(project.OutputFilePath ?? "(none)"));

                    var hasSuccesfullyLoaded = TryGetHasSuccessfullyLoaded(project, threadedWaitCallback.CancellationToken);

                    if (hasSuccesfullyLoaded.HasValue)
                    {
                        projectElement.SetAttributeValue("hasSuccessfullyLoaded", hasSuccesfullyLoaded.Value);
                    }

                    if (project.FilePath != null)
                    {
                        var msbuildProject = XDocument.Load(project.FilePath);
                        var msbuildNamespace = XNamespace.Get("http://schemas.microsoft.com/developer/msbuild/2003");

                        var msbuildReferencesElement = new XElement("msbuildReferences");
                        projectElement.Add(msbuildReferencesElement);

                        msbuildReferencesElement.Add(msbuildProject.Descendants(msbuildNamespace + "ProjectReference"));
                        msbuildReferencesElement.Add(msbuildProject.Descendants(msbuildNamespace + "Reference"));
                        msbuildReferencesElement.Add(msbuildProject.Descendants(msbuildNamespace + "ReferencePath"));
                    }

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
                                dteReferences.Add(new XElement("metadataReference",
                                    new XAttribute("path", SanitizePath(reference.Path)),
                                    new XAttribute("name", reference.Name)));
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

                    projectElement.Add(CreateElementForCompilation(compilation));

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

        private static bool? TryGetHasSuccessfullyLoaded(Project project, CancellationToken cancellationToken)
        {
            // This method has not been made a public API, but is useful for analyzing some issues. It's only available in 1.3, so we'll support
            // downlevel scenarios too.
            var method = project.GetType().GetMethod("HasSuccessfullyLoadedAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (method == null)
            {
                return null;
            }

            var task = method.Invoke(project, new object[] { cancellationToken }) as Task<bool>;

            task.Wait(cancellationToken);

            return task.Result;
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
            var aliasesAttribute = new XAttribute("aliases", string.Join(",", reference.Properties.Aliases));
            var compilationReference = reference as CompilationReference;
            var portableExecutableReference = reference as PortableExecutableReference;

            if (compilationReference != null)
            {
                return new XElement("compilationReference",
                    aliasesAttribute,
                    CreateElementForCompilation(compilationReference.Compilation));
            }
            else if (portableExecutableReference != null)
            {
                return new XElement("peReference",
                    new XAttribute("file", SanitizePath(portableExecutableReference.FilePath ?? "(none)")),
                    new XAttribute("display", SanitizePath(portableExecutableReference.Display)),
                    aliasesAttribute);
            }
            else
            {
                return new XElement("metadataReference", new XAttribute("display", SanitizePath(reference.Display)));
            }
        }

        private static XElement CreateElementForCompilation(Compilation compilation)
        {
            StrongBox<int> compilationId;
            if (!s_CompilationIds.TryGetValue(compilation, out compilationId))
            {
                compilationId = new StrongBox<int>(s_NextCompilationId++);
                s_CompilationIds.Add(compilation, compilationId);
            }

            var namespaces = new Queue<INamespaceSymbol>();
            var typesElement = new XElement("types");

            namespaces.Enqueue(compilation.Assembly.GlobalNamespace);

            while (namespaces.Count > 0)
            {
                var @ns = namespaces.Dequeue();

                foreach (var type in @ns.GetTypeMembers())
                {
                    typesElement.Add(new XElement("type", new XAttribute("name", type.ToDisplayString())));
                }

                foreach (var childNamespace in @ns.GetNamespaceMembers())
                {
                    namespaces.Enqueue(childNamespace);
                }
            }

            return new XElement("compilation",
                new XAttribute("objectId", compilationId.Value),
                new XAttribute("assemblyIdentity", compilation.Assembly.Identity.ToString()),
                typesElement);
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