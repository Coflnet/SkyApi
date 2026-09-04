using System.Diagnostics;
using Coflnet.Sky.Core;
using NUnit.Framework;
using OpenTelemetry.Trace;

namespace SkyApi.Services;

/// <summary>Tests trace sampling invariants used by all services.</summary>
public class JaegerServiceExtensionTests
{
    /// <summary>Zero interval guarantees each root operation is sampled.</summary>
    [Test]
    public void SamplerHonorsConfiguredInterval()
    {
        var sampler = new JaegerSercieExtention.RationOrTimeBasedSampler(0, 0);

        Assert.That(sampler.ShouldSample(CreateParameters("request")).Decision, Is.EqualTo(SamplingDecision.RecordAndSample));
        Assert.That(sampler.ShouldSample(CreateParameters("request")).Decision, Is.EqualTo(SamplingDecision.RecordAndSample));
    }

    /// <summary>Children inherit their trace's root sampling decision.</summary>
    [Test]
    public void ParentSamplerKeepsTraceDecisionConsistent()
    {
        var sampler = new ParentBasedSampler(new JaegerSercieExtention.RationOrTimeBasedSampler(0, 3600));
        var root = CreateParameters("request");
        Assert.That(sampler.ShouldSample(root).Decision, Is.EqualTo(SamplingDecision.RecordAndSample));

        var sampledParent = new ActivityContext(root.TraceId, ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded);
        var child = new SamplingParameters(sampledParent, root.TraceId, "child", ActivityKind.Internal);
        Assert.That(sampler.ShouldSample(child).Decision, Is.EqualTo(SamplingDecision.RecordAndSample));

        var unsampledParent = new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.None);
        var unsampledChild = new SamplingParameters(unsampledParent, unsampledParent.TraceId, "child", ActivityKind.Internal);
        Assert.That(sampler.ShouldSample(unsampledChild).Decision, Is.EqualTo(SamplingDecision.Drop));
    }

    private static SamplingParameters CreateParameters(string name) =>
        new(default, ActivityTraceId.CreateRandom(), name, ActivityKind.Server);
}
