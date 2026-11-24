using System;
using System.IO;
using System.Linq;
using RVTuk.Core.FamilyBrowser.Requests;
using Xunit;

namespace RVTuk.Core.Tests.FamilyBrowser;

// The .Requests folder: users can only add files there (View + Upload in Autodesk Docs), so a
// request, its attachment and a withdrawal are all new files; only Done moves anything.
public class RequestStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rvtuk_req_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    private FamilyRequest Make(RequestStore store, string user, string text, DateTime at, string? path = "Doors\\Door.rfa")
    {
        store.EnsureFolder(); // the maintainer's browser does this
        var r = new FamilyRequest
        {
            Id = RequestStore.NewId(user, at),
            Kind = path == null ? FamilyRequest.KindNewFamily : FamilyRequest.KindFix,
            RelativePath = path,
            FamilyName = path == null ? null : "Door",
            User = user,
            CreatedUtc = at.ToString("o"),
            Text = text,
        };
        store.Save(r);
        return r;
    }

    [Fact]
    public void MissingFolder_HasNoRequests()
    {
        var store = new RequestStore(_root);
        Assert.Empty(store.LoadOpen());
        Assert.Equal(0, store.CountDone());
    }

    [Fact]
    public void Save_NeverCreatesTheFolder()
    {
        // A view-only user's local folder would never reach the maintainer.
        var store = new RequestStore(_root);
        var r = new FamilyRequest { Id = RequestStore.NewId("noa", DateTime.UtcNow), Text = "x" };
        Assert.Throws<DirectoryNotFoundException>(() => store.Save(r));
        Assert.False(store.FolderExists);
    }

    [Theory]
    // Written by DataContractJsonSerializer (net48: "\/" escapes, keys in alphabetical order,
    // raw Hebrew) and by System.Text.Json (net8/10: indented, Hebrew as \u escapes). Revit
    // 2024 and 2025+ share the folder, so each must read the other's files.
    [InlineData("{\"Attachment\":null,\"CreatedUtc\":\"2026-11-24T10:00:00.0000000Z\",\"FamilyName\":\"Door\",\"Id\":\"x\",\"Kind\":\"fix\",\"RelativePath\":\"Doors\\\\Door.rfa\",\"Text\":\"ארון גבוה \\/ tall\",\"User\":\"noa\"}")]
    [InlineData("{\n  \"Id\": \"x\",\n  \"Kind\": \"fix\",\n  \"RelativePath\": \"Doors\\\\Door.rfa\",\n  \"FamilyName\": \"Door\",\n  \"User\": \"noa\",\n  \"CreatedUtc\": \"2026-11-24T10:00:00.0000000Z\",\n  \"Text\": \"\\u05D0\\u05E8\\u05D5\\u05DF \\u05D2\\u05D1\\u05D5\\u05D4 / tall\",\n  \"Attachment\": null\n}")]
    public void FilesFromEitherFramework_ReadBack(string json)
    {
        var store = new RequestStore(_root);
        store.EnsureFolder();
        File.WriteAllText(Path.Combine(store.Folder, "20261124-100000-noa-abc123.json"), json);

        var r = Assert.Single(store.LoadOpen());
        Assert.Equal("20261124-100000-noa-abc123", r.Id); // the file name wins over the stored id
        Assert.Equal("Doors\\Door.rfa", r.RelativePath);
        Assert.Equal("noa", r.User);
        Assert.Equal("ארון גבוה / tall", r.Text);
        Assert.Null(r.Attachment);
    }

    [Fact]
    public void SavedRequest_ReadsBack_WithHebrewAndLatinText()
    {
        var store = new RequestStore(_root);
        const string text = "ארון גבוה 60 ס\"מ — tall cabinet, oven slot\nsecond line";
        var saved = Make(store, "noa", text, new DateTime(2026, 11, 24, 10, 0, 0, DateTimeKind.Utc));

        var read = Assert.Single(store.LoadOpen());
        Assert.Equal(saved.Id, read.Id);
        Assert.Equal(text, read.Text);
        Assert.Equal("Doors\\Door.rfa", read.RelativePath);
        Assert.Equal(FamilyRequest.KindFix, read.Kind);
        Assert.Equal("noa", read.User);
        Assert.Equal(saved.CreatedUtc, read.CreatedUtc);
        Assert.Null(read.Attachment);
    }

    [Fact]
    public void Requests_AreOldestFirst()
    {
        var store = new RequestStore(_root);
        Make(store, "b", "second", new DateTime(2026, 11, 24, 0, 0, 0, DateTimeKind.Utc));
        Make(store, "a", "first", new DateTime(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(new[] { "first", "second" }, store.LoadOpen().Select(r => r.Text));
    }

    [Fact]
    public void TwoRequestsInTheSameSecond_GetDifferentFiles()
    {
        var at = new DateTime(2026, 11, 24, 0, 0, 0, DateTimeKind.Utc);
        Assert.NotEqual(RequestStore.NewId("noa", at), RequestStore.NewId("noa", at));
    }

    [Fact]
    public void Id_KeepsOnlyLettersAndDigitsOfTheUserName()
    {
        var id = RequestStore.NewId(@"OFFICE\noa.levi", new DateTime(2026, 11, 24, 8, 30, 5, DateTimeKind.Utc));
        Assert.StartsWith("20261124-083005-OFFICEnoalevi-", id);
        Assert.True(id.All(c => char.IsLetterOrDigit(c) || c == '-'));
    }

    [Fact]
    public void Saving_NeverOverwritesAnExistingFile()
    {
        var store = new RequestStore(_root);
        var r = Make(store, "noa", "one", DateTime.UtcNow);
        Assert.Throws<IOException>(() => store.Save(r));
    }

    [Fact]
    public void Withdraw_HidesTheRequest_ByAddingAMarkerFile()
    {
        var store = new RequestStore(_root);
        var r = Make(store, "noa", "oops", DateTime.UtcNow);
        store.Withdraw(r);
        Assert.Empty(store.LoadOpen());
        Assert.True(File.Exists(Path.Combine(store.Folder, r.Id + ".json"))); // nothing deleted
    }

    [Fact]
    public void MarkDone_MovesTheRequestAndItsAttachmentToDone()
    {
        var store = new RequestStore(_root);
        var r = Make(store, "noa", "fix it", DateTime.UtcNow);
        File.WriteAllText(store.AttachmentPath(r.Id), "rfa");

        store.MarkDone(r);

        Assert.Empty(store.LoadOpen());
        Assert.Equal(1, store.CountDone());
        Assert.True(File.Exists(Path.Combine(store.DoneFolder, r.Id + ".rfa")));
        Assert.False(File.Exists(store.AttachmentPath(r.Id)));
    }

    [Fact]
    public void TidyWithdrawn_MovesWithdrawnRequestsToDone()
    {
        var store = new RequestStore(_root);
        var keep = Make(store, "a", "keep", DateTime.UtcNow);
        var gone = Make(store, "b", "gone", DateTime.UtcNow);
        store.Withdraw(gone);

        Assert.Equal(1, store.TidyWithdrawn());

        Assert.Equal(keep.Id, Assert.Single(store.LoadOpen()).Id);
        Assert.False(File.Exists(Path.Combine(store.Folder, gone.Id + RequestStore.WithdrawnExtension)));
        Assert.True(File.Exists(Path.Combine(store.DoneFolder, gone.Id + ".json")));
    }

    [Fact]
    public void UnreadableFile_IsSkipped()
    {
        var store = new RequestStore(_root);
        Make(store, "noa", "good", DateTime.UtcNow);
        File.WriteAllText(Path.Combine(store.Folder, "broken.json"), "{ not json");
        Assert.Equal("good", Assert.Single(store.LoadOpen()).Text);
    }
}
