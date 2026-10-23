using System.Globalization;
using System.Text.Json;

namespace InFalsusMod.Potential;

/// <summary>冻结 v6 参数的纯托管推理；四难度独立选择模型，不在游戏中训练。</summary>
internal sealed class MengLeiModel
{
    private readonly JsonElement models;

    internal MengLeiModel(byte[] bytes)
    {
        using var json = JsonDocument.Parse(bytes);
        if (json.RootElement.GetProperty("schema_version").GetInt32() != 1
            || json.RootElement.GetProperty("version").GetString() != CommunityConstants.MengLeiRule)
            throw new InvalidDataException("萌泪酱模型版本不受支持。");
        models = json.RootElement.GetProperty("models").Clone();
    }

    internal decimal Predict(byte difficulty, int level, IReadOnlyDictionary<string, double> features)
    {
        string name = difficulty switch { 1 => "MIN", 2 => "EVO", 4 => "ULT", 8 => "FBD", _ => throw new InvalidDataException("未知谱面难度。") };
        var model = models.GetProperty(name);
        if (model.GetProperty("difficulty").GetString() != name) throw new InvalidDataException("模型难度归属不一致。");
        var structure = model.GetProperty("structure");
        double score = structure.GetProperty("intercept").GetDouble() + Dot(Features(structure, features), Array(structure, "weights"));
        string key = level.ToString(CultureInfo.InvariantCulture);
        double center = structure.GetProperty("centers").TryGetProperty(key, out var c) ? c.GetDouble() : score;
        double mean = structure.GetProperty("means").TryGetProperty(key, out var m) ? m.GetDouble() : .45;
        double estimate = level + mean + structure.GetProperty("beta").GetDouble() * (score - center);
        if (model.TryGetProperty("ensemble", out var ensemble)
            && level >= ensemble.GetProperty("minimum_level").GetInt32() && level <= ensemble.GetProperty("maximum_level").GetInt32())
        {
            double[] x = Features(ensemble, features);
            var linear = ensemble.GetProperty("linear");
            double[] z = Normalize(x, linear, clip: false);
            double delta = Math.Clamp(Dot(new[] { 1.0, level }.Concat(z).ToArray(), Array(linear, "weights")), 0, .9);
            var kernel = ensemble.GetProperty("kernel");
            z = Normalize(x.Append((double)level).ToArray(), kernel, clip: true);
            var centers = kernel.GetProperty("centers");
            double[] weights = Array(kernel, "weights");
            if (centers.GetArrayLength() != weights.Length) throw new InvalidDataException("核模型维度不一致。");
            double kernelDelta = kernel.GetProperty("target_mean").GetDouble(), gamma = kernel.GetProperty("gamma").GetDouble();
            for (int i = 0; i < weights.Length; i++)
            {
                var row = centers[i];
                if (row.GetArrayLength() != z.Length) throw new InvalidDataException("核中心维度不一致。");
                double distance = 0;
                for (int j = 0; j < z.Length; j++) distance += Math.Pow(z[j] - row[j].GetDouble(), 2);
                kernelDelta += Math.Exp(-gamma * distance / z.Length) * weights[i];
            }
            estimate = level + (delta + Math.Clamp(kernelDelta, 0, .9)) / 2;
        }
        return Quantize(level, estimate);
    }

    internal static decimal Quantize(int level, double estimate)
    {
        if (level < 0 || !double.IsFinite(estimate)) throw new InvalidDataException("官方等级或定数估计无效。");
        double bounded = Math.Clamp(estimate, level, level + .9);
        int[] offsets = level < 7 ? new[] { 0, 5 } : level < 9 ? new[] { 0, 5, 8 } : Enumerable.Range(0, 10).ToArray();
        return offsets.Select(offset => level + offset / 10m)
            .OrderBy(value => Math.Round(Math.Abs((double)value - bounded), 12)).ThenByDescending(value => value).First();
    }

    private static double[] Features(JsonElement model, IReadOnlyDictionary<string, double> features) => model.GetProperty("columns")
        .EnumerateArray().Select(column =>
        {
            double value = features[column.GetString()!];
            if (!double.IsFinite(value) || value < 0) throw new InvalidDataException("定数特征必须为有限非负数。");
            // 补偿 1+x 的舍入误差，接近 Python math.log1p 的小量精度。
            double sum = 1 + value;
            return sum == 1 ? value : Math.Log(sum) * (value / (sum - 1));
        }).ToArray();

    private static double[] Array(JsonElement model, string name) => model.GetProperty(name).EnumerateArray().Select(v => v.GetDouble()).ToArray();

    private static double Dot(double[] a, double[] b)
    {
        if (a.Length != b.Length) throw new InvalidDataException("线性模型维度不一致。");
        double sum = 0;
        for (int i = 0; i < a.Length; i++) sum += a[i] * b[i];
        return sum;
    }

    private static double[] Normalize(double[] values, JsonElement model, bool clip)
    {
        double[] mean = Array(model, "mean"), scale = Array(model, "scale");
        if (values.Length != mean.Length || values.Length != scale.Length || scale.Any(v => v <= 0))
            throw new InvalidDataException("模型标准化参数无效。");
        return values.Select((value, i) => clip ? Math.Clamp((value - mean[i]) / scale[i], -5, 5) : (value - mean[i]) / scale[i]).ToArray();
    }
}
