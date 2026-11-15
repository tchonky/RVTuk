"""Writes sanitized copies of the stored image instructions as test fixtures.

Opens the family database read-only. Real text and images never reach a file:
only sanitized markup is written, and a row that still holds anything
unexpected stops the script. Run: python -I sanitize_instructions.py
"""
import json, os, pathlib, re, sqlite3

ONE_PIXEL_PNG = ("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA"
                 "60e6kgAAAABJRU5ErkJggg==")
OLD_DECLARATION = "clr-namespace:RVTuk.UI.Controls;assembly=RVTuk.UI"
HIDDEN = {"Tag": "sample", "ToolTip": "sample", "Text": "Sample text",
          "NavigateUri": "https://example.com/"}
MARKUP_VALUE = re.compile(r"#[0-9A-Fa-f]{6,8}|[-\d.,]+|True|False")


def sanitize(xaml):
    xaml = re.sub(r'ImageData="[^"]*"', f'ImageData="{ONE_PIXEL_PNG}"', xaml)
    for name, value in HIDDEN.items():
        xaml = re.sub(rf'(?<=\s){name}="[^"]*"', f'{name}="{value}"', xaml)

    def text(match):
        value = match.group(1)
        keep = not value.strip() or MARKUP_VALUE.fullmatch(value)
        return match.group(0) if keep else ">Sample text<"

    return re.sub(r">([^<]+)<", text, xaml)


config_path = os.path.expandvars(r"%APPDATA%\Autodesk\Revit\Addins\RVTuk\config.json")
library = json.load(open(config_path, encoding="utf-8-sig"))["LibraryFolderPath"]
database = pathlib.Path(library, ".DB", "RVTuk.db")
connection = sqlite3.connect(database.as_uri() + "?mode=ro", uri=True)
rows = connection.execute(
    "SELECT Id, FileName, InstructionsXaml FROM Families "
    "WHERE InstructionsXaml LIKE ? ORDER BY Id", (f"%{OLD_DECLARATION}%",)).fetchall()

here = pathlib.Path(__file__).parent
for number, (family_id, file_name, xaml) in enumerate(rows, 1):
    clean = sanitize(xaml)
    assert OLD_DECLARATION in clean
    assert all(ord(c) < 128 for c in clean), "non-ASCII text left: add its attribute to HIDDEN"
    assert len(clean) < 6000, "something large is left: an image or a long text"
    target = here / f"legacy-net48-{number}.txt"
    target.write_text(clean, encoding="utf-8", newline="")
    print(f"{target.name}  <- family {family_id}  {file_name}  ({len(xaml)} -> {len(clean)} chars)")
print(len(rows), "rows")
