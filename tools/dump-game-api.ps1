<#
.SYNOPSIS
  Dumps Sea Power's gameplay API to a text file. Nothing is installed and nothing is run: the
  script only reads type and member names out of the assembly.

.DESCRIPTION
  Seapower-Scripts.dll is a normal .NET assembly, so its type, field and method names can be listed
  without the game, a compiler or any extra tooling. The result (game-api.txt) is what makes it
  possible to point the mod's hatch code at the real members instead of guessing.

  By default only types that look relevant are dumped (hatch, silo, cover, container, launcher,
  weapon, animation, blueprint). Use -Everything for all types; the file gets big.

.EXAMPLE
  .\tools\dump-game-api.ps1
  .\tools\dump-game-api.ps1 -Path "D:\Steam\steamapps\common\Sea Power\Sea Power_Data\Managed\Seapower-Scripts.dll"
#>
[CmdletBinding()]
param(
    [string]$Path,
    [string]$OutFile,
    [switch]$Everything
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Path)) {
    $roots = @(
        'C:\Program Files (x86)\Steam\steamapps\common\Sea Power',
        'C:\Program Files\Steam\steamapps\common\Sea Power',
        'D:\Steam\steamapps\common\Sea Power',
        'D:\SteamLibrary\steamapps\common\Sea Power',
        'E:\SteamLibrary\steamapps\common\Sea Power'
    )
    foreach ($root in $roots) {
        $candidate = Join-Path $root 'Sea Power_Data\Managed\Seapower-Scripts.dll'
        if (Test-Path -LiteralPath $candidate) { $Path = $candidate; break }
    }
}

if ([string]::IsNullOrWhiteSpace($Path)) {
    throw 'Could not find Seapower-Scripts.dll. Run again with -Path pointing at Sea Power_Data\Managed\Seapower-Scripts.dll'
}
if (-not (Test-Path -LiteralPath $Path)) { throw "Not found: $Path" }

if ([string]::IsNullOrWhiteSpace($OutFile)) {
    $OutFile = Join-Path (Split-Path $PSScriptRoot -Parent) 'game-api.txt'
}

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('Sea Power API dump')
$lines.Add("File: $Path")
$lines.Add("When: $(Get-Date -Format s)")
$lines.Add('')

# Reflection-only: the assembly is inspected, never executed and never loaded into the process.
$assembly = $null
try {
    $assembly = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($Path)
}
catch {
    $assembly = [System.Reflection.Assembly]::LoadFrom($Path)
}

$types = @()
try {
    $types = $assembly.GetTypes()
}
catch [System.Reflection.ReflectionTypeLoadException] {
    $types = @($_.Exception.Types | Where-Object { $null -ne $_ })
}
catch {
    $lines.Add('Could not enumerate types: ' + $_.Exception.Message)
}

$filter = 'hatch|silo|cover|lid|door|canopy|container|launcher|weapon|animation|blueprint|opening|shutter|magazine'
$interesting = New-Object System.Collections.Generic.List[object]
foreach ($type in $types) {
    if ($null -eq $type) { continue }
    $full = $type.FullName
    if ([string]::IsNullOrWhiteSpace($full)) { continue }
    if ($Everything -or ($full -match $filter)) { $interesting.Add($type) }
}

$lines.Add("Types in assembly: $($types.Count). Types dumped: $($interesting.Count).")
$lines.Add('')

$flags = [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::NonPublic
$flags = $flags -bor [System.Reflection.BindingFlags]::Instance
$flags = $flags -bor [System.Reflection.BindingFlags]::Static
$flags = $flags -bor [System.Reflection.BindingFlags]::DeclaredOnly

foreach ($type in ($interesting | Sort-Object FullName)) {
    $base = ''
    try { if ($null -ne $type.BaseType) { $base = ' : ' + $type.BaseType.FullName } } catch { }

    $lines.Add("TYPE $($type.FullName)$base")

    try {
        foreach ($field in $type.GetFields($flags)) {
            $lines.Add("   field    $($field.FieldType.Name) $($field.Name)")
        }
    } catch { }

    try {
        foreach ($property in $type.GetProperties($flags)) {
            $lines.Add("   property $($property.PropertyType.Name) $($property.Name)")
        }
    } catch { }

    try {
        foreach ($method in $type.GetMethods($flags)) {
            $parameters = @()
            foreach ($parameter in $method.GetParameters()) {
                $parameters += ($parameter.ParameterType.Name + ' ' + $parameter.Name)
            }
            $lines.Add("   method   $($method.ReturnType.Name) $($method.Name)($($parameters -join ', '))")
        }
    } catch { }

    $lines.Add('')
}

# Names survive in the metadata even when a type cannot be resolved, and animation clip names live
# there too. This is the safety net that catches e.g. "Hatch_Open".
$lines.Add('== raw names that look like a hatch or a launch animation ==')
$text = [System.Text.Encoding]::GetEncoding(28591).GetString([System.IO.File]::ReadAllBytes($Path))
$seen = New-Object System.Collections.Generic.HashSet[string]
foreach ($match in [regex]::Matches($text, '[A-Za-z_][A-Za-z0-9_]{3,60}')) {
    $name = $match.Value
    if ($name -notmatch 'Hatch|Silo|Cover|Door|Lid|Canopy|Shutter|Launch|VLS') { continue }
    if ($seen.Add($name)) { $lines.Add('   ' + $name) }
}

[System.IO.File]::WriteAllLines($OutFile, $lines)
Write-Host "Wrote $OutFile ($($lines.Count) lines)"
Write-Host 'Attach that file, not the DLL, if the DLL upload is rejected.'
