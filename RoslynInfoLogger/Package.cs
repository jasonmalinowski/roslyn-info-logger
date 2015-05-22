using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;
using System.ComponentModel.Design;
using System.IO;

namespace RoslynInfoLogger
{
    [Guid("8f4299ec-e098-42b5-8f9b-025639f8c44c")]
    [PackageRegistration(UseManagedResourcesOnly = true)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    internal sealed class Package : Microsoft.VisualStudio.Shell.Package
    {
        protected override void Initialize()
        {
            base.Initialize();

            var commandService = (OleMenuCommandService)GetService(typeof(IMenuCommandService));

            var menuCommandID = new CommandID(CommandIds.CommandSet, CommandIds.LogInfoCommandId);
            EventHandler eventHandler = LogWorkspaceInfoCommandHandler;
            var menuItem = new MenuCommand(eventHandler, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        private void LogWorkspaceInfoCommandHandler(object sender, EventArgs e)
        {
            string temporaryPath = Path.Combine(Path.GetTempPath(), "RoslynWorkspaceInfo.xml");

            LogWorkspaceCommand.LogInfo(this, temporaryPath);
        }
    }
}
