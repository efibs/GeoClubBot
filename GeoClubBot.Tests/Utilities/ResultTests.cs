using FluentAssertions;
using Utilities;
using Xunit;

namespace GeoClubBot.Tests.Utilities;

public sealed class ResultTests
{
    [Fact]
    public void SuccessGeneric_ExposesValue_AndNoError()
    {
        var result = Result<int>.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Value.Should().Be(42);
        result.Error.Should().Be(Error.None);
    }

    [Fact]
    public void FailureGeneric_ExposesError_AndThrowsOnValue()
    {
        var error = Error.NotFound("code", "message");
        var result = Result<int>.Failure(error);

        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);

        var act = () => _ = result.Value;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void FailureGeneric_WithNoneError_Throws()
    {
        var act = () => Result<int>.Failure(Error.None);

        act.Should().Throw<ArgumentException>().WithParameterName("error");
    }

    [Fact]
    public void SuccessGeneric_AllowsNullReferenceValue()
    {
        var result = Result<string?>.Success(null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public void ImplicitConversion_FromValue_ProducesSuccess()
    {
        Result<string> result = "hello";

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("hello");
    }

    [Fact]
    public void ImplicitConversion_FromError_ProducesFailure()
    {
        Result<string> result = Error.Validation("v", "bad");

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void NonGenericSuccess_HasNoError()
    {
        var result = Result.Success();

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
        result.Error.Should().Be(Error.None);
    }

    [Fact]
    public void NonGenericFailure_ExposesError()
    {
        var error = Error.Conflict("c", "conflict");
        var result = Result.Failure(error);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public void NonGenericFailure_WithNoneError_Throws()
    {
        var act = () => Result.Failure(Error.None);

        act.Should().Throw<ArgumentException>().WithParameterName("error");
    }

    [Fact]
    public void NonGeneric_ImplicitConversion_FromError_ProducesFailure()
    {
        Result result = Error.Unauthorized("u", "nope");

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unauthorized);
    }

    [Theory]
    [InlineData(ErrorType.NotFound)]
    [InlineData(ErrorType.Validation)]
    [InlineData(ErrorType.Conflict)]
    [InlineData(ErrorType.Forbidden)]
    [InlineData(ErrorType.Unauthorized)]
    [InlineData(ErrorType.Unexpected)]
    public void ErrorFactories_SetExpectedType(ErrorType type)
    {
        var error = type switch
        {
            ErrorType.NotFound => Error.NotFound("c", "m"),
            ErrorType.Validation => Error.Validation("c", "m"),
            ErrorType.Conflict => Error.Conflict("c", "m"),
            ErrorType.Forbidden => Error.Forbidden("c", "m"),
            ErrorType.Unauthorized => Error.Unauthorized("c", "m"),
            _ => Error.Unexpected("c", "m"),
        };

        error.Type.Should().Be(type);
        error.Code.Should().Be("c");
        error.Message.Should().Be("m");
    }

    [Fact]
    public void Error_None_IsEquatableByValue()
    {
        var none = new Error(string.Empty, string.Empty, ErrorType.Unexpected);

        none.Should().Be(Error.None);
    }

    [Fact]
    public void Error_WithSameFields_AreEqual()
    {
        var a = Error.NotFound("x", "y");
        var b = Error.NotFound("x", "y");

        a.Should().Be(b);
    }
}
