# Native-Dialog Hand-off Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** "Edit…" buttons that close the DWG Export dialog and open the right native Revit dialog (PDF Export for rules/sets, Modify DWG/DXF Export Setup for DWG setups).

**Architecture:** One `Func<string,bool>` delegate ("pdf" | "dwgsetups") from the command into the view model; window closes when the post succeeds. No Core changes.

**Spec:** `docs/tools/dwg-exporter/specs/2026-07-16-native-dialog-handoff-design.md`

## Global Constraints

Same as the parent tool. No Core/test changes in this feature; verification is compilation (all three configs) + in-Revit checks.

---

### Task 1: VM delegate + window buttons + command wiring

**Files:**
- Modify: `src/RVTuk.UI/DwgExporter/ViewModels/DwgExportViewModel.cs`
- Modify: `src/RVTuk.UI/DwgExporter/Views/DwgExportWindow.xaml` + `.xaml.cs`
- Modify: `src/RVTuk.Revit/DwgExporter/Commands/DwgExportCommand.cs`

- [ ] **Step 1: VM** — add next to the other window-wired callbacks:

```csharp
        /// <summary>Hand-off to a native Revit dialog ("pdf" = PDF Export for naming rules and
        /// view/sheet sets, "dwgsetups" = Modify DWG/DXF Export Setup). Wired by the command;
        /// returns true when the command was posted (the window then closes — posted commands
        /// run only after this modal command ends).</summary>
        public Func<string, bool>? OpenNativeDialog { get; set; }
```

- [ ] **Step 2: XAML** — add `Edit…` buttons: range dropdown row and File Naming combo row get `Click="EditPdfDialog_Click"`, DWG setup combo row gets `Click="EditDwgSetups_Click"`; each combo wrapped in a DockPanel with the button docked right. Tooltips explain close-edit-reopen.

- [ ] **Step 3: Code-behind**:

```csharp
        private void EditPdfDialog_Click(object sender, RoutedEventArgs e) => HandOff("pdf");
        private void EditDwgSetups_Click(object sender, RoutedEventArgs e) => HandOff("dwgsetups");

        private void HandOff(string kind)
        {
            if (_vm.OpenNativeDialog?.Invoke(kind) == true)
            {
                Close(); // the posted native dialog opens once this modal command returns
            }
            else
            {
                MessageBox.Show(this,
                    "Couldn't open the native dialog on this Revit version.",
                    "DWG Export", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
```

- [ ] **Step 4: Command** — after constructing `vm`:

```csharp
                vm.OpenNativeDialog = kind =>
                {
                    try
                    {
                        var postable = kind == "pdf"
                            ? PostableCommand.ExportPDF
                            : PostableCommand.ExportOptionsExportSetupsDWGOrDXF;
                        var id = RevitCommandId.LookupPostableCommandId(postable);
                        if (id == null || !commandData.Application.CanPostCommand(id)) return false;
                        commandData.Application.PostCommand(id);
                        return true;
                    }
                    catch
                    {
                        return false; // another command already posted, or id unavailable
                    }
                };
```

- [ ] **Step 5: Build** all three configs — 0 errors. Run Core suite — unchanged, green.
- [ ] **Step 6: Commit** — `feat(dwg-exporter): Edit… hand-off buttons to native PDF/DWG-setup dialogs`

### Task 2: Docs

- [ ] Backlog: convert the three editor feature items to "shipped as hand-off"; README one-liner. Commit.
- [ ] In-Revit checklist (user): each button closes the window and opens the right dialog; rule/set/setup edits appear after reopening; selections restored.
