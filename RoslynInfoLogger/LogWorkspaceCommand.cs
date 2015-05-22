using Microsoft.CodeAnalysis;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using System;
using System.Linq;
using System.Threading;
using System.Xml.Linq;

namespace RoslynInfoLogger
{
    internal static class LogWorkspaceCommand
    {
        public static void LogInfo(IServiceProvider serviceProvider, string path, CancellationToken cancellationToken)
        {
            var componentModel = (IComponentModel)serviceProvider.GetService(typeof(SComponentModel));
            var workspace = componentModel.GetService<VisualStudioWorkspace>();

            var document = new XDocument();
            var workspaceElement = new XElement("workspace");
            document.Add(workspaceElement);

            foreach (var project in workspace.CurrentSolution.Projects)
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

                var compilation = project.GetCompilationAsync(cancellationToken).Result;
                var compilationReferencesElement = new XElement("compilationReferences");
                projectElement.Add(compilationReferencesElement);

                foreach (var reference in compilation.References)
                {
                    compilationReferencesElement.Add(CreateElementForPortableExecutableReference(reference));
                }

                var diagnosticsElement = new XElement("diagnostics");
                projectElement.Add(diagnosticsElement);

                foreach (var diagnostic in compilation.GetDiagnostics(cancellationToken))
                {
                    diagnosticsElement.Add(new XElement("diagnostic", diagnostic.GetMessage()));
                }
            }

            document.Save(path);
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
    }
}