using PassingTrace.Ai.Worker;
using PassingTrace.Core.Events;
using Xunit;

namespace PassingTrace.Events.IntegrationTests;

public sealed class AmountClassificationTests
{
    [Fact]
    public void Amount_is_a_cross_category_tag_and_is_derived_from_evidence()
    {
        Assert.True(EventTaxonomy.IsBehaviorTag("amount"));
        Assert.False(EventTaxonomy.IsCategory("amount"));
        Assert.Equal("金额", EventTaxonomy.BehaviorTagLabel("amount"));
        var envelope = new SemanticEnvelope("午饭花了20元", [], [],
            [new(20m, "CNY", "午饭", "本人", 0.9m, "花了20元")], [],
            new("food", 0.9m, 0, 2, null),
            [new("dining", 0.9m, 0, 2, null)]).WithAmountTag();
        Assert.Equal("food", envelope.PrimaryCategory!.TaxonomyKey);
        Assert.Contains(envelope.BehaviorTags!, tag => tag.TaxonomyKey == "amount");
        Assert.Contains(envelope.BehaviorTags!, tag => tag.TaxonomyKey == "dining");
        Assert.Single(envelope.WithAmountTag().BehaviorTags!, tag => tag.TaxonomyKey == "amount");
    }

    [Theory]
    [InlineData(0, "CNY", 0.9, "免费0元", true)]
    [InlineData(20, "CNY", 0.4, "可能20元", false)]
    [InlineData(20, "", 0.9, "20", false)]
    [InlineData(20, "CNY", 0.9, "", false)]
    [InlineData(-20, "CNY", 0.9, "金额", false)]
    public void Unsubstantiated_model_amount_labels_are_not_published(decimal amount, string currency,
        decimal confidence, string evidence, bool expected)
    {
        var envelope = new SemanticEnvelope("记录", [], [],
            [new(amount, currency, "", "", confidence, evidence)], [],
            BehaviorTags: [new("amount", 1, null, null, null)]).WithAmountTag();
        Assert.Equal(expected, envelope.BehaviorTags!.Any(tag => tag.TaxonomyKey == "amount"));
    }
}
