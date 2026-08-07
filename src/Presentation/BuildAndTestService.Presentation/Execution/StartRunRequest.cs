namespace BuildAndTestService.Presentation.Execution;

/// <summary>The JSON request body for <c>POST /run</c>.</summary>
public sealed record StartRunRequest(IReadOnlyList<string>? Arguments, string? WorkingDirectory, int? TimeoutSeconds);
