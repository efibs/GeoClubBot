using FluentAssertions;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Utilities;

public sealed class ResultExtensionsTests
{
    [Fact]
    public void Match_OnSuccess_InvokesOnlyOnSuccess()
    {
        var failureCalled = false;
        var result = Result<int>.Success(10);

        var output = result.Match(
            onSuccess: v => v * 2,
            onFailure: _ =>
            {
                failureCalled = true;
                return -1;
            });

        output.Should().Be(20);
        failureCalled.Should().BeFalse();
    }

    [Fact]
    public void Match_OnFailure_InvokesOnlyOnFailure()
    {
        var successCalled = false;
        var error = Error.NotFound("c", "m");
        var result = Result<int>.Failure(error);

        var output = result.Match(
            onSuccess: _ =>
            {
                successCalled = true;
                return "ok";
            },
            onFailure: e => e.Code);

        output.Should().Be("c");
        successCalled.Should().BeFalse();
    }

    [Fact]
    public void Map_OnSuccess_TransformsValue()
    {
        var result = Result<int>.Success(5);

        var mapped = result.Map(v => v.ToString());

        mapped.IsSuccess.Should().BeTrue();
        mapped.Value.Should().Be("5");
    }

    [Fact]
    public void Map_OnFailure_PropagatesErrorAndSkipsMapper()
    {
        var mapperCalled = false;
        var error = Error.Validation("v", "bad");
        var result = Result<int>.Failure(error);

        var mapped = result.Map<int, string>(v =>
        {
            mapperCalled = true;
            return v.ToString();
        });

        mapped.IsFailure.Should().BeTrue();
        mapped.Error.Should().Be(error);
        mapperCalled.Should().BeFalse();
    }

    [Fact]
    public void Map_CanChain()
    {
        var result = Result<int>.Success(2);

        var mapped = result.Map(v => v + 1).Map(v => v * 10);

        mapped.Value.Should().Be(30);
    }
}
