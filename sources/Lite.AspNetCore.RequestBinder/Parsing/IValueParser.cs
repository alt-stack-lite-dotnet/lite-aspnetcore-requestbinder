using System;
using System.Globalization;

namespace Lite.AspNetCore.RequestBinder.Parsing;

public interface IValueParser<T>
{
    bool TryParse(string? value, IFormatProvider? provider, out T result);
}

public static class ValueParser
{
    public static readonly IFormatProvider Invariant = CultureInfo.InvariantCulture;
}
