using System;
using System.Reactive;
using ReactiveUI;

namespace Atomos.UI.Models;

public class InfoItem
{
    public string Name { get; set; }
    public string Value { get; set; }
    public ReactiveCommand<Unit, Unit> Command { get; set; }

    public InfoItem(string name, string value, ReactiveCommand<Unit, Unit> command = null)
    {
        Name = name;
        Value = value;
        Command = command;
    }

    public override bool Equals(object? obj)
    {
        if (obj is InfoItem other)
        {
            return Name == other.Name && Value == other.Value;
        }
        return false;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Name, Value);
    }
}