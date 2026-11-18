# Family Browser

Search the office family library and load families into the open project.

## Finding a family

- Type in the search box. Every word must appear in the family name or its tags, in any order.
- **Category** shows only the ticked categories. "(No category)" holds families that have not been scanned yet.
- ⬅ shows families loaded in the open project, ➡ families in the library, ⭐ your favourites. Press ⬅ and ➡ together for families that are in both.
- Click a tag under a family's name to search for it.
- The grid button at the bottom switches to large thumbnails.

## Checking the project (⟳)

⟳ compares the library with the open project. It runs only when you press it, and after a Scan.

- ✓ means the family is loaded and up to date.
- **↑ Update** means the library copy has a newer _Version. Select the family and press **Update**, or press **↑ Update All** above the list.
- ⚠️ means the family is loaded in the project but is not in the library. Press **Save to Library** to add it. It appears in the list after the next Scan.
- ⚠️ on a family that is in the library means the library has more than one copy of it. Point at ⚠️ to see where. Update All skips it until the extra copies are removed.
- A red version number means the version parameter is an instance parameter. Change it to a type parameter.
- Update keeps the values set in the project.
- **↑ Update All** first lists what will change. While it runs, **Cancel** at the bottom stops it after the current family.
- After you switch to another project, press ⟳ again. Coming back to the project you checked shows its results again.
- "Revit is busy" means Revit did not start the request within 10 seconds, usually because a dialog is open. Close it and try again.

Switch to the project window first. The check does not work while a family is open in the Family Editor.

## Family actions

- **Load** loads the selected family into the open project and replaces the loaded copy. It reads **Update** when the library copy is newer.
- **Open in Family Editor** (top-right icon) opens the library file. A family that is only in the model opens from the model.
- **Edit Info** (top-right icon) edits the family's instructions, tags and thumbnail. Paste or drag pictures into the instructions. While editing, Ctrl+Click opens a link.
- **Rescan** (top-right icon) re-reads one family's parameters and thumbnail after its file changed.
- "Working…" at the bottom of the list means Revit or the library database is still busy with your last action.

## View-only access

Most people can only view the library, so only its maintainer can scan or edit it. In that case:
- the browser reads a copy of the library kept on your PC. The note at the bottom shows its date, and the copy updates by itself when the library changes;
- "Offline copy" means the library could not be reached, for example because Desktop Connector is not running. You can still browse and load families you already have;
- the Scan, Rescan, Edit Info and Save to Library buttons are hidden or turned off;
- ⭐ favourites are your own and stay on your PC.

You need Autodesk Desktop Connector, signed in with your Autodesk account.

## Settings (⚙️)

- **Library root folder** is the folder with the office families. Its .DB subfolder holds the database everyone shares.
- **Scan** adds new families and removes deleted ones. Tick **Update thumbnails** or **Update parameters** to re-read those too. Parameters are read through Revit, which is slower. Instructions, tags, favourites and custom thumbnails are always kept.
- **Ignored subfolders** and **Ignored files** hide families, for example archives or Revit backups, without deleting their data.

## When something goes wrong

- "The active window is a family": switch to the project window and try again.
- "Revit did not accept the request": finish or cancel what Revit is doing, then try again.
- The list is empty: clear the search and the filters, press ⟳, or run a Scan if the library is new.
- Anything else: contact the RVTuk team.
