using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using LootUp.Core.Characters;
using LootUp.Core.Game;
using LootUp.Core.Items;
using UnityEditor;
using UnityEngine;

namespace LootUp.Editor.Balance
{
    [Serializable]
    public sealed class LevelGrowthSimulationSettings
    {
        public LevelBalanceSimulationSettings RunSettings;
        public int StartingLevel = 1;
        public int StartingExperience;
        public int TargetLevel = 20;
        public int MaxRuns = 200;
        public int Population = 300;
        public float RunsPerDay = 3f;
    }

    [Serializable]
    public sealed class LevelGrowthRunResult
    {
        public int SampleIndex;
        public int RunNumber;
        public int StartLevel;
        public int StartExperience;
        public int ExperienceEarned;
        public int EndLevel;
        public int EndExperience;
        public int HighestFloor;
        public int GameplayScore;
        public string EndReason;
    }

    [Serializable]
    public sealed class LevelGrowthMilestoneResult
    {
        public int Level;
        public float ReachedPercent;
        public int P10Runs;
        public int MedianRuns;
        public int P90Runs;
        public float MedianDays;
    }

    [Serializable]
    public sealed class LevelGrowthCurvePoint
    {
        public int RunNumber;
        public int P10Level;
        public int MedianLevel;
        public int P90Level;
        public int P10Floor;
        public int MedianFloor;
        public int P90Floor;
        public float TargetReachedPercent;
    }

    [Serializable]
    public sealed class LevelGrowthLevelResult
    {
        public int Level;
        public int Runs;
        public float AverageExperience;
        public int P10Floor;
        public int MedianFloor;
        public int P90Floor;
        public float LifeDepletedPercent;
        public float TimeOverPercent;
    }

    [Serializable]
    public sealed class LevelGrowthSimulationResult
    {
        public string CharacterId;
        public string CharacterName;
        public string SkillProfile;
        public int Population;
        public int MaxRuns;
        public int StartingLevel;
        public int StartingExperience;
        public int TargetLevel;
        public float RunsPerDay;
        public List<LevelGrowthMilestoneResult> Milestones = new List<LevelGrowthMilestoneResult>();
        public List<LevelGrowthCurvePoint> Curve = new List<LevelGrowthCurvePoint>();
        public List<LevelGrowthLevelResult> Levels = new List<LevelGrowthLevelResult>();
        public List<LevelGrowthRunResult> Runs = new List<LevelGrowthRunResult>();
        public List<string> Warnings = new List<string>();
    }

    internal sealed class LevelGrowthSimulationEngine
    {
        public LevelGrowthSimulationResult Run(LevelGrowthSimulationSettings settings)
        {
            Validate(settings);
            LevelBalanceSimulationSettings runSettings = settings.RunSettings;
            CharacterDefinition character = runSettings.Character;
            ItemTable itemTable = ItemTable.Load(runSettings.ItemTableCsv);
            SimulationProfileValues profile = LevelBalanceSimulationEngine.ResolveProfile(
                runSettings.SkillProfile);
            int population = Mathf.Clamp(settings.Population, 10, 5000);
            int maxRuns = Mathf.Clamp(settings.MaxRuns, 1, 2000);
            int startingLevel = Mathf.Clamp(
                settings.StartingLevel,
                1,
                character.MaxCharacterLevel);
            int startingExperience = Mathf.Max(0, settings.StartingExperience);
            NormalizeProgress(character, ref startingLevel, ref startingExperience);
            int targetLevel = Mathf.Clamp(
                settings.TargetLevel,
                startingLevel,
                character.MaxCharacterLevel);

            LevelGrowthSimulationResult result = new LevelGrowthSimulationResult
            {
                CharacterId = character.CharacterId,
                CharacterName = character.DisplayName,
                SkillProfile = runSettings.SkillProfile.ToString(),
                Population = population,
                MaxRuns = maxRuns,
                StartingLevel = startingLevel,
                StartingExperience = startingExperience,
                TargetLevel = targetLevel,
                RunsPerDay = Mathf.Max(0.1f, settings.RunsPerDay)
            };

            int[][] levelsByRun = CreateMatrix(maxRuns, population);
            int[][] floorsByRun = CreateMatrix(maxRuns, population);
            int[][] targetRuns = new int[population][];
            int[] milestones = BuildMilestones(startingLevel, targetLevel, character);
            RunRewardSettings rewardSettings = new RunRewardSettings();

            for (int sample = 0; sample < population; sample++)
            {
                int level = startingLevel;
                int experience = startingExperience;
                targetRuns[sample] = Enumerable.Repeat(-1, milestones.Length).ToArray();
                RecordReachedMilestones(level, 0, milestones, targetRuns[sample]);

                for (int runNumber = 1; runNumber <= maxRuns; runNumber++)
                {
                    int startLevel = level;
                    int startExperience = experience;
                    runSettings.CharacterLevel = level;
                    System.Random random = new System.Random(unchecked(
                        runSettings.Seed + sample * 1000003 + runNumber * 486187739));
                    LevelBalanceRunResult run = LevelBalanceSimulationEngine.SimulateRun(
                        runNumber,
                        runSettings,
                        itemTable,
                        profile,
                        random);
                    RunRewardBreakdown reward = RunRewardCalculator.Calculate(
                        rewardSettings,
                        character,
                        level,
                        1,
                        run.HighestFloor,
                        run.GameplayScore,
                        run.RemainingHearts,
                        run.GameMoney);
                    experience += reward.TotalExperience;
                    NormalizeProgress(character, ref level, ref experience);
                    RecordReachedMilestones(level, runNumber, milestones, targetRuns[sample]);

                    levelsByRun[runNumber - 1][sample] = level;
                    floorsByRun[runNumber - 1][sample] = run.HighestFloor;
                    result.Runs.Add(new LevelGrowthRunResult
                    {
                        SampleIndex = sample + 1,
                        RunNumber = runNumber,
                        StartLevel = startLevel,
                        StartExperience = startExperience,
                        ExperienceEarned = reward.TotalExperience,
                        EndLevel = level,
                        EndExperience = experience,
                        HighestFloor = run.HighestFloor,
                        GameplayScore = run.GameplayScore,
                        EndReason = run.EndReason
                    });
                }
            }

            PopulateCurve(result, levelsByRun, floorsByRun, targetLevel);
            PopulateMilestones(result, milestones, targetRuns);
            PopulateLevels(result);
            PopulateWarnings(result);
            return result;
        }

        private static int[][] CreateMatrix(int rows, int columns)
        {
            int[][] matrix = new int[rows][];
            for (int i = 0; i < rows; i++)
            {
                matrix[i] = new int[columns];
            }

            return matrix;
        }

        private static int[] BuildMilestones(
            int startingLevel,
            int targetLevel,
            CharacterDefinition character)
        {
            HashSet<int> levels = new HashSet<int> { targetLevel };
            int[] candidates = { 5, 10, 15, 20 };
            for (int i = 0; i < candidates.Length; i++)
            {
                if (candidates[i] > startingLevel
                    && candidates[i] <= targetLevel
                    && candidates[i] <= character.MaxCharacterLevel)
                {
                    levels.Add(candidates[i]);
                }
            }

            if (character.CharacterSkill != null
                && character.CharacterSkill.UnlockLevel > startingLevel
                && character.CharacterSkill.UnlockLevel <= targetLevel)
            {
                levels.Add(character.CharacterSkill.UnlockLevel);
            }

            return levels.OrderBy(value => value).ToArray();
        }

        private static void RecordReachedMilestones(
            int level,
            int runNumber,
            IReadOnlyList<int> milestones,
            IList<int> reachedRuns)
        {
            for (int i = 0; i < milestones.Count; i++)
            {
                if (reachedRuns[i] < 0 && level >= milestones[i])
                {
                    reachedRuns[i] = runNumber;
                }
            }
        }

        private static void PopulateCurve(
            LevelGrowthSimulationResult result,
            IReadOnlyList<int[]> levelsByRun,
            IReadOnlyList<int[]> floorsByRun,
            int targetLevel)
        {
            for (int i = 0; i < levelsByRun.Count; i++)
            {
                int[] levels = levelsByRun[i].OrderBy(value => value).ToArray();
                int[] floors = floorsByRun[i].OrderBy(value => value).ToArray();
                result.Curve.Add(new LevelGrowthCurvePoint
                {
                    RunNumber = i + 1,
                    P10Level = Percentile(levels, 0.1f),
                    MedianLevel = Percentile(levels, 0.5f),
                    P90Level = Percentile(levels, 0.9f),
                    P10Floor = Percentile(floors, 0.1f),
                    MedianFloor = Percentile(floors, 0.5f),
                    P90Floor = Percentile(floors, 0.9f),
                    TargetReachedPercent = levels.Count(value => value >= targetLevel)
                        * 100f / Mathf.Max(1, levels.Length)
                });
            }
        }

        private static void PopulateMilestones(
            LevelGrowthSimulationResult result,
            IReadOnlyList<int> milestones,
            IReadOnlyList<int[]> targetRuns)
        {
            for (int milestoneIndex = 0; milestoneIndex < milestones.Count; milestoneIndex++)
            {
                int[] reached = targetRuns
                    .Select(sample => sample[milestoneIndex])
                    .Where(run => run >= 0)
                    .OrderBy(run => run)
                    .ToArray();
                int median = reached.Length > 0 ? Percentile(reached, 0.5f) : -1;
                result.Milestones.Add(new LevelGrowthMilestoneResult
                {
                    Level = milestones[milestoneIndex],
                    ReachedPercent = reached.Length * 100f / Mathf.Max(1, result.Population),
                    P10Runs = reached.Length > 0 ? Percentile(reached, 0.1f) : -1,
                    MedianRuns = median,
                    P90Runs = reached.Length > 0 ? Percentile(reached, 0.9f) : -1,
                    MedianDays = median >= 0 ? median / result.RunsPerDay : -1f
                });
            }
        }

        private static void PopulateWarnings(LevelGrowthSimulationResult result)
        {
            LevelGrowthMilestoneResult target = result.Milestones.LastOrDefault();
            if (target == null || target.ReachedPercent < 50f)
            {
                result.Warnings.Add(
                    $"{result.MaxRuns}회 이내 목표 Lv.{result.TargetLevel} 도달률이 50% 미만입니다.");
            }

            LevelGrowthCurvePoint first = result.Curve.FirstOrDefault();
            LevelGrowthCurvePoint last = result.Curve.LastOrDefault();
            if (first != null && last != null && last.MedianFloor <= first.MedianFloor)
            {
                result.Warnings.Add(
                    "레벨 성장 후에도 P50 도달 층이 증가하지 않습니다. 성장 체감 또는 난도 모델을 확인하세요.");
            }
        }

        private static void PopulateLevels(LevelGrowthSimulationResult result)
        {
            IEnumerable<IGrouping<int, LevelGrowthRunResult>> groups = result.Runs
                .GroupBy(run => run.StartLevel)
                .OrderBy(group => group.Key);
            foreach (IGrouping<int, LevelGrowthRunResult> group in groups)
            {
                LevelGrowthRunResult[] runs = group.ToArray();
                int[] floors = runs
                    .Select(run => run.HighestFloor)
                    .OrderBy(value => value)
                    .ToArray();
                result.Levels.Add(new LevelGrowthLevelResult
                {
                    Level = group.Key,
                    Runs = runs.Length,
                    AverageExperience = (float)runs.Average(run => run.ExperienceEarned),
                    P10Floor = Percentile(floors, 0.1f),
                    MedianFloor = Percentile(floors, 0.5f),
                    P90Floor = Percentile(floors, 0.9f),
                    LifeDepletedPercent = runs.Count(run => run.EndReason == "LifeDepleted")
                        * 100f / Mathf.Max(1, runs.Length),
                    TimeOverPercent = runs.Count(run => run.EndReason == "TimeOver")
                        * 100f / Mathf.Max(1, runs.Length)
                });
            }
        }

        private static void NormalizeProgress(
            CharacterDefinition character,
            ref int level,
            ref int experience)
        {
            level = Mathf.Clamp(level, 1, character.MaxCharacterLevel);
            experience = Mathf.Max(0, experience);
            while (level < character.MaxCharacterLevel)
            {
                int required = character.GetRequiredExperienceForLevel(level);
                if (required <= 0 || experience < required)
                {
                    break;
                }

                experience -= required;
                level++;
            }

            if (level >= character.MaxCharacterLevel)
            {
                experience = 0;
            }
        }

        private static int Percentile(IReadOnlyList<int> sorted, float percentile)
        {
            if (sorted.Count == 0)
            {
                return 0;
            }

            int index = Mathf.Clamp(
                Mathf.CeilToInt(sorted.Count * Mathf.Clamp01(percentile)) - 1,
                0,
                sorted.Count - 1);
            return sorted[index];
        }

        private static void Validate(LevelGrowthSimulationSettings settings)
        {
            if (settings == null || settings.RunSettings == null)
            {
                throw new InvalidOperationException("성장 시뮬레이션 설정이 필요합니다.");
            }

            LevelBalanceSimulationEngine.Validate(settings.RunSettings);
        }
    }

    internal static class LevelGrowthReportExporter
    {
        [Serializable]
        private sealed class GrowthExportConfiguration
        {
            public string generatedAt;
            public string characterId;
            public string skillProfile;
            public int startingLevel;
            public int startingExperience;
            public int targetLevel;
            public int maxRuns;
            public int population;
            public float runsPerDay;
            public int seed;
            public int maximumFloor;
        }

        public static string Export(
            LevelGrowthSimulationSettings settings,
            LevelGrowthSimulationResult result)
        {
            if (settings == null || result == null)
            {
                throw new InvalidOperationException("내보낼 성장 시뮬레이션 결과가 없습니다.");
            }

            string root = Directory.GetParent(Application.dataPath)?.FullName
                ?? Application.dataPath;
            string output = Path.Combine(
                root,
                "Docs",
                "BalanceReports",
                DateTime.Now.ToString("yyyy-MM-dd_HHmmss_fff", CultureInfo.InvariantCulture)
                + "_"
                + Sanitize(result.CharacterId)
                + "_growth");
            Directory.CreateDirectory(output);
            WriteAll(settings, result, output);
            AssetDatabase.Refresh();
            return output;
        }

        public static string ExportComparison(
            IReadOnlyList<LevelGrowthSimulationSettings> settings,
            IReadOnlyList<LevelGrowthSimulationResult> results)
        {
            if (settings == null
                || results == null
                || settings.Count != results.Count
                || results.Count < 2)
            {
                throw new InvalidOperationException(
                    "통합 내보내기에는 둘 이상의 캐릭터 성장 결과와 동일 개수의 설정이 필요합니다.");
            }

            ValidateComparisonSettings(settings);

            string root = Directory.GetParent(Application.dataPath)?.FullName
                ?? Application.dataPath;
            string output = Path.Combine(
                root,
                "Docs",
                "BalanceReports",
                DateTime.Now.ToString("yyyy-MM-dd_HHmmss_fff", CultureInfo.InvariantCulture)
                + "_growth_comparison");
            Directory.CreateDirectory(output);
            for (int i = 0; i < results.Count; i++)
            {
                string characterFolder = Path.Combine(
                    output,
                    Sanitize(results[i].CharacterId));
                Directory.CreateDirectory(characterFolder);
                WriteAll(settings[i], results[i], characterFolder);
            }

            WriteComparisonCsv(results, output);
            WriteComparisonSummary(settings[0], results, output);
            WriteComparisonSvg(results, output);
            AssetDatabase.Refresh();
            return output;
        }

        private static void ValidateComparisonSettings(
            IReadOnlyList<LevelGrowthSimulationSettings> settings)
        {
            LevelGrowthSimulationSettings baseline = settings[0];
            for (int i = 1; i < settings.Count; i++)
            {
                LevelGrowthSimulationSettings candidate = settings[i];
                bool matches = candidate.StartingLevel == baseline.StartingLevel
                    && candidate.StartingExperience == baseline.StartingExperience
                    && candidate.TargetLevel == baseline.TargetLevel
                    && candidate.MaxRuns == baseline.MaxRuns
                    && candidate.Population == baseline.Population
                    && Mathf.Approximately(candidate.RunsPerDay, baseline.RunsPerDay)
                    && candidate.RunSettings.SkillProfile == baseline.RunSettings.SkillProfile
                    && candidate.RunSettings.Seed == baseline.RunSettings.Seed
                    && candidate.RunSettings.MaximumFloor == baseline.RunSettings.MaximumFloor;
                if (!matches)
                {
                    throw new InvalidOperationException(
                        "통합 비교 결과의 설정이 서로 다릅니다. 전체 캐릭터 반복 성장을 다시 실행하세요.");
                }
            }
        }

        private static void WriteAll(
            LevelGrowthSimulationSettings settings,
            LevelGrowthSimulationResult result,
            string output)
        {
            WriteConfiguration(settings, result, output);
            WriteRuns(result, output);
            WriteCurve(result, output);
            WriteLevels(result, output);
            WriteSummary(settings, result, output);
            WriteSvg(result, output);
        }

        private static void WriteComparisonCsv(
            IReadOnlyList<LevelGrowthSimulationResult> results,
            string output)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("CharacterId,CharacterName,TargetLevel,ReachedPercent,P10Runs,MedianRuns,P90Runs,MedianDays,FinalMedianLevel,FinalMedianFloor,FinalP90Floor");
            for (int i = 0; i < results.Count; i++)
            {
                LevelGrowthSimulationResult result = results[i];
                LevelGrowthMilestoneResult target = result.Milestones.LastOrDefault();
                LevelGrowthCurvePoint final = result.Curve.LastOrDefault();
                builder.Append(Csv(result.CharacterId)).Append(',')
                    .Append(Csv(result.CharacterName)).Append(',')
                    .Append(result.TargetLevel).Append(',')
                    .Append(Number(target != null ? target.ReachedPercent : 0f)).Append(',')
                    .Append(target != null ? target.P10Runs : -1).Append(',')
                    .Append(target != null ? target.MedianRuns : -1).Append(',')
                    .Append(target != null ? target.P90Runs : -1).Append(',')
                    .Append(Number(target != null ? target.MedianDays : -1f)).Append(',')
                    .Append(final != null ? final.MedianLevel : 0).Append(',')
                    .Append(final != null ? final.MedianFloor : 0).Append(',')
                    .AppendLine((final != null ? final.P90Floor : 0).ToString(CultureInfo.InvariantCulture));
            }

            Write(output, "growth-comparison.csv", builder.ToString());
        }

        private static void WriteComparisonSummary(
            LevelGrowthSimulationSettings settings,
            IReadOnlyList<LevelGrowthSimulationResult> results,
            string output)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# LootUp 전체 캐릭터 반복 성장 비교");
            builder.AppendLine();
            builder.AppendLine($"- 생성 시각: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine($"- 숙련도: {settings.RunSettings.SkillProfile}");
            builder.AppendLine($"- 시작: Lv.{results[0].StartingLevel} / XP {results[0].StartingExperience}");
            builder.AppendLine($"- 목표: Lv.{results[0].TargetLevel}");
            builder.AppendLine($"- 표본: 캐릭터당 {results[0].Population:N0}명 x {results[0].MaxRuns:N0}회");
            builder.AppendLine($"- Seed: {settings.RunSettings.Seed}");
            builder.AppendLine();
            builder.AppendLine("| 캐릭터 | 목표 도달률 | P10 | P50 | P90 | P50 일수 | 최종 P50 레벨 | 최종 P50 층 | 최종 P90 층 |");
            builder.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|");
            for (int i = 0; i < results.Count; i++)
            {
                LevelGrowthSimulationResult result = results[i];
                LevelGrowthMilestoneResult target = result.Milestones.LastOrDefault();
                LevelGrowthCurvePoint final = result.Curve.LastOrDefault();
                builder.AppendLine(
                    $"| {result.CharacterName} | {target?.ReachedPercent ?? 0f:0.0}% | {Format(target?.P10Runs ?? -1)} | {Format(target?.MedianRuns ?? -1)} | {Format(target?.P90Runs ?? -1)} | {(target != null && target.MedianDays >= 0f ? target.MedianDays.ToString("0.0", CultureInfo.InvariantCulture) : "-")} | {final?.MedianLevel ?? 0} | {final?.MedianFloor ?? 0}F | {final?.P90Floor ?? 0}F |");
            }

            builder.AppendLine();
            builder.AppendLine("> 캐릭터별 하위 폴더에는 설정 JSON, 원본 런·곡선·레벨 CSV, Markdown 요약과 SVG가 저장됩니다.");
            Write(output, "growth-comparison.md", builder.ToString());
        }

        private static void WriteComparisonSvg(
            IReadOnlyList<LevelGrowthSimulationResult> results,
            string output)
        {
            const float left = 80f;
            const float top = 90f;
            const float width = 1020f;
            const float height = 380f;
            string[] colors = { "#2878D0", "#138A72", "#E2A21A", "#C85B3C" };
            int maximumLevel = Mathf.Max(
                1,
                results.Max(result => result.Curve.Max(point => point.P90Level)));
            int maximumRuns = results.Max(result => result.MaxRuns);
            StringBuilder svg = new StringBuilder();
            svg.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1200\" height=\"600\" viewBox=\"0 0 1200 600\">");
            svg.AppendLine("<rect width=\"1200\" height=\"600\" fill=\"#F5F7FA\"/>");
            svg.AppendLine("<text x=\"60\" y=\"48\" font-family=\"Arial, sans-serif\" font-size=\"25\" font-weight=\"700\" fill=\"#17202A\">LOOTUP CHARACTER GROWTH COMPARISON</text>");
            svg.AppendLine($"<rect x=\"{left}\" y=\"{top}\" width=\"{width}\" height=\"{height}\" fill=\"#FFFFFF\" stroke=\"#D8DEE7\"/>");
            for (int tick = 0; tick <= 4; tick++)
            {
                float y = top + height - height * tick / 4f;
                int level = Mathf.RoundToInt(maximumLevel * tick / 4f);
                svg.AppendLine($"<line x1=\"{left}\" y1=\"{y}\" x2=\"{left + width}\" y2=\"{y}\" stroke=\"#E7EBF0\"/>");
                svg.AppendLine($"<text x=\"{left - 12}\" y=\"{y + 4}\" text-anchor=\"end\" font-family=\"Arial, sans-serif\" font-size=\"11\" fill=\"#5B6573\">Lv.{level}</text>");
            }

            for (int i = 0; i < results.Count; i++)
            {
                LevelGrowthSimulationResult result = results[i];
                StringBuilder points = new StringBuilder();
                for (int j = 0; j < result.Curve.Count; j++)
                {
                    LevelGrowthCurvePoint point = result.Curve[j];
                    float x = left + width * (point.RunNumber - 1f) / Mathf.Max(1f, maximumRuns - 1f);
                    float y = top + height * (1f - point.MedianLevel / (float)maximumLevel);
                    AppendPoint(points, x, y);
                }

                string color = colors[i % colors.Length];
                svg.AppendLine($"<polyline points=\"{points}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"3\"/>");
                float legendX = 80f + i * 265f;
                svg.AppendLine($"<line x1=\"{legendX}\" y1=\"525\" x2=\"{legendX + 28}\" y2=\"525\" stroke=\"{color}\" stroke-width=\"4\"/>");
                svg.AppendLine($"<text x=\"{legendX + 38}\" y=\"530\" font-family=\"Arial, sans-serif\" font-size=\"13\" fill=\"#17202A\">{Escape(result.CharacterName)}</text>");
            }

            svg.AppendLine("</svg>");
            Write(output, "growth-comparison.svg", svg.ToString());
        }

        private static void WriteConfiguration(
            LevelGrowthSimulationSettings settings,
            LevelGrowthSimulationResult result,
            string output)
        {
            GrowthExportConfiguration configuration = new GrowthExportConfiguration
            {
                generatedAt = DateTime.Now.ToString("O", CultureInfo.InvariantCulture),
                characterId = result.CharacterId,
                skillProfile = result.SkillProfile,
                startingLevel = result.StartingLevel,
                startingExperience = result.StartingExperience,
                targetLevel = result.TargetLevel,
                maxRuns = result.MaxRuns,
                population = result.Population,
                runsPerDay = result.RunsPerDay,
                seed = settings.RunSettings.Seed,
                maximumFloor = settings.RunSettings.MaximumFloor
            };
            Write(
                output,
                "growth-configuration.json",
                JsonUtility.ToJson(configuration, true));
        }

        private static void WriteRuns(LevelGrowthSimulationResult result, string output)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Sample,Run,StartLevel,StartXP,XPEarned,EndLevel,EndXP,HighestFloor,GameplayScore,EndReason");
            for (int i = 0; i < result.Runs.Count; i++)
            {
                LevelGrowthRunResult run = result.Runs[i];
                builder.Append(run.SampleIndex).Append(',')
                    .Append(run.RunNumber).Append(',')
                    .Append(run.StartLevel).Append(',')
                    .Append(run.StartExperience).Append(',')
                    .Append(run.ExperienceEarned).Append(',')
                    .Append(run.EndLevel).Append(',')
                    .Append(run.EndExperience).Append(',')
                    .Append(run.HighestFloor).Append(',')
                    .Append(run.GameplayScore).Append(',')
                    .AppendLine(run.EndReason);
            }

            Write(output, "growth-runs.csv", builder.ToString());
        }

        private static void WriteCurve(LevelGrowthSimulationResult result, string output)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Run,P10Level,MedianLevel,P90Level,P10Floor,MedianFloor,P90Floor,TargetReachedPercent");
            for (int i = 0; i < result.Curve.Count; i++)
            {
                LevelGrowthCurvePoint point = result.Curve[i];
                builder.Append(point.RunNumber).Append(',')
                    .Append(point.P10Level).Append(',')
                    .Append(point.MedianLevel).Append(',')
                    .Append(point.P90Level).Append(',')
                    .Append(point.P10Floor).Append(',')
                    .Append(point.MedianFloor).Append(',')
                    .Append(point.P90Floor).Append(',')
                    .AppendLine(point.TargetReachedPercent.ToString("0.###", CultureInfo.InvariantCulture));
            }

            Write(output, "growth-curve.csv", builder.ToString());
        }

        private static void WriteLevels(LevelGrowthSimulationResult result, string output)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Level,Runs,AverageXP,P10Floor,MedianFloor,P90Floor,LifeDepletedPercent,TimeOverPercent");
            for (int i = 0; i < result.Levels.Count; i++)
            {
                LevelGrowthLevelResult row = result.Levels[i];
                builder.Append(row.Level).Append(',')
                    .Append(row.Runs).Append(',')
                    .Append(row.AverageExperience.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                    .Append(row.P10Floor).Append(',')
                    .Append(row.MedianFloor).Append(',')
                    .Append(row.P90Floor).Append(',')
                    .Append(row.LifeDepletedPercent.ToString("0.###", CultureInfo.InvariantCulture)).Append(',')
                    .AppendLine(row.TimeOverPercent.ToString("0.###", CultureInfo.InvariantCulture));
            }

            Write(output, "growth-level-summary.csv", builder.ToString());
        }

        private static void WriteSummary(
            LevelGrowthSimulationSettings settings,
            LevelGrowthSimulationResult result,
            string output)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("# LootUp 반복 성장 시뮬레이션");
            builder.AppendLine();
            builder.AppendLine($"- 캐릭터: {result.CharacterName} ({result.CharacterId})");
            builder.AppendLine($"- 시작: Lv.{result.StartingLevel} / XP {result.StartingExperience}");
            builder.AppendLine($"- 목표: Lv.{result.TargetLevel}");
            builder.AppendLine($"- 표본: {result.Population:N0}명 × 최대 {result.MaxRuns:N0}회");
            builder.AppendLine($"- 일일 플레이 가정: {result.RunsPerDay:0.#}회");
            builder.AppendLine($"- 실제 보상 공식: `{nameof(RunRewardCalculator)}`");
            builder.AppendLine();
            builder.AppendLine("| 목표 레벨 | 도달률 | P10 횟수 | P50 횟수 | P90 횟수 | P50 일수 |");
            builder.AppendLine("|---:|---:|---:|---:|---:|---:|");
            for (int i = 0; i < result.Milestones.Count; i++)
            {
                LevelGrowthMilestoneResult row = result.Milestones[i];
                builder.AppendLine($"| {row.Level} | {row.ReachedPercent:0.0}% | {Format(row.P10Runs)} | {Format(row.MedianRuns)} | {Format(row.P90Runs)} | {(row.MedianDays >= 0f ? row.MedianDays.ToString("0.0", CultureInfo.InvariantCulture) : "-")} |");
            }

            builder.AppendLine();
            builder.AppendLine("## 레벨별 런 결과");
            builder.AppendLine();
            builder.AppendLine("| 레벨 | 런 수 | 평균 XP | P10 층 | P50 층 | P90 층 | HP 종료 | 시간 종료 |");
            builder.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|");
            for (int i = 0; i < result.Levels.Count; i++)
            {
                LevelGrowthLevelResult row = result.Levels[i];
                builder.AppendLine($"| {row.Level} | {row.Runs:N0} | {row.AverageExperience:0.0} | {row.P10Floor} | {row.MedianFloor} | {row.P90Floor} | {row.LifeDepletedPercent:0.0}% | {row.TimeOverPercent:0.0}% |");
            }

            builder.AppendLine();
            builder.AppendLine("> 수치는 확률 모델 결과이며 실제 플레이 로그로 보정해야 합니다. 성장 레벨에 따라 캐릭터 스킬 해금 효과가 런 결과에 반영됩니다.");
            Write(output, "growth-summary.md", builder.ToString());
        }

        private static void WriteSvg(LevelGrowthSimulationResult result, string output)
        {
            const float left = 80f;
            const float top = 90f;
            const float width = 1020f;
            const float height = 380f;
            int maxLevel = Mathf.Max(result.TargetLevel, result.Curve.Max(point => point.P90Level));
            StringBuilder median = new StringBuilder();
            StringBuilder p10 = new StringBuilder();
            StringBuilder p90 = new StringBuilder();
            for (int i = 0; i < result.Curve.Count; i++)
            {
                LevelGrowthCurvePoint point = result.Curve[i];
                float x = left + width * i / Mathf.Max(1, result.Curve.Count - 1f);
                AppendPoint(p10, x, top + height * (1f - point.P10Level / (float)maxLevel));
                AppendPoint(median, x, top + height * (1f - point.MedianLevel / (float)maxLevel));
                AppendPoint(p90, x, top + height * (1f - point.P90Level / (float)maxLevel));
            }

            StringBuilder svg = new StringBuilder();
            svg.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1200\" height=\"580\" viewBox=\"0 0 1200 580\">");
            svg.AppendLine("<rect width=\"1200\" height=\"580\" fill=\"#F5F7FA\"/>");
            svg.AppendLine($"<text x=\"60\" y=\"48\" font-family=\"Arial, sans-serif\" font-size=\"25\" font-weight=\"700\" fill=\"#17202A\">{Escape(result.CharacterName)} GROWTH CURVE</text>");
            svg.AppendLine($"<rect x=\"{left}\" y=\"{top}\" width=\"{width}\" height=\"{height}\" fill=\"#FFFFFF\" stroke=\"#D8DEE7\"/>");
            for (int tick = 0; tick <= 4; tick++)
            {
                float y = top + height - height * tick / 4f;
                int level = Mathf.RoundToInt(maxLevel * tick / 4f);
                svg.AppendLine($"<line x1=\"{left}\" y1=\"{y}\" x2=\"{left + width}\" y2=\"{y}\" stroke=\"#E7EBF0\"/>");
                svg.AppendLine($"<text x=\"{left - 12}\" y=\"{y + 4}\" text-anchor=\"end\" font-family=\"Arial, sans-serif\" font-size=\"11\" fill=\"#5B6573\">Lv.{level}</text>");
            }

            svg.AppendLine($"<polyline points=\"{p10}\" fill=\"none\" stroke=\"#93A4B8\" stroke-width=\"2\"/>");
            svg.AppendLine($"<polyline points=\"{median}\" fill=\"none\" stroke=\"#138A72\" stroke-width=\"4\"/>");
            svg.AppendLine($"<polyline points=\"{p90}\" fill=\"none\" stroke=\"#2878D0\" stroke-width=\"2\"/>");
            svg.AppendLine($"<text x=\"{left}\" y=\"520\" font-family=\"Arial, sans-serif\" font-size=\"13\" fill=\"#5B6573\">P10 #93A4B8 · P50 #138A72 · P90 #2878D0 · 최대 {result.MaxRuns}회</text>");
            svg.AppendLine("</svg>");
            Write(output, "growth-curve.svg", svg.ToString());
        }

        private static void AppendPoint(StringBuilder builder, float x, float y)
        {
            builder.AppendFormat(CultureInfo.InvariantCulture, "{0:0.##},{1:0.##} ", x, y);
        }

        private static string Format(int value)
        {
            return value >= 0 ? value.ToString(CultureInfo.InvariantCulture) : "-";
        }

        private static string Csv(string value)
        {
            string normalized = value ?? string.Empty;
            return $"\"{normalized.Replace("\"", "\"\"")}\"";
        }

        private static string Number(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Sanitize(string value)
        {
            string normalized = string.IsNullOrWhiteSpace(value)
                ? "character"
                : value.Trim();
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalid.Length; i++)
            {
                normalized = normalized.Replace(invalid[i], '_');
            }

            return normalized;
        }

        private static void Write(string directory, string name, string content)
        {
            File.WriteAllText(
                Path.Combine(directory, name),
                content,
                new UTF8Encoding(false));
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty)
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
        }
    }
}
