using System.Collections.Generic;
using System.Text.Json;
using Pulswerk.Core;

namespace Pulswerk.Core.Tests;

public class ControlRuleTests
{
    [Fact]
    public void EvaluatesNumericThreshold()
    {
        var condition = new ControlConditionConfig("plant_pv", "gte", 1000);

        Assert.True(ControlRuleEvaluator.Evaluate(
            condition,
            new Dictionary<string, object> { ["plant_pv"] = 1250d }));
        Assert.False(ControlRuleEvaluator.Evaluate(
            condition,
            new Dictionary<string, object> { ["plant_pv"] = 999d }));
    }

    [Fact]
    public void EvaluatesBooleanAndCompoundConditions()
    {
        var condition = new ControlConditionConfig(
            All: new List<ControlConditionConfig>
            {
                new("pv_active", "eq", true),
                new("battery_soc", "gt", 20)
            });

        Assert.True(ControlRuleEvaluator.Evaluate(condition, new Dictionary<string, object>
        {
            ["pv_active"] = "active",
            ["battery_soc"] = 42d
        }));
    }

    [Fact]
    public void DeserializesControlRulesFromJson()
    {
        const string json = """
        {
          "controls": [
            {
              "id": "export-power",
              "when": { "source": "pv_active", "operator": "eq", "value": true },
              "actions": [ { "target": "knx_total_power", "valueSource": "total_power" } ]
            }
          ]
        }
        """;

        using var document = JsonDocument.Parse(json);
        var controls = JsonSerializer.Deserialize<List<ControlRuleConfig>>(
            document.RootElement.GetProperty("controls").GetRawText());

        Assert.NotNull(controls);
        Assert.Single(controls!);
        Assert.Equal("export-power", controls[0].Id);
        Assert.False(controls[0].OnChangeOnly);
    }

    [Fact]
    public void DeserializesRuleWithoutWhenBlock()
    {
        const string json = """
        {
          "id": "refresh-setpoint",
          "actions": [ { "target": "building-knx_total_power", "value": 0 } ]
        }
        """;

        var rule = JsonSerializer.Deserialize<ControlRuleConfig>(json);

        Assert.NotNull(rule);
        Assert.Null(rule!.When);
        Assert.Single(rule.Actions);
    }
}