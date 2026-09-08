using CCP.Kernel.Api.Errors;
using CCP.Kernel.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CCP.Kernel.UnitTests.Results;

public sealed class ResultTests
{
    [Fact]
    public void Success_HasNoErrors()
    {
        Result result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Success_ThrowsWhenErrorIsRead()
    {
        Result result = Result.Success();

        Assert.Throws<InvalidOperationException>(() => result.Error);
    }

    [Fact]
    public void Failure_ThrowsWhenValueIsRead()
    {
        Result<string> result = Result.Failure<string>(
            Error.NotFound("TEST.NOT_FOUND", "Missing."));

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void TryGetValue_DoesNotThrowOnFailure()
    {
        Result<string> result = Result.Failure<string>(
            Error.NotFound("TEST.NOT_FOUND", "Missing."));

        Assert.False(result.TryGetValue(out string? value));
        Assert.Null(value);
    }

    [Fact]
    public void TryGetValue_ReturnsValueOnSuccess()
    {
        Result<string> result = Result.Success("ok");

        Assert.True(result.TryGetValue(out string? value));
        Assert.Equal("ok", value);
    }

    [Fact]
    public void ImplicitConversion_FromValue_ProducesSuccess()
    {
        Result<int> result = 42;

        Assert.True(result.IsSuccess);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void ImplicitConversion_FromError_ProducesFailure()
    {
        Result<int> result = Error.Validation("TEST.INVALID", "Bad input.");

        Assert.True(result.IsFailure);
        Assert.Equal("TEST.INVALID", result.Error.Code);
    }

    [Fact]
    public void Failure_WithNoErrors_IsRejected()
    {
        // A failure that carries no reason is a bug in the calling code, and
        // would surface as an empty error response to a user.
        Assert.Throws<InvalidOperationException>(() => Result.Failure([]));
    }

    [Fact]
    public void Failure_CarriesEveryError()
    {
        Result result = Result.Failure(
        [
            Error.Validation("TEST.A", "First.", "fieldA"),
            Error.Validation("TEST.B", "Second.", "fieldB")
        ]);

        Assert.Equal(2, result.Errors.Count);
        Assert.Equal("TEST.A", result.Error.Code);
    }
}

/// <summary>
/// The error contract is what every client of the Platform codes against, so
/// its shape and its status mapping are pinned by tests (ADR-008).
/// </summary>
public sealed class ProblemDetailsFactoryTests
{
    [Theory]
    [InlineData(ErrorType.Validation, StatusCodes.Status422UnprocessableEntity)]
    [InlineData(ErrorType.NotFound, StatusCodes.Status404NotFound)]
    [InlineData(ErrorType.Conflict, StatusCodes.Status409Conflict)]
    [InlineData(ErrorType.Unauthenticated, StatusCodes.Status401Unauthorized)]
    [InlineData(ErrorType.Forbidden, StatusCodes.Status403Forbidden)]
    [InlineData(ErrorType.Rule, StatusCodes.Status400BadRequest)]
    [InlineData(ErrorType.Unexpected, StatusCodes.Status500InternalServerError)]
    public void StatusCodeFor_MapsEveryErrorType(ErrorType type, int expectedStatus)
    {
        Assert.Equal(expectedStatus, ProblemDetailsFactory.StatusCodeFor(type));
    }

    [Fact]
    public void Create_IncludesCodeAndCorrelationId()
    {
        ProblemDetails problem = ProblemDetailsFactory.Create(
            [Error.NotFound("PLATFORM.USER_NOT_FOUND", "The user does not exist.")],
            correlationId: "abc123",
            instance: "/api/v1/users/1");

        Assert.Equal(404, problem.Status);
        Assert.Equal("PLATFORM.USER_NOT_FOUND", problem.Extensions["code"]);
        Assert.Equal("abc123", problem.Extensions["correlationId"]);
        Assert.Equal("/api/v1/users/1", problem.Instance);
    }

    [Fact]
    public void Create_IncludesFieldErrors_ForValidationFailures()
    {
        ProblemDetails problem = ProblemDetailsFactory.Create(
        [
            Error.Validation("PLATFORM.FIELD_REQUIRED", "Name is required.", "name"),
            Error.Validation("PLATFORM.FIELD_TOO_LONG", "Code is too long.", "code")
        ],
            correlationId: "abc123");

        var errors = Assert.IsType<ErrorDetail[]>(problem.Extensions["errors"]);

        Assert.Equal(2, errors.Length);
        Assert.Equal("name", errors[0].Field);
        Assert.Equal("PLATFORM.FIELD_TOO_LONG", errors[1].Code);
    }

    [Fact]
    public void Create_OmitsFieldErrors_ForNonValidationFailures()
    {
        // Field names are internal detail. Emitting them for, say, a permission
        // denial would leak the shape of a resource the caller may not see.
        ProblemDetails problem = ProblemDetailsFactory.Create(
            [Error.Forbidden("PLATFORM.PERMISSION_DENIED", "Denied.")],
            correlationId: "abc123");

        Assert.False(problem.Extensions.ContainsKey("errors"));
    }

    [Fact]
    public void Unexpected_RevealsNothingBeyondTheCorrelationId()
    {
        ProblemDetails problem = ProblemDetailsFactory.Unexpected("abc123", "/api/v1/things");

        Assert.Equal(500, problem.Status);
        Assert.Equal("PLATFORM.INTERNAL_ERROR", problem.Extensions["code"]);
        Assert.Equal("abc123", problem.Extensions["correlationId"]);

        // The detail must never carry exception text, a stack trace, SQL or a
        // file path (ARCHITECTURE.md §11.3).
        string detail = problem.Detail!;
        Assert.DoesNotContain("Exception", detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("at ", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", detail, StringComparison.Ordinal);
    }
}
