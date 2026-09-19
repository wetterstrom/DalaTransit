using System;
using System.Collections.Generic;
using System.Text;

namespace DalaTransit.Domain;

/// <summary>
/// Value Object som representerar en tidsavvikelse.
/// Inkapslar regler för vad som räknas som försening eller tidig avgång.
/// </summary>
public readonly record struct Delay
{
    public TimeSpan Duration { get; }

    public int TotalSeconds => (int)Duration.TotalSeconds;
    public double TotalMinutes => Math.Round(Duration.TotalMinutes, 1);
    public bool IsDelayed => Duration > TimeSpan.Zero;
    public bool IsEarly => Duration < TimeSpan.Zero;
    public bool IsOnTime => Duration == TimeSpan.Zero;

    private Delay(TimeSpan duration)
    {
        Duration = duration;
    }

    public static Delay FromSeconds(int seconds) => new(TimeSpan.FromSeconds(seconds));
    public static Delay FromDifference(DateTime scheduled, DateTime actual) => new(actual - scheduled);
    public static Delay Zero => new(TimeSpan.Zero);

    public override string ToString()
    {
        if (IsOnTime) return "I tid";
        if (IsEarly) return $"{Math.Abs(TotalMinutes)} min tidig";
        return $"+{TotalMinutes} min sen";
    }
}