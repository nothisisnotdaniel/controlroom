using System.Net;
using System.Text;
using System.Text.Json;

namespace ControlRoom.Server.Tests;

/// <summary>The <c>category</c> action of <c>POST /api/backlog/change</c>: sets the story's <c>Category:</c> line
/// (replaced, inserted before the first heading or appended), only the story file committed as
/// <c>backlog: category #N</c>, every refusal without a write or commit.</summary>
public sealed partial class BacklogChangeApiTests
{
    /// <summary>The <c>category</c> action.</summary>
    public sealed class Category
    {
        [Fact]
        public async Task Category_InsertsBeforeTheFirstHeadingCommitsOnlyTheStoryAndPushes()
        {
            using Repo repo = Repo.Create();
            string path = Path.Combine(repo.Folder, "backlog", "002-open.md");
            File.WriteAllText(path, "# 002 Open two\n\n\n## What\n\nx\n\n## Why\n\ny\n");
            repo.CommitAll("headings");
            string remote = repo.Root.AddBareRemote(repo.Folder);
            string version = repo.Version();

            JsonElement result = await repo.Change(new
            {
                action = "category", line = 3, storyVersion = repo.StoryVersion(2), category = "bug",
            });

            Assert.Equal("# 002 Open two\n\nCategory: bug\n\n## What\n\nx\n\n## Why\n\ny\n", File.ReadAllText(path));
            repo.AssertOneCommit("backlog: category #2", ["backlog/002-open.md"]);
            Assert.True(result.GetProperty("committed").GetBoolean());
            Assert.True(result.GetProperty("pushed").GetBoolean());
            Assert.Equal(version, result.GetProperty("version").GetString());
            Assert.Equal(version, repo.Version());
            Assert.Equal(
                TempFactoryRoot.Git(repo.Folder, "rev-parse", "HEAD"),
                TempFactoryRoot.Git(remote, "rev-parse", "refs/heads/develop"));
            Assert.Equal("", TempFactoryRoot.Git(repo.Folder, "status", "--porcelain"));
        }

        [Fact]
        public async Task Category_WithoutAHeadingIsAppendedAfterTheLastLine()
        {
            using Repo repo = Repo.Create();

            await repo.Change(new { action = "category", line = 3, storyVersion = repo.StoryVersion(2), category = "feature" });

            Assert.Equal(
                StoryTwo + "\nCategory: feature\n",
                File.ReadAllText(Path.Combine(repo.Folder, "backlog", "002-open.md")));
            repo.AssertOneCommit("backlog: category #2", ["backlog/002-open.md"]);
        }

        [Fact]
        public async Task Category_ReplacesTheFirstCountingLineAndKeepsEveryOtherByte()
        {
            using Repo repo = Repo.Create();
            string path = Path.Combine(repo.Folder, "backlog", "002-open.md");
            File.WriteAllText(path, "# 002 Open two\n\nCategory: Nope \n\n## What\n\nCategory: ui\n");
            repo.CommitAll("invalid category");

            await repo.Change(new { action = "category", line = 3, storyVersion = repo.StoryVersion(2), category = "tidy-up" });

            Assert.Equal("# 002 Open two\n\nCategory: tidy-up\n\n## What\n\nCategory: ui\n", File.ReadAllText(path));
            repo.AssertOneCommit("backlog: category #2", ["backlog/002-open.md"]);
        }

        [Fact]
        public async Task Category_OnAHandItemKeepsCrlfAndTheBom()
        {
            using Repo repo = Repo.Create(HandBacklog);
            string path = Path.Combine(repo.Folder, "backlog", "002-open.md");
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes("﻿# 002 Hand two\r\n\r\nCategory: ux\r\n\r\n## What\r\n"));
            repo.CommitAll("crlf");

            await repo.Change(new { action = "category", line = 2, storyVersion = repo.StoryVersion(2), category = "docs" });

            Assert.Equal(
                Encoding.UTF8.GetBytes("﻿# 002 Hand two\r\n\r\nCategory: docs\r\n\r\n## What\r\n"),
                File.ReadAllBytes(path));
            repo.AssertOneCommit("backlog: category #2", ["backlog/002-open.md"]);
        }

        [Fact]
        public async Task Category_OnAProposalIsSetAndComesBackFromTheReads()
        {
            using Repo repo = Repo.Create();

            await repo.Change(new { action = "category", line = 5, storyVersion = repo.StoryVersion(3), category = "security" });
            using HttpClient client = repo.Factory.CreateTokenClient();
            using JsonDocument story = JsonDocument.Parse(await client.GetStringAsync("/api/story?project=Alpha&number=3"));
            using JsonDocument backlogs = JsonDocument.Parse(await client.GetStringAsync("/api/backlogs"));

            Assert.Equal(
                "# Something else\n\nCategory: security\n",
                File.ReadAllText(Path.Combine(repo.Folder, "backlog", "003-proposal.md")));
            repo.AssertOneCommit("backlog: category #3", ["backlog/003-proposal.md"]);
            Assert.Equal("security", story.RootElement.GetProperty("category").GetString());
            Assert.Equal(
                "security",
                backlogs.RootElement.GetProperty("projects")[0].GetProperty("approval")[0].GetProperty("category").GetString());
        }

        [Theory]
        [MemberData(nameof(InvalidCategoryBodies), MemberType = typeof(BacklogChangeApiTests))]
        public async Task Category_InvalidRequest_Is400InvalidAndWritesNothing(string fields)
        {
            using Repo repo = Repo.Create();
            await AssertNoteRefused(repo, fields, HttpStatusCode.BadRequest, "invalid");
        }

        [Fact]
        public async Task Category_ThatTheStoryAlreadyHas_Is400InvalidWithoutACommit()
        {
            using Repo repo = Repo.Create();
            File.WriteAllText(Path.Combine(repo.Folder, "backlog", "002-open.md"), "# 002 Open two\n\nCategory: bug\n");
            repo.CommitAll("has bug");

            await AssertNoteRefused(
                repo, "{\"action\":\"category\",\"line\":3,\"storyVersion\":\"S\",\"category\":\"bug\"}",
                HttpStatusCode.BadRequest, "invalid");
        }

        [Fact]
        public async Task Category_OnAMissingStory_Is400Invalid()
        {
            using Repo repo = Repo.Create();
            string version = repo.StoryVersion(2);
            File.Delete(Path.Combine(repo.Folder, "backlog", "002-open.md"));
            repo.CommitAll("gone");

            await AssertNoteRefused(
                repo, "{\"action\":\"category\",\"line\":3,\"storyVersion\":\"" + version + "\",\"category\":\"bug\"}",
                HttpStatusCode.BadRequest, "invalid");
        }

        [Fact]
        public async Task Category_WithAStaleStoryVersion_Is409Changed()
        {
            using Repo repo = Repo.Create();
            string stale = Sha(Encoding.UTF8.GetBytes("# 002 Open two\n\nOld.\n"));

            await AssertNoteRefused(
                repo, "{\"action\":\"category\",\"line\":3,\"storyVersion\":\"" + stale + "\",\"category\":\"bug\"}",
                HttpStatusCode.Conflict, "changed");
        }

        [Fact]
        public async Task Category_OnAnotherBranch_CommitsOnBacklogAndTheReadsShowTheWorktreeCopy()
        {
            using Repo repo = Repo.Create();
            string checkoutStory = Path.Combine(repo.Folder, "backlog", "002-open.md");
            File.WriteAllText(checkoutStory, "# 002 Open two\n\nCategory: docs\n");
            repo.CommitAll("docs");
            TempFactoryRoot.Git(repo.Folder, "checkout", "-q", "-b", "factory/x");
            IReadOnlyList<string> before = repo.State();

            JsonElement result = await repo.Change(new
            {
                action = "category", line = 3, storyVersion = repo.StoryVersion(2), category = "bug",
            });
            using HttpClient client = repo.Factory.CreateTokenClient();
            using JsonDocument backlogs = JsonDocument.Parse(await client.GetStringAsync("/api/backlogs"));
            using JsonDocument story = JsonDocument.Parse(await client.GetStringAsync("/api/story?project=Alpha&number=2"));
            using JsonDocument search = JsonDocument.Parse(await client.GetStringAsync("/api/search?category=bug"));

            Assert.False(result.GetProperty("pushed").GetBoolean());
            Assert.Equal(before, repo.State());
            Assert.Equal("# 002 Open two\n\nCategory: docs\n", File.ReadAllText(checkoutStory));
            Assert.Equal(
                "# 002 Open two\n\nCategory: bug\n",
                File.ReadAllText(Path.Combine(repo.WorktreePath, "backlog", "002-open.md")));
            repo.AssertBacklogCommit("backlog: category #2", ["backlog/002-open.md"]);
            JsonElement alpha = backlogs.RootElement.GetProperty("projects")[0];
            Assert.True(alpha.GetProperty("pending").GetBoolean());
            Assert.Equal("bug", alpha.GetProperty("open")[0].GetProperty("category").GetString());
            Assert.Equal("bug", story.RootElement.GetProperty("category").GetString());
            Assert.Equal(2, search.RootElement.GetProperty("items")[0].GetProperty("number").GetInt32());
        }
    }

    public static TheoryData<string> InvalidCategoryBodies()
    {
        return
        [
            "{\"action\":\"category\",\"line\":3,\"storyVersion\":\"S\"}",
            "{\"action\":\"category\",\"line\":3,\"storyVersion\":\"S\",\"category\":5}",
            "{\"action\":\"category\",\"line\":3,\"storyVersion\":\"S\",\"category\":null}",
            "{\"action\":\"category\",\"line\":3,\"storyVersion\":\"S\",\"category\":\"\"}",
            "{\"action\":\"category\",\"line\":3,\"storyVersion\":\"S\",\"category\":\"nope\"}",
            "{\"action\":\"category\",\"line\":3,\"storyVersion\":\"S\",\"category\":\"Bug\"}",
            "{\"action\":\"category\",\"line\":3,\"storyVersion\":\"S\",\"category\":\"FEATURE\"}",
            "{\"action\":\"category\",\"line\":3,\"storyVersion\":\"S\",\"category\":\"bug \"}",
            "{\"action\":\"category\",\"line\":3,\"category\":\"bug\"}",
            "{\"action\":\"category\",\"storyVersion\":\"S\",\"category\":\"bug\"}",
            "{\"action\":\"category\",\"line\":2,\"storyVersion\":\"S1\",\"category\":\"bug\"}",
            "{\"action\":\"category\",\"line\":6,\"storyVersion\":\"S\",\"category\":\"bug\"}",
            "{\"action\":\"category\",\"line\":7,\"storyVersion\":\"S\",\"category\":\"bug\"}",
            "{\"action\":\"category\",\"line\":4,\"storyVersion\":\"S\",\"category\":\"bug\"}",
            "{\"action\":\"note\",\"line\":3,\"storyVersion\":\"S\",\"note\":\"n\",\"category\":\"bug\"}",
            "{\"action\":\"approve\",\"line\":5,\"category\":\"bug\"}",
            "{\"action\":\"story\",\"line\":3,\"storyVersion\":\"S\",\"markdown\":\"# x\\n\",\"category\":\"bug\"}",
        ];
    }
}
