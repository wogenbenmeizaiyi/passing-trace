namespace PassingTrace.Ai.Worker;

public sealed record SemanticEnvelope(
    string Summary,
    IReadOnlyList<ImageDescription> Images,
    IReadOnlyList<SemanticMentionData> Mentions,
    IReadOnlyList<ExpenseFactData> Expenses,
    IReadOnlyList<MemoryCandidate> Memories,
    SemanticLabelData? PrimaryCategory = null,
    IReadOnlyList<SemanticLabelData>? BehaviorTags = null)
{
    // Derive this cross-category label from monetary evidence, not model compliance.
    public SemanticEnvelope WithAmountTag()
    {
        var evidence = Expenses.FirstOrDefault(x => x.Amount >= 0 && x.Confidence >= 0.70m &&
            !string.IsNullOrWhiteSpace(x.Evidence) && x.Currency is { Length: 3 } &&
            x.Currency.All(char.IsAsciiLetter));
        var tags = (BehaviorTags ?? []).Where(x =>
            !string.Equals(x.TaxonomyKey, "amount", StringComparison.OrdinalIgnoreCase));
        return this with
        {
            BehaviorTags = evidence is null ? tags.Take(5).ToArray() :
                new[] { new SemanticLabelData("amount", evidence.Confidence, null, null, null) }
                    .Concat(tags).Take(5).ToArray(),
        };
    }
}

public sealed record SemanticLabelData(
    string TaxonomyKey,
    decimal Confidence,
    int? TextStart,
    int? TextLength,
    Guid? MediaId);

public sealed record ImageDescription(Guid MediaId, string Description);

public sealed record SemanticMentionData(
    string Category,
    string NormalizedValue,
    string OriginalValue,
    string Assertion,
    decimal Confidence,
    int? TextStart,
    int? TextLength,
    Guid? MediaId);

public sealed record ExpenseFactData(
    decimal Amount,
    string Currency,
    string Purpose,
    string Scope,
    decimal Confidence,
    string Evidence);

public sealed record MemoryCandidate(
    string Type,
    string Content,
    decimal Confidence,
    string Evidence);
