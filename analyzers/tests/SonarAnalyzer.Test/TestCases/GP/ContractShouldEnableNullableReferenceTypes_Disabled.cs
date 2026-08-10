#nullable disable

public sealed record CandidateRegisteredContract(System.Guid CandidateId, string Email); // Noncompliant {{'CandidateRegisteredContract' is declared without nullable reference types, so its members do not say which values are optional.}}
