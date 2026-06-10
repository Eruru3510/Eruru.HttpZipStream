using System.Diagnostics;

namespace Eruru.HttpZipStream;

public class Progress<TContext> (
	Action<Progress<TContext>> callback, int intervalMilliseconds = 1000, TContext? context = default
) {

	public TContext? Context { get; } = context;
	public long MaxValue { get; set; }
	public long Value { get; private set; }
	public int Speed { get; private set; }

	readonly Stopwatch Stopwatch = new ();
	readonly Action<Progress<TContext>> Callback = callback;
	readonly int IntervalMilliseconds = intervalMilliseconds;
	long LastValue = -1;

	public void Resume () {
		Stopwatch.Start ();
	}

	public void Pause () {
		Stopwatch.Stop ();
	}

	public void Reset () {
		MaxValue = 0;
		Value = 0;
		Speed = 0;
		Stopwatch.Reset ();
		LastValue = -1;
	}

	public void Append (long value) {
		Update (Value + value);
	}

	public void Update (long value) {
		Value = value;
		if (LastValue < 0 || (MaxValue > 0 && Value >= MaxValue) || Stopwatch.ElapsedMilliseconds >= IntervalMilliseconds) {
			var time = Stopwatch.ElapsedMilliseconds;
			Speed = (int)(time == 0 ? 0 : 1000F / time * (value - (LastValue < 0 ? 0 : LastValue)));
			LastValue = value;
			Stopwatch.Restart ();
			Callback (this);
		}
	}

}