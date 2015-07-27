# Roslyn Info Logger
A Visual Studio extension that logs info from the Roslyn workspace to help in
debugging Roslyn.  Unless you're actually debugging Roslyn there's absolutely
no reason to go installing this.

## Instructions
Once you've installed the extension, there's a new command "Log Roslyn
Workspace Info" at or near the top of the Tools menu.  If you're encountering
an issue where a Roslyn developer has told you to use this tool, reproduce the
issue first, and then run this command. It'll create a file
RoslynWorkspaceInfo.xml on your desktop that can be e-mailed or attached to a
bug as appropriate. There's no dialog telling you it did this, so once you've
clicked the command start looking on your desktop for the file.
