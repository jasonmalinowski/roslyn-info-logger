using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;

namespace RoslynInfoLogger
{
    [Guid("5f3a6ac6-b4db-418c-809e-457930135c7d")]
    public class WorkspaceChangeEventCountToolWindow : ToolWindowPane
    {
        public WorkspaceChangeEventCountToolWindow()
        {
        }

        protected override void Initialize()
        {
            var componentModel = (IComponentModel)GetService(typeof(SComponentModel));

            Caption = "Roslyn Workspace Change Event Counts";
            Content = new WorkspaceChangeEventCountToolWindowControl(componentModel.GetService<VisualStudioWorkspace>());
        }
    }
}
