# Family Browser

Search the office family library and load families into the open project.

## Finding a family

- Click the search box: a list opens with the categories and the tags.
- Type words: every word must appear in the family name or its tags, in any order. Typing also narrows the list of categories and suggests matching tags.
- **Categories**: tick one or more to see families in any of them. With none ticked you see every category. "(No category)" holds families that have not been scanned yet. The number next to each is how many families you would see.
- **Tags**: add a tag to see only families that have it. Add several and a family needs all of them. Enter adds the highlighted tag.
- Every family also has tags of its own that you can't remove: **Project** (loaded in the open project, after ⟳), **Library** (in the library) and **Fix requested** (someone asked for a fix). Add **Project** and **Library** together for families that are in both.
- Ticked categories and added tags show as chips in the box. ✕ on a chip removes it, Backspace in an empty box removes the last one, and ✕ at the right clears everything.
- ⭐ next to the box shows only your favourites.
- Click a tag under a family's name to add it to the search.
- The grid button at the bottom switches to large thumbnails.

## Checking the project (⟳)

⟳ compares the library with the open project. It runs only when you press it, and after a Scan.

- ✓ means the family is loaded and up to date.
- RVTuk notes which library file a family was loaded from, its size and date, whenever it is loaded, also with Revit's own Load Family. ⟳ then shows **↑ Update** when that library file changed since, even if its _Version wasn't raised.
- **?** means the family is loaded, but RVTuk can't tell whether it matches the library: it was loaded before RVTuk noted this, or from outside the library, and there is no _Version to compare. Load it once from the browser and it is checked from then on.
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

## Asking the maintainer

- **Request a fix** (under Load) tells the library maintainer that the selected family needs fixing or changing. Describe what is wrong. If you already changed the family in your project, tick **Attach my project's version** so the maintainer can see your change.
- **Request family** (bottom of the list) asks for a family the library doesn't have. It is also offered when a search finds nothing.
- **Suggest for Library** (on a family that is in your project but not in the library, where the maintainer has **Save to Library**) sends that family itself to the maintainer, with an optional note. If the maintainer approves it, it is added to the library and appears after the next Scan.
- The inbox button at the bottom lists every open request, with their number on it. Check it before asking for the same thing twice. You can **Withdraw** your own requests.
- A family with an open fix request shows 🔧 in the list, the **Fix requested** tag, and the request above its instructions.
- Your Windows user name and the date are added. The maintainer sees a request once Desktop Connector has uploaded it, usually within a minute, and marks it **Done** when it is handled.

## View-only access

Most people can only view the library, so only its maintainer can scan or edit it. In that case:
- the browser reads a copy of the library kept on your PC. The note at the bottom shows its date, and the copy updates by itself when the library changes;
- "Offline copy" means the library could not be reached, for example because Desktop Connector is not running. You can still browse and load families you already have;
- the Scan, Rescan and Edit Info buttons are hidden or turned off, and Save to Library becomes **Suggest for Library**;
- ⭐ favourites are your own and stay on your PC.

You need Autodesk Desktop Connector, signed in with your Autodesk account.

## Settings (⚙️)

- **Library root folder** is the folder with the office families. Its .DB subfolder holds the database everyone shares.
- **Scan** adds new families and removes deleted ones. Tick **Update thumbnails** or **Update parameters** to re-read those too. Parameters are read through Revit, which is slower. Instructions, tags, favourites and custom thumbnails are always kept.
- **Ignored subfolders** and **Ignored files** hide families, for example archives or Revit backups, without deleting their data.
- **Install office shortcuts** replaces your Revit keyboard shortcuts, in every Revit version on your PC, with the office's. Your old shortcuts are kept as a backup file next to them (`KeyboardShortcuts.before-RVTuk-….xml` in your Revit settings folder). Restart Revit to use them.
- **Publish my shortcuts** (maintainer only) makes your own shortcuts, as set in Revit's Keyboard Shortcuts dialog, the office's. They are kept as `KeyboardShortcuts.xml` in the library's .DB folder.

## When something goes wrong

- "The active window is a family": switch to the project window and try again.
- "Revit did not accept the request": finish or cancel what Revit is doing, then try again.
- The list is empty: remove some chips or clear the search, press ⟳, or run a Scan if the library is new.
- "The request was not sent": check that Desktop Connector is running, and ask the maintainer to let you add files to the library's .Requests folder.
- Anything else: contact the RVTuk team.
