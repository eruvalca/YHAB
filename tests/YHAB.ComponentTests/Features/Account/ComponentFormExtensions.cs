using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace YHAB.ComponentTests.Features.Account;

internal static class ComponentFormExtensions
{
    extension<TComponent>(TComponent page) where TComponent : IComponent
    {
        // bUnit dispatches form events but does not execute static SSR's form-value mapper.
        internal void SetFormValue(string name, object? value) =>
            typeof(TComponent).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(page, value);

        internal void SetInputValue(string name, object? value)
        {
            var input = typeof(TComponent).GetProperty("Input", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            input.GetType().GetProperty(name)!.SetValue(input, value);
        }
    }
}
