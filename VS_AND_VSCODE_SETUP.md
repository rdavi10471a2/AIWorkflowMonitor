# Visual Studio And VS Code Setup

The monitor is intentionally separate from the watched project. This keeps generated `Working`, `History`, archive, and telemetry files out of your application repository.

## VS Code

Open the parent folder:

```powershell
code "C:\VSCodeProjects\MonitorProject"
```

You should see both folders:

```text
Monitor
MyWatchedProject
```

This makes it easy to inspect the monitor and the watched project side by side.

## Visual Studio 2022

You can open:

- `C:\VSCodeProjects\MonitorProject\Monitor\AIWorkflowMonitor.sln` for the monitor
- the watched project's own `.sln` from its sibling folder

Keep them as separate solutions unless you intentionally want one combined workspace.

When creating the watched project in Visual Studio 2022, use:

```text
Location: C:\VSCodeProjects\MonitorProject
Project name: MyWatchedProject
Solution name: MyWatchedProject
```

For a simple one-project app, check `Place solution and project in the same directory`. The desired result is:

```text
C:\VSCodeProjects\MonitorProject\
  Monitor\
  MyWatchedProject\
    MyWatchedProject.sln
    MyWatchedProject.csproj
```

If that checkbox is unchecked, Visual Studio may create an extra nesting level. That can still work, but make sure `WorkflowSettings:ObservedRoot` points to the actual watched project root that contains the source files.

Avoid this:

```text
C:\VSCodeProjects\MonitorProject\
  Monitor\
    MyWatchedProject\
```

The watched project should be beside `Monitor`, not inside it.

## Recommended Folder Names

Any sibling project name is fine. These are examples:

```text
C:\VSCodeProjects\MonitorProject\Monitor
C:\VSCodeProjects\MonitorProject\MyApiProject
C:\VSCodeProjects\MonitorProject\MyDesktopApp
```

After changing the watched project, update only `WorkflowSettings:ObservedRoot` and rerun a refresh.

