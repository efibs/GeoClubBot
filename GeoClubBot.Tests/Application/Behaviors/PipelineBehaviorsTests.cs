using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using UseCases.Abstractions;
using UseCases.Behaviors;
using UseCases.Observability;
using UseCases.OutputPorts.Repositories;
using Xunit;

namespace GeoClubBot.Tests.Application.Behaviors;

public sealed class PipelineBehaviorsTests
{
    // Public, or NSubstitute could not proxy IValidator<ReadQuery>.
    public sealed record PlainCommand : ICommand;

    public sealed record ResultCommand : ICommand<int>;

    public sealed record ReadQuery : IQuery<int>;

    private static RequestHandlerDelegate<int> Returns(int value) => _ => Task.FromResult(value);

    private static RequestHandlerDelegate<int> Throws(Exception exception) => _ => Task.FromException<int>(exception);

    // ---- Unit of work -----------------------------------------------------

    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task UnitOfWork_CommitsACommand_AfterItsHandler()
    {
        var saved = false;
        _unitOfWork.When(u => u.SaveChangesAsync(Arg.Any<CancellationToken>())).Do(_ => saved = true);
        var savedBeforeHandlerFinished = true;
        RequestHandlerDelegate<Unit> next = _ =>
        {
            savedBeforeHandlerFinished = saved;
            return Task.FromResult(Unit.Value);
        };

        await new UnitOfWorkBehavior<PlainCommand, Unit>(_unitOfWork).Handle(new PlainCommand(), next, CancellationToken.None);

        saved.Should().BeTrue();
        savedBeforeHandlerFinished.Should().BeFalse();
    }

    [Fact]
    public async Task UnitOfWork_CommitsACommandWithAResult()
    {
        var result = await new UnitOfWorkBehavior<ResultCommand, int>(_unitOfWork)
            .Handle(new ResultCommand(), Returns(7), CancellationToken.None);

        result.Should().Be(7);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnitOfWork_NeverCommitsAQuery()
    {
        var result = await new UnitOfWorkBehavior<ReadQuery, int>(_unitOfWork)
            .Handle(new ReadQuery(), Returns(7), CancellationToken.None);

        result.Should().Be(7);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task UnitOfWork_CommitsNothing_WhenTheHandlerFails()
    {
        var act = () => new UnitOfWorkBehavior<ResultCommand, int>(_unitOfWork)
            .Handle(new ResultCommand(), Throws(new InvalidOperationException()), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    // ---- Validation -------------------------------------------------------

    private static IValidator<ReadQuery> Validator(params string[] problems)
    {
        var validator = Substitute.For<IValidator<ReadQuery>>();
        validator.ValidateAsync(Arg.Any<IValidationContext>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult(problems.Select(p => new ValidationFailure("Field", p))));
        return validator;
    }

    [Fact]
    public async Task Validation_WithoutValidators_JustRuns()
    {
        var result = await new ValidationBehavior<ReadQuery, int>([])
            .Handle(new ReadQuery(), Returns(7), CancellationToken.None);

        result.Should().Be(7);
    }

    [Fact]
    public async Task Validation_LetsAValidRequestThrough()
    {
        var result = await new ValidationBehavior<ReadQuery, int>([Validator()])
            .Handle(new ReadQuery(), Returns(7), CancellationToken.None);

        result.Should().Be(7);
    }

    [Fact]
    public async Task Validation_ReportsTheProblemsOfEveryValidator_AndNeverRunsTheHandler()
    {
        var handlerRan = false;
        RequestHandlerDelegate<int> next = _ =>
        {
            handlerRan = true;
            return Task.FromResult(7);
        };

        var act = () => new ValidationBehavior<ReadQuery, int>([Validator("first"), Validator(), Validator("second")])
            .Handle(new ReadQuery(), next, CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<ValidationException>();
        thrown.Which.Errors.Select(e => e.ErrorMessage).Should().BeEquivalentTo("first", "second");
        handlerRan.Should().BeFalse();
    }

    // ---- Unhandled exceptions ---------------------------------------------

    [Fact]
    public async Task UnhandledException_PassesTheResultThrough()
    {
        var result = await new UnhandledExceptionBehavior<ReadQuery, int>(NullLogger<UnhandledExceptionBehavior<ReadQuery, int>>.Instance)
            .Handle(new ReadQuery(), Returns(7), CancellationToken.None);

        result.Should().Be(7);
    }

    [Fact]
    public async Task UnhandledException_IsRethrownAsItIs()
    {
        var failure = new InvalidOperationException("boom");

        var act = () => new UnhandledExceptionBehavior<ReadQuery, int>(NullLogger<UnhandledExceptionBehavior<ReadQuery, int>>.Instance)
            .Handle(new ReadQuery(), Throws(failure), CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
    }

    // ---- Logging and metrics ----------------------------------------------

    private static LoggingBehavior<ReadQuery, int> Logging() =>
        new(NullLogger<LoggingBehavior<ReadQuery, int>>.Instance);

    /// <summary>Everything the handler metrics record while <paramref name="action"/> runs, on this request.</summary>
    private static async Task<(int Durations, long Failures)> MeasureAsync(Func<Task> action)
    {
        var durations = 0;
        var failures = 0L;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == HandlerMetrics.MeterName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
        {
            if (instrument == HandlerMetrics.HandlerDurationMs && IsThisRequest(tags) && value >= 0)
            {
                Interlocked.Increment(ref durations);
            }
        });
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            if (instrument == HandlerMetrics.HandlerFailures && IsThisRequest(tags))
            {
                Interlocked.Add(ref failures, value);
            }
        });
        listener.Start();

        await action();

        return (durations, failures);
    }

    private static bool IsThisRequest(ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
        tags.ToArray().Contains(new KeyValuePair<string, object?>("request_name", nameof(ReadQuery)));

    [Fact]
    public async Task Logging_TimesASuccess_WithoutCountingAFailure()
    {
        var result = 0;

        var (durations, failures) = await MeasureAsync(async () =>
            result = await Logging().Handle(new ReadQuery(), Returns(7), CancellationToken.None));

        result.Should().Be(7);
        durations.Should().Be(1);
        failures.Should().Be(0);
    }

    [Fact]
    public async Task Logging_TimesAndCountsAFailure_AndRethrowsIt()
    {
        var failure = new InvalidOperationException("boom");
        Exception? thrown = null;

        var (durations, failures) = await MeasureAsync(async () =>
        {
            try
            {
                await Logging().Handle(new ReadQuery(), Throws(failure), CancellationToken.None);
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
        });

        thrown.Should().BeSameAs(failure);
        durations.Should().Be(1);
        failures.Should().Be(1);
    }

    // ---- Tracing ----------------------------------------------------------

    private static async Task<Activity> TraceAsync(Func<Task> action)
    {
        Activity? span = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ApplicationDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == nameof(ReadQuery))
                {
                    span = activity;
                }
            }
        };
        ActivitySource.AddActivityListener(listener);

        await action();

        return span!;
    }

    [Fact]
    public async Task Tracing_NamesTheSpanAfterTheRequest()
    {
        var result = 0;

        var span = await TraceAsync(async () =>
            result = await new TracingBehavior<ReadQuery, int>().Handle(new ReadQuery(), Returns(7), CancellationToken.None));

        result.Should().Be(7);
        span.GetTagItem("request_name").Should().Be(nameof(ReadQuery));
        span.Status.Should().NotBe(ActivityStatusCode.Error);
    }

    [Fact]
    public async Task Tracing_MarksAFailedSpan_AndRethrows()
    {
        var failure = new InvalidOperationException("boom");
        Exception? thrown = null;

        var span = await TraceAsync(async () =>
        {
            try
            {
                await new TracingBehavior<ReadQuery, int>().Handle(new ReadQuery(), Throws(failure), CancellationToken.None);
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
        });

        thrown.Should().BeSameAs(failure);
        span.Status.Should().Be(ActivityStatusCode.Error);
        span.StatusDescription.Should().Be("boom");
        span.Events.Should().ContainSingle(e => e.Name == "exception");
    }
}
