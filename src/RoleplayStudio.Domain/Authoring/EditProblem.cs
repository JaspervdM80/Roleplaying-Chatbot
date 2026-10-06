namespace RoleplayStudio.Domain.Authoring;

/// <summary>Why an edit cannot be saved: the property at fault and a message template.</summary>
public sealed record EditProblem(string Field, string Message);
