[CmdletBinding()]
param(
	[ValidateRange(0, 100)]
	[double]$MinimumLineCoverage = 80
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$results = Join-Path $root ('artifacts/coverage/' + [Guid]::NewGuid().ToString('N'))
Push-Location $root
try {
	& dotnet test JobHunterAI.slnx --collect:'XPlat Code Coverage' --settings coverage.runsettings --results-directory $results --verbosity quiet --nologo
	if ($LASTEXITCODE -ne 0) { throw 'Tests or coverage collection failed.' }
	$reports = @(Get-ChildItem $results -Recurse -Filter coverage.cobertura.xml)
	if ($reports.Count -lt 2) { throw 'Expected coverage reports from both test projects.' }
	$lines = @{}
	foreach ($report in $reports) {
		[xml]$xml = Get-Content $report.FullName -Raw
		foreach ($package in $xml.coverage.packages.package) {
			foreach ($class in $package.classes.class) {
				foreach ($line in $class.lines.line) {
					$key = $package.name + '|' + $class.filename.Replace('\', '/') + '|' + $line.number
					if (!$lines.ContainsKey($key)) { $lines[$key] = 0L }
					$lines[$key] += [long]$line.hits
				}
			}
		}
	}
	if ($lines.Count -eq 0) { throw 'No production coverage lines were collected.' }
	$assemblies = @($lines.Keys | Group-Object { ($_ -split '\|')[0] })
	foreach ($name in @('Domain', 'Application', 'Infrastructure', 'AI', 'Automation', 'Api', 'Web')) {
		if ('JobHunterAI.' + $name -notin $assemblies.Name) { throw "Missing production assembly: JobHunterAI.$name" }
	}
	$covered = @($lines.Values | Where-Object { $_ -gt 0 }).Count
	$percent = 100.0 * $covered / $lines.Count
	$details = @($assemblies | ForEach-Object {
		$hit = @($_.Group | Where-Object { $lines[$_] -gt 0 }).Count
		[pscustomobject]@{ Assembly = $_.Name; Covered = $hit; Total = $_.Count; Percent = [math]::Round(100.0 * $hit / $_.Count, 2) }
	} | Sort-Object Assembly)
	$details | Format-Table -AutoSize | Out-Host
	$summary = [pscustomobject]@{ Covered = $covered; Total = $lines.Count; Percent = [math]::Round($percent, 2); Minimum = $MinimumLineCoverage; Assemblies = $details }
	$summary | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $results 'summary.json')
	Write-Host ('Merged line coverage: {0:N2}% ({1}/{2}); reports: {3}' -f $percent, $covered, $lines.Count, $results)
	if ($percent -lt $MinimumLineCoverage) { throw "Line coverage is below $MinimumLineCoverage%." }
}
finally { Pop-Location }
