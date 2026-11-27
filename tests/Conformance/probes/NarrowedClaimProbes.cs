using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Conformance;

/// <summary>A public base whose internal members public interfaces of a subclass lean on (#822).</summary>
public abstract class ClaimTrackerBase
{
    internal int Changes;

    internal bool IsChanged => Changes > 0;

    internal void AcceptChanges() => Changes = 0;
}

public sealed class ClaimTracker : ClaimTrackerBase, IChangeTracking
{
    bool IChangeTracking.IsChanged => IsChanged;

    void IChangeTracking.AcceptChanges() => AcceptChanges();
}

public abstract class ClaimBoxBase
{
    internal object? Value { get; set; }
}

public sealed class ClaimBox : ClaimBoxBase, IStrongBox
{
    object? IStrongBox.Value
    {
        get => Value;
        set => Value = value;
    }
}

public abstract class ClaimAccessorBoxBase
{
    private object? _value;

    internal object? Value
    {
        get => _value;
        set => _value = value is int number ? number * 2 : value;
    }
}

public sealed class ClaimAccessorBox : ClaimAccessorBoxBase, IStrongBox
{
    object? IStrongBox.Value
    {
        get => Value;
        set => Value = value;
    }
}

/// <remarks>
/// The tsgo log of the probes shows the type errors of this event on purpose (TS2304 for the
/// unmapped <c>System.EventArgs</c>, TS2345 for <c>delegateAdd</c> over a nullable handler):
/// the rows run on Bun and Node regardless. See the EventArgs mapping follow-up.
/// </remarks>
public abstract class ClaimInitializerBase
{
    internal event EventHandler? Initialized;

    internal bool IsInitialized { get; private set; }

    internal void BeginInit() => IsInitialized = false;

    internal void EndInit()
    {
        IsInitialized = true;
        Initialized?.Invoke(this, null!);
    }
}

public sealed class ClaimInitializer : ClaimInitializerBase, ISupportInitializeNotification
{
    event EventHandler? ISupportInitializeNotification.Initialized
    {
        add => Initialized += value;
        remove => Initialized -= value;
    }

    bool ISupportInitializeNotification.IsInitialized => IsInitialized;

    void ISupportInitialize.BeginInit() => BeginInit();

    void ISupportInitialize.EndInit() => EndInit();
}

public static class NarrowedClaimProbes
{
    public static string ReadPropertyThroughTheInterface()
    {
        var tracker = new ClaimTracker();
        IChangeTracking viaInterface = tracker;
        var before = viaInterface.IsChanged;

        tracker.Changes = 2;

        var changed = viaInterface.IsChanged;

        viaInterface.AcceptChanges();

        return string.Join(",", before, changed, viaInterface.IsChanged, tracker.Changes);
    }

    public static string WriteAutoPropertyThroughTheInterface()
    {
        var box = new ClaimBox();
        IStrongBox viaInterface = box;

        viaInterface.Value = "a";

        var read = viaInterface.Value;

        box.Value = "b";

        return string.Join(",", read, viaInterface.Value, box.Value);
    }

    public static string WriteAccessorPropertyThroughTheInterface()
    {
        var box = new ClaimAccessorBox();
        IStrongBox viaInterface = box;

        viaInterface.Value = 3;

        var read = viaInterface.Value;

        box.Value = 5;

        return string.Join(",", read, viaInterface.Value, box.Value);
    }

    public static string SubscribeEventThroughTheInterface()
    {
        var initializer = new ClaimInitializer();
        ISupportInitializeNotification viaInterface = initializer;
        var raised = 0;
        EventHandler handler = (_, _) => raised++;

        viaInterface.Initialized += handler;
        viaInterface.BeginInit();

        var pending = viaInterface.IsInitialized;

        viaInterface.EndInit();
        viaInterface.Initialized -= handler;
        viaInterface.EndInit();

        return string.Join(",", pending, viaInterface.IsInitialized, raised);
    }
}
