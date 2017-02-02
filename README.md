# Roslyn Info Logger
A Visual Studio extension that logs info from the Roslyn workspace to help in
debugging Roslyn. Unless you're actually debugging Roslyn there's absolutely
no reason to go installing this.

## Instructions
Once you've installed the extension, a new set of commands under `Tools > Roslyn Information`:

- **Log Workspace Structure:** This will create a file RoslynWorkspaceStructure.xml on your desktop. This contains a high-level
  structure of the workspce state as viewed from different layers, helping diagnose which layer something went wrong in.
  This file that can be e-mailed or attached to a bug, although it can often be large so zipping it is a good idea.
  There's no dialog telling you it did this, so once you've clicked the command and it's completed start looking on your desktop for the file.

- **Show Workspace Change Event Counts:** This will show a tool window that counts various types of workspace events. This is handy
  when you're seeing a performance issue and want to see if some flurry of events is causing it. The reset button resets all counters.
  Make a screenshot to send the data to somebody else.