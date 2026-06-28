namespace GuardrailApi.Pipeline;

public class EnsembleDecisionEngine
{
    private static readonly Dictionary<string, double> SeverityWeights = new()
    {
        ["critical"] = 1.0,
        ["high"] = 0.8,
        ["medium"] = 0.5,
        ["low"] = 0.2,
    };

    private static readonly Dictionary<string, double> StageWeights = new()
    {
        ["deterministic"] = 0.9,
        ["opa"] = 0.85,
        ["llm-judge"] = 0.7,
    };

    private static readonly Dictionary<string, double> ConfidenceThresholds = new()
    {
        ["critical"] = 0.6,
        ["high"] = 0.7,
        ["medium"] = 0.75,
        ["low"] = 0.85,
    };

    public (string Decision, double Confidence) Decide(List<Violation> violations, string[] frameworks)
    {
        if (violations.Count == 0)
            return ("allow", 1.0);

        var significant = violations
            .Where(v => v.Confidence >= ConfidenceThresholds.GetValueOrDefault(v.Severity, 0.7))
            .ToList();

        if (significant.Count == 0)
            return ("allow", 0.8);

        // Weighted score per violation
        var scores = significant.Select(v =>
        {
            var severityWeight = SeverityWeights.GetValueOrDefault(v.Severity, 0.5);
            var stageWeight = StageWeights.GetValueOrDefault(v.Stage, 0.5);
            return v.Confidence * severityWeight * stageWeight;
        }).ToList();

        double maxScore = scores.Max();
        double avgScore = scores.Average();

        // Multi-source agreement bonus: if multiple stages flag the same type, boost confidence
        var multiStageTypes = significant
            .GroupBy(v => v.Type)
            .Where(g => g.Select(v => v.Stage).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();

        if (multiStageTypes.Count > 0)
            maxScore = Math.Min(1.0, maxScore * 1.15);

        // False positive reduction: single low-confidence hit from one stage only
        if (significant.Count == 1
            && significant[0].Confidence < 0.8
            && significant[0].Severity is "low" or "medium")
        {
            return ("escalate", significant[0].Confidence);
        }

        // Decision mapping
        string decision;
        if (significant.Any(v => v.Severity == "critical"))
            decision = DetermineAction(significant.Where(v => v.Severity == "critical"));
        else if (maxScore >= 0.8)
            decision = DetermineAction(significant);
        else if (maxScore >= 0.5)
            decision = "escalate";
        else
            decision = "allow";

        return (decision, Math.Round(maxScore, 3));
    }

    private static string DetermineAction(IEnumerable<Violation> violations)
    {
        var remediations = violations.Select(v => v.Remediation).Distinct().ToList();

        if (remediations.Contains("block")) return "block";
        if (remediations.Contains("redact")) return "redact";
        if (remediations.Contains("rewrite")) return "rewrite";
        if (remediations.Contains("escalate")) return "escalate";
        return "block";
    }
}
