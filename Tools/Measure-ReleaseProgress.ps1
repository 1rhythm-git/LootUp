param(
    [datetime]$Date = (Get-Date).Date,
    [double]$ProposedHours = 0,
    [ValidateSet("", "STAB", "DESIGN", "SETTINGS", "MISSION", "MAIL", "UPGRADE", "SHOP", "RELEASE", "BUFFER")]
    [string]$TaskId = "",
    [string]$LogPath = (Join-Path $PSScriptRoot "../Docs/09_RELEASE_PROGRESS_LOG.csv"),
    [switch]$AsJson
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$releaseStart = [datetime]"2026-10-19"
$releaseEnd = [datetime]"2026-12-31"
$totalPlannedHours = 104.0
$targetDate = $Date.Date
$workstreamBudgets = [ordered]@{
    STAB = 6.0
    DESIGN = 14.0
    SETTINGS = 6.0
    MISSION = 12.0
    MAIL = 10.0
    UPGRADE = 12.0
    SHOP = 10.0
    RELEASE = 24.0
    BUFFER = 10.0
}

function Get-PlannedHoursForDate {
    param([datetime]$Value)

    if ($Value.Date -lt $releaseStart -or $Value.Date -gt $releaseEnd) {
        return 0.0
    }

    if ($Value.DayOfWeek -eq [DayOfWeek]::Saturday -or
        $Value.DayOfWeek -eq [DayOfWeek]::Sunday) {
        return 2.5
    }

    return 1.0
}

function Get-PlannedHoursInRange {
    param(
        [datetime]$Start,
        [datetime]$End
    )

    if ($End.Date -lt $Start.Date) {
        return 0.0
    }

    $sum = 0.0
    for ($cursor = $Start.Date; $cursor -le $End.Date; $cursor = $cursor.AddDays(1)) {
        $sum += Get-PlannedHoursForDate $cursor
    }

    return $sum
}

function Get-Percent {
    param(
        [double]$Actual,
        [double]$Planned
    )

    if ($Planned -le 0) {
        return 0.0
    }

    return [math]::Round($Actual / $Planned * 100.0, 1)
}

function Get-AchievementStatus {
    param(
        [double]$Percent,
        [double]$Planned
    )

    if ($targetDate -lt $releaseStart) {
        return "일정 시작 전"
    }

    if ($targetDate -gt $releaseEnd) {
        return "일정 종료"
    }

    if ($Planned -le 0) {
        return "계획 없음"
    }

    if ([math]::Abs($Percent - 100.0) -lt 0.05) {
        return "달성"
    }

    if ($Percent -gt 100.0) {
        return "초과 달성"
    }

    return "미달"
}

function Get-EarnedHours {
    param([object[]]$Items)

    $sum = 0.0
    foreach ($item in $Items) {
        $sum += [double]$item.EarnedHours
    }

    return $sum
}

$entries = @()
if (Test-Path -LiteralPath $LogPath) {
    $entries = @(Import-Csv -LiteralPath $LogPath)
}

$measuredEntries = @(
    $entries | Where-Object {
        $_.TaskId -ne "OUT_OF_SCOPE" -and
        $_.Workstream -ne "OUT_OF_SCOPE" -and
        -not [string]::IsNullOrWhiteSpace($_.Date)
    } | ForEach-Object {
        [pscustomobject]@{
            Date = [datetime]::ParseExact($_.Date, "yyyy-MM-dd", [Globalization.CultureInfo]::InvariantCulture).Date
            Workstream = $_.Workstream
            EarnedHours = [double]::Parse($_.EarnedHours, [Globalization.CultureInfo]::InvariantCulture)
        }
    }
)

$dayOffset = (([int]$targetDate.DayOfWeek + 6) % 7)
$weekStart = $targetDate.AddDays(-$dayOffset)
$weekEnd = $weekStart.AddDays(6)
$effectivePlanEnd = if ($targetDate -lt $releaseStart) {
    $releaseStart.AddDays(-1)
} elseif ($targetDate -gt $releaseEnd) {
    $releaseEnd
} else {
    $targetDate
}

$dailyPlanned = Get-PlannedHoursForDate $targetDate
$weeklyPlanned = Get-PlannedHoursInRange $weekStart $weekEnd
$cumulativePlanned = Get-PlannedHoursInRange $releaseStart $effectivePlanEnd
$dailyEarned = Get-EarnedHours @($measuredEntries | Where-Object Date -eq $targetDate)
$weeklyEarned = Get-EarnedHours @($measuredEntries | Where-Object { $_.Date -ge $weekStart -and $_.Date -le $weekEnd })
$cumulativeEarned = Get-EarnedHours @($measuredEntries | Where-Object Date -le $targetDate)

$dailyPercent = Get-Percent $dailyEarned $dailyPlanned
$weeklyPercent = Get-Percent $weeklyEarned $weeklyPlanned
$releasePercent = Get-Percent $cumulativeEarned $totalPlannedHours
$scheduleAdherencePercent = Get-Percent $cumulativeEarned $cumulativePlanned
$projectedDailyPercent = Get-Percent ($dailyEarned + [math]::Max(0.0, $ProposedHours)) $dailyPlanned
$selectedWorkstreamBudget = 0.0
$selectedWorkstreamEarned = 0.0
$selectedWorkstreamPercent = 0.0
$projectedWorkstreamPercent = 0.0
if (-not [string]::IsNullOrWhiteSpace($TaskId)) {
    $selectedWorkstreamBudget = [double]$workstreamBudgets[$TaskId]
    $selectedWorkstreamEarned = Get-EarnedHours @($measuredEntries | Where-Object Workstream -eq $TaskId)
    $selectedWorkstreamPercent = Get-Percent $selectedWorkstreamEarned $selectedWorkstreamBudget
    $projectedWorkstreamPercent = Get-Percent (
        $selectedWorkstreamEarned + [math]::Max(0.0, $ProposedHours)
    ) $selectedWorkstreamBudget
}

$result = [ordered]@{
    Date = $targetDate.ToString("yyyy-MM-dd")
    DailyTargetHours = [math]::Round($dailyPlanned, 1)
    DailyEarnedHours = [math]::Round($dailyEarned, 1)
    DailyPercent = $dailyPercent
    DailyStatus = Get-AchievementStatus $dailyPercent $dailyPlanned
    ProposedHours = [math]::Round([math]::Max(0.0, $ProposedHours), 1)
    ProjectedDailyPercent = $projectedDailyPercent
    ProjectedDailyStatus = Get-AchievementStatus $projectedDailyPercent $dailyPlanned
    WeekStart = $weekStart.ToString("yyyy-MM-dd")
    WeekEnd = $weekEnd.ToString("yyyy-MM-dd")
    WeeklyTargetHours = [math]::Round($weeklyPlanned, 1)
    WeeklyEarnedHours = [math]::Round($weeklyEarned, 1)
    WeeklyPercent = $weeklyPercent
    WeeklyStatus = Get-AchievementStatus $weeklyPercent $weeklyPlanned
    CumulativePlannedHours = [math]::Round($cumulativePlanned, 1)
    CumulativeEarnedHours = [math]::Round($cumulativeEarned, 1)
    ReleaseTotalHours = $totalPlannedHours
    ReleaseProgressPercent = $releasePercent
    ScheduleDeltaHours = [math]::Round($cumulativeEarned - $cumulativePlanned, 1)
    ScheduleAdherencePercent = $scheduleAdherencePercent
    SelectedWorkstream = $TaskId
    WorkstreamBudgetHours = [math]::Round($selectedWorkstreamBudget, 1)
    WorkstreamEarnedHours = [math]::Round($selectedWorkstreamEarned, 1)
    WorkstreamPercent = $selectedWorkstreamPercent
    ProjectedWorkstreamPercent = $projectedWorkstreamPercent
}

if ($AsJson) {
    [pscustomobject]$result | ConvertTo-Json
    exit 0
}

[pscustomobject]$result | Format-List
