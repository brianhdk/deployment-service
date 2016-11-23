[CmdletBinding()]
Param(
	[Parameter(Mandatory=$false)]
	[ValidateSet("Local", "Remote")]
	[string]$target = "Local"
)

$ErrorActionPreference = "Stop"
$script_directory = Split-Path -Parent $PSCommandPath

$settings = @{
    "src" = @{
        "deploymentservice" = Resolve-Path $script_directory\..\src\DeploymentService
    }
    "tools" = @{
        "nuget" = Resolve-Path $script_directory\..\.nuget\NuGet.exe
    }
}

# remove .nupkg files in script directory
Get-ChildItem $script_directory | Where-Object { $_.Extension -eq ".nupkg" } | Remove-Item

foreach ($project in $settings.src.Keys) {

    $projectDirectory = Resolve-Path $settings.src[$project]

    cd $projectDirectory

    # remove .nupkg files in project directory
    Get-ChildItem $projectDirectory | Where-Object { $_.Extension -eq ".nupkg" } | Remove-Item

	# https://docs.nuget.org/create/nuspec-reference
	$csproj = Get-ChildItem $projectDirectory | Where-Object { $_.Extension -eq ".csproj" }[0]

    if ($csproj -eq $null) {
        throw "No .csproj file found in $projectDirectory."
    }

	# https://docs.nuget.org/consume/command-line-reference
    &$settings.tools.nuget pack $csproj -Build -Properties Configuration=Release -IncludeReferencedProjects -MSBuildVersion 14
	
    # Move .nupkg to script directory
    Get-ChildItem $projectDirectory | Where-Object { $_.Extension -eq ".nupkg" } | Move-Item -Destination $script_directory -Force
}


Get-ChildItem $script_directory | Where-Object { $_.Extension -eq ".nupkg" } | ForEach {

	If ($target -eq "Remote") {

        Try {

		    &$settings.tools.nuget push $_.FullName 66666666-6666-6666-6666-666666666666 -Source http://nuget.vertica.dk/api/v2/package
        }
        Catch {

            $message = $_.Exception.Message
            
            If ($message.Contains("The server is configured to not allow overwriting packages that already exist.")) {

                Write-Host "WARNING: $message" -ForegroundColor Yellow
            }
            Else {

                Throw $_.Exception
            }
        }
	} 
    Else {

		Get-ChildItem $projectDirectory | Where-Object { $_.Extension -eq ".nupkg" } | Move-Item -Destination "D:\Dropbox\Development\NuGet.Packages" -Force
	}
}

cd $script_directory