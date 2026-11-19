namespace ControlRoom.Server.Tests;

/// <summary><c>state.json</c> texts shared by the job tests.</summary>
internal static class JobSamples
{
    /// <summary>A full state in the factory's real shape, with fields the board doesn't know.</summary>
    public const string FullState =
        """
        {
          "id": "ignored-id",
          "project": "Alpha",
          "title": "#2 Factory dashboard",
          "branch": "factory/007-board",
          "mode": "quality",
          "stage": "review",
          "round": 2,
          "reviews": { "security": "PASS", "ux": "pending", "ui": "skipped", "code": "CHANGES", "extra": "x" },
          "pastRounds": [ { "security": "CHANGES", "ux": "PASS", "ui": "skipped", "code": "PASS" }, "bad", 3 ],
          "decision": null,
          "note": "waiting on ci",
          "path": "full",
          "risk": "high",
          "base": "develop",
          "baseSha": "secret-sha",
          "stories": [ "Alpha/backlog/002-board.md" ],
          "specReview": "PASS",
          "specRevised": true,
          "tokens": {
            "builder": { "runs": 1, "model": "opus", "input": 1000, "cacheRead": 200000, "output": 3000 },
            "approver": { "runs": 1, "model": "sonnet", "input": 10, "cacheRead": 20, "output": 30 },
            "broken": "x"
          },
          "ci": { "status": "passed", "reason": "secret-reason" }
        }
        """;

    /// <summary>Every known field of the wrong type.</summary>
    public const string WrongTypes =
        """
        {
          "project": 1, "title": [], "branch": {}, "stage": true, "round": "2", "mode": null, "path": 3,
          "risk": false, "reviews": [ "PASS" ], "pastRounds": { "a": 1 }, "specReview": 1, "specRevised": "yes",
          "decision": 2, "note": [], "ci": "passed", "tokens": [ 1, 2 ]
        }
        """;

    /// <summary>A minimal state for a job at the given stage and round.</summary>
    public static string State(string project, string stage, int round)
    {
        return "{\"project\":\"" + project + "\",\"title\":\"T\",\"stage\":\"" + stage + "\",\"round\":" + round
            + "}";
    }
}
