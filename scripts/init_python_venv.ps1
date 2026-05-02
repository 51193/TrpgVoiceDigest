$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir
$PyDir = Join-Path $ProjectRoot "python"
$VenvDir = Join-Path $PyDir "venv"

$pythonCmd = $null
foreach ($cmd in @("python3", "python")) {
    $found = Get-Command $cmd -ErrorAction SilentlyContinue
    if ($found) {
        $pythonCmd = $cmd
        break
    }
}

if (-not $pythonCmd) {
    Write-Error "未找到 python，请先安装 Python 3。"
    exit 1
}

if (-not (Test-Path $VenvDir)) {
    Write-Host "创建虚拟环境: $VenvDir"
    & $pythonCmd -m venv $VenvDir
} else {
    Write-Host "复用已有虚拟环境: $VenvDir"
}

$venvPython = Join-Path $VenvDir "Scripts" "python.exe"
& $venvPython -m pip install --upgrade pip

Write-Host "安装 WhisperX 及依赖..."
$reqPath = Join-Path $PyDir "requirements.txt"
& $venvPython -m pip install -r $reqPath

$ffmpegFound = Get-Command ffmpeg -ErrorAction SilentlyContinue
if (-not $ffmpegFound) {
    Write-Host "警告: 未检测到 ffmpeg，可执行转录/录音可能失败。"
}

Write-Host ""
Write-Host "Python 环境准备完成。"
Write-Host "建议将 config/app.config.json 中 whisper.pythonExecutable 设置为:"
Write-Host "$VenvDir\Scripts\python.exe"
Write-Host ""
Write-Host "若需要使用说话者分离 (Speaker Diarization)，请:"
Write-Host "  1. 前往 https://huggingface.co/pyannote/speaker-diarization-3.1 接受用户协议"
Write-Host "  2. 前往 https://huggingface.co/pyannote/segmentation-3.0 接受用户协议"
Write-Host "  3. 在 https://huggingface.co/settings/tokens 创建 Access Token"
Write-Host "  4. 设置环境变量: `$env:HF_TOKEN=<your_token>"
