using System.Collections.Concurrent;

namespace MockLhdn;

public class MockSubmission
{
    public required string SubmissionUid { get; init; }
    public required string DocumentUid { get; init; }
    public required string CodeNumber { get; init; }
    public required bool WillBeInvalid { get; init; }
    public required DateTimeOffset SubmittedAt { get; init; }
    public bool Cancelled { get; set; }
}

public class SubmissionStore
{
    private readonly ConcurrentDictionary<string, MockSubmission> _bySubmission = new();
    private readonly ConcurrentDictionary<string, MockSubmission> _byDocument = new();

    public MockSubmission Add(string codeNumber, bool willBeInvalid)
    {
        var sub = new MockSubmission
        {
            SubmissionUid = $"SUB-{Guid.NewGuid():N}",
            DocumentUid = $"DOC-{Guid.NewGuid():N}",
            CodeNumber = codeNumber,
            WillBeInvalid = willBeInvalid,
            SubmittedAt = DateTimeOffset.UtcNow,
        };
        _bySubmission[sub.SubmissionUid] = sub;
        _byDocument[sub.DocumentUid] = sub;
        return sub;
    }

    public MockSubmission? BySubmission(string uid) => _bySubmission.GetValueOrDefault(uid);
    public MockSubmission? ByDocument(string uid) => _byDocument.GetValueOrDefault(uid);
}
