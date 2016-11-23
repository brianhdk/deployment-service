[CmdletBinding()]
Param(
	[Parameter(Mandatory=$false)]
	[ValidateSet("Local", "Remote")]
	[string]$target = "Local"
)

$ErrorActionPreference = "Stop"
$script_directory = Split-Path -Parent $PSCommandPath

function Deploy-WindowsService ($localDirectory, $deploymentHelperUrl, $serviceName, $remoteLocalDirectory) {

    Assert ($localDirectory -ne $null) "Missing required argument 'localDirectory'"

    $localDirectory = Resolve-Path $localDirectory
    $zipFile = Join-Path $localDirectory ("..\" + (Get-Item $localDirectory).Name + ".zip")

	if (Test-Path $zipFile) {
        Remove-Item $zipFile -Force
	}

	Zip -from $localDirectory -toFile $zipFile

	$webclient = New-Object System.Net.WebClient 

	try {

        Write-Host "Deploying $serviceName to $remoteLocalDirectory through $deploymentHelperUrl"

		$webclient.UploadFile("$deploymentHelperUrl/windowsservice?serviceName=$serviceName&localDirectory=$remoteLocalDirectory", $zipFile)

        Write-Host "$serviceName has been deployed." -ForegroundColor Green
	}
	catch [Net.WebException] {

		$ex = $_.Exception

		$response = $ex.Response

		if ($response -eq $null) {
			Throw $ex
		}

		$reader = New-Object System.IO.StreamReader $ex.Response.GetResponseStream()
		$result = $reader.ReadToEnd();

		Throw $result
	}
}