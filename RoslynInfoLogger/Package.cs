using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using System.ComponentModel.Design;
using System.IO;
using System.Threading;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio;

namespace RoslynInfoLogger
{
    [Guid("8f4299ec-e098-42b5-8f9b-025639f8c44c")]
    [PackageRegistration(UseManagedResourcesOnly = true)]
    [ProvideMenuResource("Menus.ctmenu", 4)]
    [ProvideToolWindow(typeof(WorkspaceChangeEventCountToolWindow), Transient = true, Orientation = ToolWindowOrientation.Bottom)]
    internal sealed class Package : Microsoft.VisualStudio.Shell.Package
    {
        protected override void Initialize()
        {
            base.Initialize();

            var commandService = (OleMenuCommandService)GetService(typeof(IMenuCommandService));

            commandService.AddCommand(
                new MenuCommand(LogWorkspaceStructureCommandHandler,
                new CommandID(CommandIds.CommandSet, CommandIds.LogStructureCommandId)));

            commandService.AddCommand(
                new MenuCommand(ShowEventCountToolWindowCommandHandler,
                new CommandID(CommandIds.CommandSet, CommandIds.ShowEventCountToolWindowCommandId)));
        }

        private void LogWorkspaceStructureCommandHandler(object sender, EventArgs e)
        {
            string temporaryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "RoslynWorkspaceStructure.xml");

            LogWorkspaceStructureCommand.LogInfo(this, temporaryPath);
        }

        private void ShowEventCountToolWindowCommandHandler(object sender, EventArgs e)
        {
            ToolWindowPane window = this.FindToolWindow(typeof(WorkspaceChangeEventCountToolWindow), id: 0, create: true);

            IVsWindowFrame windowFrame = (IVsWindowFrame)window.Frame;
            ErrorHandler.ThrowOnFailure(windowFrame.Show());
        }
    }
}
