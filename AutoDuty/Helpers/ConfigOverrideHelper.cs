using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ECommons.DalamudServices;

namespace AutoDuty.Helpers;

internal static class ConfigOverrideHelper
{
    private readonly record struct OverrideEntry(FieldInfo Field, object? PreviousValue);
    private static readonly Dictionary<FieldInfo, OverrideEntry> Active = [];

    internal static bool HasOverrides => Active.Count > 0;

    internal static bool Push(Dictionary<string, string> overrides)
    {
        if (overrides is null || overrides.Count == 0)
        {
            Svc.Log.Error("Overrides dictionary empty");
            return false;
        }

        List<FieldInfo> newlyTracked = [];
        List<(FieldInfo Field, object? Before)> touched = [];
        foreach ((string name, string value) in overrides)
        {
            FieldInfo? field = ConfigHelper.FindConfig(name);
            if (field is null)
            {
                Svc.Log.Error($"Unable to find config: {name}");
                Revert(touched, newlyTracked);
                return false;
            }

            if (field.FieldType.ToString().Contains("Dalamud.Plugin", System.StringComparison.InvariantCultureIgnoreCase))
            {
                Svc.Log.Error($"Cannot override plugin field: {name}");
                Revert(touched, newlyTracked);
                return false;
            }

            if (field.FieldType.IsAssignableTo(typeof(IList)))
            {
                Svc.Log.Error($"List configs are not supported: {name}");
                Revert(touched, newlyTracked);
                return false;
            }

            object? newValue = ConfigHelper.ConvertConfigValue(field.FieldType, value, out string failReason);
            if (newValue is null)
            {
                Svc.Log.Error($"Unable to set {name}: {failReason}");
                Revert(touched, newlyTracked);
                return false;
            }

            object? before = field.GetValue(Configuration);
            touched.Add((field, before));
            if (!Active.ContainsKey(field))
            {
                Active[field] = new OverrideEntry(field, before);
                newlyTracked.Add(field);
            }

            field.SetValue(Configuration, newValue);
        }

        Svc.Log.Debug($"Applied {overrides.Count} override(s); active={Active.Count}");
        return true;
    }

    internal static bool Pop()
    {
        if (Active.Count == 0)
            return false;

        Svc.Log.Debug($"Restoring {Active.Count} override(s)");
        foreach (OverrideEntry entry in Active.Values)
            entry.Field.SetValue(Configuration, entry.PreviousValue);
        Active.Clear();
        return true;
    }

    private static void Revert(List<(FieldInfo Field, object? Before)> touched, List<FieldInfo> newlyTracked)
    {
        for (int i = touched.Count - 1; i >= 0; i--)
            touched[i].Field.SetValue(Configuration, touched[i].Before);
        foreach (FieldInfo field in newlyTracked)
            Active.Remove(field);
    }
}
