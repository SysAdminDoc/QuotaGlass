using QuotaGlass.Shared;
using Xunit;

namespace QuotaGlass.Tests;

/// <summary>
/// L-07 — exercises the plan-inference heuristics. Real-world payloads
/// usually carry <see cref="ProviderSnapshot.Plan"/> already; the
/// inference only kicks in when it's null / "unknown".
/// </summary>
public sealed class PlanInferenceTests
{
    [Fact]
    public void Returns_existing_plan_when_already_set()
    {
        var snap = new ProviderSnapshot
        {
            Ok = true, Provider = "claude", Plan = "max-20x",
            Buckets = { new Bucket { Id = "claude-session", Kind = "session" } },
        };
        Assert.Equal("max-20x", PlanInference.Infer(snap));
    }

    [Fact]
    public void Claude_multi_model_weekly_infers_max_20x()
    {
        var snap = new ProviderSnapshot
        {
            Ok = true, Provider = "claude",
            Buckets =
            {
                new Bucket { Id = "claude-session", Kind = "session", Model = "all" },
                new Bucket { Id = "claude-weekly-all", Kind = "weekly", Model = "all" },
                new Bucket { Id = "claude-weekly-sonnet", Kind = "weekly", Model = "sonnet" },
                new Bucket { Id = "claude-weekly-opus", Kind = "weekly", Model = "opus" },
            },
        };
        Assert.Equal("max-20x", PlanInference.Infer(snap));
    }

    [Fact]
    public void Claude_single_model_weekly_infers_max_5x()
    {
        var snap = new ProviderSnapshot
        {
            Ok = true, Provider = "claude",
            Buckets =
            {
                new Bucket { Id = "claude-session", Kind = "session", Model = "all" },
                new Bucket { Id = "claude-weekly-all", Kind = "weekly", Model = "all" },
                new Bucket { Id = "claude-weekly-sonnet", Kind = "weekly", Model = "sonnet" },
            },
        };
        Assert.Equal("max-5x", PlanInference.Infer(snap));
    }

    [Fact]
    public void Claude_weekly_only_no_models_infers_pro()
    {
        var snap = new ProviderSnapshot
        {
            Ok = true, Provider = "claude",
            Buckets = { new Bucket { Id = "claude-weekly-all", Kind = "weekly", Model = "all" } },
        };
        Assert.Equal("pro", PlanInference.Infer(snap));
    }

    [Fact]
    public void Claude_session_only_infers_free()
    {
        var snap = new ProviderSnapshot
        {
            Ok = true, Provider = "claude",
            Buckets = { new Bucket { Id = "claude-session", Kind = "session", Model = "all" } },
        };
        Assert.Equal("free", PlanInference.Infer(snap));
    }

    [Fact]
    public void Codex_weekly_infers_plus()
    {
        var snap = new ProviderSnapshot
        {
            Ok = true, Provider = "codex",
            Buckets = { new Bucket { Id = "codex-weekly-all", Kind = "weekly", Model = "all" } },
        };
        Assert.Equal("plus", PlanInference.Infer(snap));
    }

    [Fact]
    public void Null_or_empty_returns_null()
    {
        Assert.Null(PlanInference.Infer(null));
        Assert.Null(PlanInference.Infer(new ProviderSnapshot { Ok = true, Provider = "claude" }));
    }
}
