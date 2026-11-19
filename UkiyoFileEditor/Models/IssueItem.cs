namespace UkiyoFileEditor.Models;

/// <summary>One problem found by "Check all files".</summary>
public sealed record IssueItem(string File, string Key, string Id, Severity Severity, string Message, string Original, string Translation);
