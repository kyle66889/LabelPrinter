using Xunit;

namespace LabelPrinter.Tests;

public class PreviousInstanceTests
{
    private sealed class FakeInstance : IInstanceHandle
    {
        private readonly string? _error;

        public FakeInstance(int id, string description, string? error = null)
        {
            Id = id;
            Description = description;
            _error = error;
        }

        public int Id { get; }
        public string Description { get; }
        public bool Stopped { get; private set; }

        public bool TryStop(TimeSpan wait, out string? error)
        {
            error = _error;
            Stopped = _error is null;
            return _error is null;
        }

        public void Dispose()
        {
        }
    }

    [Fact]
    public void StopAll_succeeds_when_every_instance_stops()
    {
        var a = new FakeInstance(1, "a");
        var b = new FakeInstance(2, "b");

        var result = PreviousInstance.StopAll(new[] { a, b }, TimeSpan.FromSeconds(1));

        Assert.True(result.AllStopped);
        Assert.Empty(result.Errors);
        Assert.True(a.Stopped && b.Stopped);
    }

    [Fact]
    public void StopAll_reports_each_failure_but_still_tries_the_rest()
    {
        var denied = new FakeInstance(10, "denied", "Access is denied.");
        var ok = new FakeInstance(11, "ok");

        var result = PreviousInstance.StopAll(new[] { denied, ok }, TimeSpan.FromSeconds(1));

        Assert.False(result.AllStopped);
        Assert.Single(result.Errors);
        Assert.Contains("PID 10", result.Errors[0]);
        Assert.Contains("Access is denied.", result.Errors[0]);
        Assert.True(ok.Stopped);
    }

    [Fact]
    public void StopAll_with_nothing_to_stop_is_a_success() =>
        Assert.True(PreviousInstance.StopAll(Array.Empty<IInstanceHandle>(), TimeSpan.FromSeconds(1)).AllStopped);

    [Fact]
    public void Describe_lists_one_line_per_instance()
    {
        var text = PreviousInstance.Describe(new[]
        {
            new FakeInstance(1, @"D:\Pda\LabelPrinter.exe (v1.1.5, PID 1)"),
            new FakeInstance(2, "PID 2")
        });

        var lines = text.Split(Environment.NewLine);
        Assert.Equal(2, lines.Length);
        Assert.Contains("PID 1", lines[0]);
        Assert.Equal("PID 2", lines[1]);
    }

    [Fact]
    public void FindOthers_never_includes_this_process()
    {
        var found = PreviousInstance.FindOthers();
        try
        {
            Assert.DoesNotContain(found, h => h.Id == Environment.ProcessId);
        }
        finally
        {
            foreach (var h in found)
                h.Dispose();
        }
    }

    [Theory]
    [InlineData("LabelPrinter", "LabelPrinter", InstanceIdentity.Same)]
    [InlineData("LabelPrinter", " labelprinter ", InstanceIdentity.Same)]
    [InlineData("LabelPrinter", "Acme Label Printer Pro", InstanceIdentity.Different)]
    [InlineData("LabelPrinter", null, InstanceIdentity.Unknown)]
    [InlineData("LabelPrinter", "", InstanceIdentity.Unknown)]
    [InlineData(null, "LabelPrinter", InstanceIdentity.Unknown)]
    public void Classify_compares_product_names_and_admits_when_it_cannot_tell(string? own, string? other, InstanceIdentity expected) =>
        Assert.Equal(expected, PreviousInstance.Classify(own, other));

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(300);

    [Fact]
    public void TakeOver_with_nothing_running_asks_nothing_and_does_nothing()
    {
        var asked = false;
        var tried = false;

        var r = PreviousInstance.TakeOver(
            Array.Empty<IInstanceHandle>(), _ => asked = true, () => tried = true, Wait, 3, Delay, _ => { });

        Assert.Equal(TakeoverOutcome.NothingFound, r.Outcome);
        Assert.False(asked);
        Assert.False(tried);
    }

    [Fact]
    public void TakeOver_declined_leaves_the_old_instance_alone()
    {
        var old = new FakeInstance(5, "old");
        string? shown = null;

        var r = PreviousInstance.TakeOver(
            new[] { old }, d => { shown = d; return false; }, () => true, Wait, 3, Delay, _ => { });

        Assert.Equal(TakeoverOutcome.Declined, r.Outcome);
        Assert.False(old.Stopped);
        Assert.Equal("old", shown);
    }

    [Fact]
    public void TakeOver_stops_the_old_instance_then_takes_the_lock()
    {
        var old = new FakeInstance(5, "old");

        var r = PreviousInstance.TakeOver(new[] { old }, _ => true, () => true, Wait, 3, Delay, _ => { });

        Assert.Equal(TakeoverOutcome.Taken, r.Outcome);
        Assert.True(old.Stopped);
    }

    [Fact]
    public void TakeOver_reports_a_stop_failure_and_never_touches_the_lock()
    {
        var tried = false;

        var r = PreviousInstance.TakeOver(
            new[] { new FakeInstance(7, "old", "Access is denied.") },
            _ => true, () => tried = true, Wait, 3, Delay, _ => { });

        Assert.Equal(TakeoverOutcome.StopFailed, r.Outcome);
        Assert.Contains("Access is denied.", r.Detail);
        Assert.False(tried);
    }

    [Fact]
    public void TakeOver_waits_and_retries_until_the_lock_is_released()
    {
        var attempts = 0;
        var sleeps = new List<TimeSpan>();

        var r = PreviousInstance.TakeOver(
            new[] { new FakeInstance(1, "old") },
            _ => true, () => ++attempts == 3, Wait, 10, Delay, sleeps.Add);

        Assert.Equal(TakeoverOutcome.Taken, r.Outcome);
        Assert.Equal(3, attempts);
        Assert.Equal(new[] { Delay, Delay }, sleeps);
    }

    [Fact]
    public void TakeOver_gives_up_when_the_lock_never_comes_free()
    {
        var attempts = 0;
        var sleeps = 0;

        var r = PreviousInstance.TakeOver(
            new[] { new FakeInstance(1, "old") },
            _ => true, () => { attempts++; return false; }, Wait, 4, Delay, _ => sleeps++);

        Assert.Equal(TakeoverOutcome.LockStillHeld, r.Outcome);
        Assert.Equal(4, attempts);
        Assert.Equal(3, sleeps); // no pointless sleep after the last attempt
    }
}
