using Crypton.Configuration;
using FluentAssertions;
using Xunit;

namespace Crypton.Configuration.Tests;

public sealed class ServiceDegradationTests
{
    [Fact]
    public void EnterDegraded_SetsDegradedState_AndCapturesErrors()
    {
        var store = new DegradedStateStore();

        store.EnterDegraded(["dependency unavailable", "dependency unavailable", " "]);

        store.IsDegraded.Should().BeTrue();
        store.Errors.Should().ContainSingle().Which.Should().Be("dependency unavailable");
        store.LastTransitionAt.Should().NotBeNull();
    }

    [Fact]
    public void ClearDegraded_ResetsState_AndErrors()
    {
        var store = new DegradedStateStore();
        store.EnterDegraded(["error"]);

        store.ClearDegraded();

        store.IsDegraded.Should().BeFalse();
        store.Errors.Should().BeEmpty();
        store.LastTransitionAt.Should().NotBeNull();
    }

    [Fact]
    public void GetSnapshot_ReturnsConsistentView()
    {
        var store = new DegradedStateStore();
        store.EnterDegraded(["downstream failure"]);

        var snapshot = store.GetSnapshot();

        snapshot.IsDegraded.Should().BeTrue();
        snapshot.Errors.Should().ContainSingle().Which.Should().Be("downstream failure");
        snapshot.LastTransitionAt.Should().NotBeNull();
    }

    [Fact]
    public void RecoveryValidationResult_Failure_ExposesFormattedErrors()
    {
        var result = RecoveryValidationResult.Failure(
            new RecoveryCheckResult("exchange", Passed: false, "timeout"),
            new RecoveryCheckResult("cache", Passed: false),
            new RecoveryCheckResult("config", Passed: true));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Equal("exchange: timeout", "cache");
    }

    [Fact]
    public void RecoveryValidationResult_Success_HasNoErrors()
    {
        var result = RecoveryValidationResult.Success(
            new RecoveryCheckResult("exchange", Passed: true),
            new RecoveryCheckResult("cache", Passed: true));

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }
}