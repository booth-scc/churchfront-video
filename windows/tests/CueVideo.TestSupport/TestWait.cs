namespace CueVideo.TestSupport;

/// <summary>Waits for a test condition, pumping the UI thread when the caller asks.</summary>
public static class TestWait
{
    public static async Task Until(
        Func<bool> condition,
        string failure,
        Action? pump = null,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));

        while (DateTime.UtcNow < deadline)
        {
            pump?.Invoke();

            if (condition()) return;

            await Task.Delay(25);
        }

        throw new TimeoutException(failure);
    }
}
