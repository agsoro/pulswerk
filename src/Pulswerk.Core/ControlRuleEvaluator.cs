using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Pulswerk.Core
{
    public static class ControlRuleEvaluator
    {
        public static bool Evaluate(
            ControlConditionConfig condition,
            IReadOnlyDictionary<string, object> values)
        {
            if (condition.All is { Count: > 0 })
                return condition.All.All(child => Evaluate(child, values));

            if (condition.Any is { Count: > 0 })
                return condition.Any.Any(child => Evaluate(child, values));

            if (string.IsNullOrWhiteSpace(condition.Source) ||
                string.IsNullOrWhiteSpace(condition.Operator) ||
                !values.TryGetValue(condition.Source, out var actual))
                return false;

            return Compare(actual, condition.Operator, condition.Value);
        }

        public static bool TryGetNumber(object? value, out double number)
        {
            number = 0;
            if (value is JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Number)
                    return element.TryGetDouble(out number);
                if (element.ValueKind == JsonValueKind.String)
                    value = element.GetString();
            }

            return value != null && double.TryParse(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out number);
        }

        public static object? UnwrapJsonValue(object? value)
        {
            if (value is not JsonElement element) return value;
            return element.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number when element.TryGetDouble(out var number) => number,
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Null => null,
                _ => element.GetRawText()
            };
        }

        private static bool Compare(object actual, string operation, object? expected)
        {
            expected = UnwrapJsonValue(expected);
            string op = operation.Trim().ToLowerInvariant();

            if (op is "eq" or "equals" or "ne" or "neq" or "notequals")
            {
                bool equal;
                if (TryGetNumber(actual, out var actualNumber) && TryGetNumber(expected, out var expectedNumber))
                    equal = actualNumber == expectedNumber;
                else if (TryGetBoolean(actual, out var actualBool) && TryGetBoolean(expected, out var expectedBool))
                    equal = actualBool == expectedBool;
                else
                    equal = string.Equals(
                        Convert.ToString(actual, CultureInfo.InvariantCulture),
                        Convert.ToString(expected, CultureInfo.InvariantCulture),
                        StringComparison.OrdinalIgnoreCase);

                return op is "ne" or "neq" or "notequals" ? !equal : equal;
            }

            if (!TryGetNumber(actual, out var left) || !TryGetNumber(expected, out var right))
                return false;

            return op switch
            {
                "gt" or "greaterthan" => left > right,
                "gte" or "greaterthanorequal" => left >= right,
                "lt" or "lessthan" => left < right,
                "lte" or "lessthanorequal" => left <= right,
                _ => false
            };
        }

        private static bool TryGetBoolean(object? value, out bool result)
        {
            value = UnwrapJsonValue(value);
            if (value is bool boolean)
            {
                result = boolean;
                return true;
            }

            string? text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (bool.TryParse(text, out result)) return true;
            if (text is "1" or "on" or "active" or "enabled") { result = true; return true; }
            if (text is "0" or "off" or "inactive" or "disabled") { result = false; return true; }
            return false;
        }
    }
}